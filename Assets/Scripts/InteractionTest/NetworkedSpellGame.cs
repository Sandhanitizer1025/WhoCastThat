using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Networked, authority-driven game manager for "Who Cast That?!".
    ///
    /// This is the multiplayer replacement for the single-player
    /// <c>PotionGameManager</c> prototype (which it deliberately does not touch).
    /// The object's authority (the session owner, in this template's Distributed
    /// Authority setup) owns the shuffled deck and the turn order, resolves every
    /// draw/cast request, and broadcasts the result so a spell cast by one player
    /// actually lands on the target player and everyone sees whose turn it is.
    ///
    /// TURN STRUCTURE (Exploding-Kittens shaped):
    ///   1. Your turn opens in a PLAY PHASE — cast as many potions from your rack as
    ///      you like by dropping them in the play zone.
    ///   2. You END your turn by dipping a hand in the cauldron to draw.
    ///   3. Some spells end the turn without drawing (Phase), or hand the next player
    ///      several turns in a row (Hex).
    /// Players are dealt a starting hand so the play phase is usable from turn one.
    ///
    /// Distributed Authority notes:
    ///  - Authority checks use <see cref="NetworkBehaviour.HasAuthority"/>.
    ///  - Client -> authority requests use <c>[Rpc(SendTo.Authority)]</c>.
    ///  - Replicated state uses Owner-write / Everyone-read NetworkVariables.
    /// Place this on a NetworkObject in the scene and register the prefab/scene
    /// object with the Network Manager. UI subscribes to the static events.
    /// </summary>
    public class NetworkedSpellGame : NetworkBehaviour
    {
        public static NetworkedSpellGame Instance { get; private set; }

        [Tooltip("Players required before the match starts (and below which it ends).")]
        [SerializeField] private int minPlayersToStart = 2;

        [Header("Networked potions")]
        [Tooltip("Potion prefab (NetworkObject + NetworkedPotion). Must be registered with the Network Manager.")]
        [SerializeField] private GameObject networkedPotionPrefab;

        [Tooltip("One rack (testtube stand) per seat, in seat order. Drawn potions fill its 'Slot' children in order.")]
        [SerializeField] private Transform[] seatRacks;

        [Header("Opening hand")]
        [Tooltip("Potions dealt to each player before the first turn, so there is something to cast straight away.")]
        [SerializeField] private int startingHandSize = 5;

        [Tooltip("Guarantee one Counterspell in every opening hand, so nobody is knocked out by an early Curse.")]
        [SerializeField] private bool guaranteeStartingCounterspell = true;

        [Header("Cauldron brew")]
        [Tooltip("The floating cauldron rig; drawn potions spawn here and float out to the rack.")]
        [SerializeField] private Transform cauldronRig;

        [Tooltip("Seconds the ladle stirs after the player dips a hand, before the potion floats out.")]
        [SerializeField] private float stirDurationSeconds = 2.5f;

        [Tooltip("Seconds for a drawn potion to float from the pot into the rack slot.")]
        [SerializeField] private float potionFloatSeconds = 2f;

        [Tooltip("Seconds a freshly brewed potion hangs over the cauldron showing what it is, before floating to the rack.")]
        [SerializeField] private float drawRevealSeconds = 1.25f;

        [Header("Rack seating")]
        [Tooltip("Radius of the ring used to probe for the rack rim around a slot. Must clear the slot hole but stay inside the neighbouring slot.")]
        [SerializeField] private float rimProbeRadius = 0.026f;

        [Tooltip("How deep a seated potion sits below the rack rim. The rest of the tube protrudes so it can be seen and grabbed.")]
        [SerializeField] private float slotInsertionDepth = 0.045f;

        [Header("Interrupts")]
        [Tooltip("Seconds other players get to answer a cast spell with Dispel or Reflection. Skipped automatically if nobody holds one.")]
        [SerializeField] private float interruptWindowSeconds = 3f;

        [Tooltip("Safety cap on how long a Dispel/Reflection chain can get.")]
        [SerializeField] private int maxInterruptChain = 6;

        [Header("Diagnostics")]
        [Tooltip("Authority-side log of every cast decision and turn change. Turn this on to " +
                 "find out which branch ran when a turn behaves unexpectedly in a playtest; " +
                 "the log only appears on the authority's console.")]
        [SerializeField] private bool logCastDecisions = true;

        // ---- Replicated state (authority writes, everyone reads) ----

        // Seating, by client id. This is STABLE for the whole match: a player who is
        // eliminated or disconnects keeps their entry (and therefore their rack), and is
        // simply skipped. Removing entries mid-match would renumber every seat behind
        // them and silently reassign racks to the wrong players.
        private readonly NetworkList<ulong> turnOrder = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<int> currentTurnIndex = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Turns the current player still owes (Hex/attack stacking makes this > 1).
        private readonly NetworkVariable<int> turnsRemaining = new(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Size of the Hex the current player is under, or 0 if they are taking an ordinary turn.
        // This CANNOT be derived from turnsRemaining: a normal turn and the final owed turn of an
        // attack both read 1, yet hexing from the first must pass on 2 and from the second 4+.
        // Tracking the attack itself gives the 2 -> 4 -> 6 ladder regardless of when it is played.
        private readonly NetworkVariable<int> hexAttackSize = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> gameActive = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Shared status line every client displays.
        private readonly NetworkVariable<FixedString512Bytes> announcement = new(
            "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // True while the cauldron is stirring/brewing (drives the ladle animation everywhere
        // and blocks a second dip until the potion has floated out).
        private readonly NetworkVariable<bool> stirring = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // True while a cast spell is waiting to resolve and can still be answered.
        private readonly NetworkVariable<bool> interruptWindowOpen = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Replicated mirror of the authority's cursed set, so each client's HUD can tell that
        // player "you are cursed — play a Counterspell" instead of only the shared status line.
        private readonly NetworkList<ulong> cursedNetwork = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Authority-only guard so overlapping dips can't start two brews at once.
        private bool brewing;

        // Authority-only guard covering the WHOLE draw, not just the stir. `brewing` clears
        // the moment the ladle stops, but the turn does not pass until the potion has floated
        // all the way into the rack (reveal + float ~= 4 s later). Without this flag a second
        // dip inside that window passed every check and drew a second potion.
        private bool drawInProgress;

        // ---- Client-facing events (UI subscribes to these) ----
        public static event Action<string> AnnouncementChanged;
        public static event Action<ulong> TurnChanged;

        /// <summary>Raised only on the client who cast Foresight, with the top cards of the deck.</summary>
        public static event Action<PotionType[]> ForesightRevealed;

        public string CurrentAnnouncement => announcement.Value.ToString();
        public bool GameActive => gameActive.Value;

        /// <summary>Turns the current player still owes; > 1 while they are under a Hex.</summary>
        public int TurnsRemaining => turnsRemaining.Value;

        /// <summary>True while a cast spell can still be answered with Dispel or Reflection.</summary>
        public bool InterruptWindowOpen => interruptWindowOpen.Value;

        public ulong CurrentTurnClientId =>
            (turnOrder.Count > 0 && currentTurnIndex.Value >= 0 && currentTurnIndex.Value < turnOrder.Count)
                ? turnOrder[currentTurnIndex.Value]
                : ulong.MaxValue;

        public bool IsLocalPlayersTurn =>
            NetworkManager != null && CurrentTurnClientId == NetworkManager.LocalClientId;

        /// <summary>Seat (turn-order) index of the local player, or -1 if not seated yet.</summary>
        public int LocalSeatIndex =>
            NetworkManager != null ? GetSeatIndex(NetworkManager.LocalClientId) : -1;

        /// <summary>
        /// Could the local player legally cast this potion type right now? Presentation only —
        /// it drives the castable glow on a potion, and the authority still re-validates every
        /// cast. The cases mirror <see cref="PotionInfo.Timing"/> exactly, so the glow and the
        /// tooltip can never tell the player different things.
        /// </summary>
        public bool CanLocalPlayerCastNow(PotionType type)
        {
            if (!gameActive.Value || type == PotionType.Curse)
            {
                return false; // a Curse is only ever drawn, never cast
            }

            // Cursed: nothing but a Counterspell will save you, so nothing else is playable.
            if (IsLocalPlayerCursed)
            {
                return type == PotionType.Counterspell;
            }

            // A spell is waiting on the table. Answering it is the one thing that happens out
            // of turn — this is what a player wants to spot during someone else's turn.
            if (InterruptWindowOpen)
            {
                return type == PotionType.Dispel || type == PotionType.Reflection;
            }

            if (!IsLocalPlayersTurn)
            {
                return false;
            }

            // Your turn, nothing pending: everything except the answers, which need a spell to
            // answer, and the Counterspell, which needs a Curse.
            return type != PotionType.Dispel
                && type != PotionType.Reflection
                && type != PotionType.Counterspell;
        }

        /// <summary>Seat (turn-order) index whose turn it is, or -1 before the game starts.</summary>
        public int CurrentSeatIndex =>
            (gameActive.Value && turnOrder.Count > 0) ? currentTurnIndex.Value : -1;

        /// <summary>True while the cauldron is stirring (drives the ladle animation everywhere).</summary>
        public bool IsStirring => stirring.Value;

        /// <summary>Whether a fresh dip can start a brew right now (no brew already in progress).</summary>
        public bool CanBrew => gameActive.Value && !stirring.Value;

        /// <summary>Is this player currently cursed and owing a Counterspell?</summary>
        public bool IsPlayerCursed(ulong clientId)
        {
            for (int i = 0; i < cursedNetwork.Count; i++)
            {
                if (cursedNetwork[i] == clientId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Is the local player the one who owes a Counterspell?</summary>
        public bool IsLocalPlayerCursed =>
            NetworkManager != null && IsPlayerCursed(NetworkManager.LocalClientId);

        // ---- Curse bookkeeping: the authority's set and its replicated mirror move together ----

        private void AddCurse(ulong id)
        {
            if (cursedPlayers.Add(id))
            {
                cursedNetwork.Add(id);
            }
        }

        private void RemoveCurse(ulong id)
        {
            if (!cursedPlayers.Remove(id))
            {
                return;
            }
            for (int i = 0; i < cursedNetwork.Count; i++)
            {
                if (cursedNetwork[i] == id)
                {
                    cursedNetwork.RemoveAt(i);
                    return;
                }
            }
        }

        private void ClearCurses()
        {
            cursedPlayers.Clear();
            cursedNetwork.Clear();
        }

        /// <summary>0-based seat (turn-order) index for a client, or -1 if not yet known.</summary>
        public int GetSeatIndex(ulong clientId)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (turnOrder[i] == clientId)
                {
                    return i;
                }
            }
            return -1;
        }

        // ---- Authority-only hidden state ----
        private readonly List<PotionType> deck = new();
        private readonly HashSet<ulong> cursedPlayers = new();
        private readonly HashSet<ulong> eliminated = new();

        // Which potion is sitting in which rack slot, per seat. A slot stays reserved while
        // its potion is held by a player, and is released when the potion despawns.
        private readonly Dictionary<int, NetworkedPotion[]> rackSlots = new();
        private readonly Dictionary<int, Transform[]> slotTransformCache = new();

        private float cachedPotionHalfHeight = -1f;

        // A spell that has been cast but not yet resolved, while others may answer it.
        private struct PendingSpell
        {
            public PotionType Type;
            public ulong Caster;
            public ulong Target;
            public bool Cancelled;
            public int Interrupts;
        }

        // A Reflection copies the spell on the table: the same effect fires again, as though the
        // reflector had cast it themselves. Copies are collected during the interrupt window and
        // applied after the original resolves, so a Dispel landing later still wipes the whole
        // stack — "any card beneath a Dispel never existed" covers the copies too.
        private readonly List<ulong> pendingCopyCasters = new();

        private PendingSpell pending;
        private bool pendingActive;
        private Coroutine windowRoutine;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            announcement.OnValueChanged += OnAnnouncementValueChanged;
            currentTurnIndex.OnValueChanged += OnTurnIndexValueChanged;

            if (HasAuthority)
            {
                NetworkManager.OnConnectionEvent += OnConnectionEvent;
                StartGame();
            }

            // Fire initial state for late subscribers / joiners.
            AnnouncementChanged?.Invoke(CurrentAnnouncement);
            TurnChanged?.Invoke(CurrentTurnClientId);
        }

        public override void OnNetworkDespawn()
        {
            announcement.OnValueChanged -= OnAnnouncementValueChanged;
            currentTurnIndex.OnValueChanged -= OnTurnIndexValueChanged;

            if (HasAuthority && NetworkManager != null)
            {
                NetworkManager.OnConnectionEvent -= OnConnectionEvent;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnAnnouncementValueChanged(FixedString512Bytes previous, FixedString512Bytes current)
        {
            AnnouncementChanged?.Invoke(current.ToString());
        }

        private void OnTurnIndexValueChanged(int previous, int current)
        {
            TurnChanged?.Invoke(CurrentTurnClientId);
        }

        // ===================== AUTHORITY: game setup =====================

        [ContextMenu("Start / Restart Game")]
        public void StartGame()
        {
            if (!HasAuthority)
            {
                return;
            }

            SyncSeatingWithConnectedClients();

            currentTurnIndex.Value = 0;
            turnsRemaining.Value = 1;
            hexAttackSize.Value = 0;
            stirring.Value = false;
            interruptWindowOpen.Value = false;
            brewing = false;
            drawInProgress = false;
            pendingActive = false;
            pendingCopyCasters.Clear();
            ClearCurses();
            eliminated.Clear();

            ClearAllPotions();
            rackSlots.Clear();

            gameActive.Value = turnOrder.Count >= minPlayersToStart;

            if (gameActive.Value)
            {
                // Curses are added only AFTER the opening hands are dealt, so nobody can be
                // dealt one — the same reason Exploding Kittens holds its kittens back.
                BuildDeckWithoutCurses();
                DealStartingHands();
                AddCards(PotionType.Curse, 4);
                ShuffleDeck();
                AnnounceCurrentTurn();
            }
            else
            {
                SetAnnouncement($"Waiting for players... ({turnOrder.Count}/{minPlayersToStart})");
            }
        }

        private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
        {
            if (!HasAuthority)
            {
                return;
            }

            SyncSeatingWithConnectedClients();

            if (!gameActive.Value)
            {
                if (turnOrder.Count >= minPlayersToStart)
                {
                    StartGame();
                }
                else
                {
                    SetAnnouncement($"Waiting for players... ({turnOrder.Count}/{minPlayersToStart})");
                }
                return;
            }

            CheckForWinner();
        }

        /// <summary>
        /// Before the match starts, seating tracks whoever is connected. Once it is running,
        /// seats are frozen: a player who drops out is marked eliminated rather than removed,
        /// so everyone else keeps the rack they have been filling.
        /// </summary>
        private void SyncSeatingWithConnectedClients()
        {
            var connected = new HashSet<ulong>(NetworkManager.ConnectedClientsIds);

            if (!gameActive.Value)
            {
                for (int i = turnOrder.Count - 1; i >= 0; i--)
                {
                    if (!connected.Contains(turnOrder[i]))
                    {
                        turnOrder.RemoveAt(i);
                    }
                }
                foreach (ulong id in connected)
                {
                    if (IndexOfPlayer(id) < 0)
                    {
                        turnOrder.Add(id);
                    }
                }
                if (turnOrder.Count > 0 && currentTurnIndex.Value >= turnOrder.Count)
                {
                    currentTurnIndex.Value %= turnOrder.Count;
                }
                return;
            }

            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong id = turnOrder[i];
                if (!connected.Contains(id) && !eliminated.Contains(id))
                {
                    eliminated.Add(id);
                    RemoveCurse(id);
                }
            }

            // Late joiners take any spare seat and are dealt in. Appending is safe — it is
            // only REMOVAL that renumbers seats — and without this, players who connect a
            // moment after the match starts (the normal case with several MPPM players)
            // would be locked out of their own game.
            int maxSeats = seatRacks != null ? seatRacks.Length : 4;
            foreach (ulong id in connected)
            {
                if (IndexOfPlayer(id) >= 0)
                {
                    continue;
                }

                if (turnOrder.Count < maxSeats)
                {
                    turnOrder.Add(id);
                    DealHandTo(id);
                    SetAnnouncement($"{PlayerLabel(id)} pulls up a seat.");
                    continue;
                }

                // Every seat is taken, but some belong to players who have dropped out. A
                // reconnecting player comes back with a BRAND NEW client id, so their old seat
                // is a ghost that can never be matched to them again — and once four such
                // ghosts pile up, nobody can rejoin at all. Hand the newcomer a vacant seat by
                // OVERWRITING it in place: replacing an entry keeps every seat index (and so
                // every rack) stable, which removing one would not.
                int vacant = FirstVacantSeat(connected);
                if (vacant < 0)
                {
                    continue; // genuinely full of live players
                }

                ulong departed = turnOrder[vacant];
                eliminated.Remove(departed);
                RemoveCurse(departed);
                ClearSeatPotions(vacant); // the ghost's hand goes with them

                turnOrder[vacant] = id;
                DealHandTo(id);
                SetAnnouncement($"{PlayerLabel(id)} takes a vacant seat.");
            }
        }

        /// <summary>Despawn every potion sitting in a seat's rack and free its slots.</summary>
        private void ClearSeatPotions(int seat)
        {
            if (seat < 0 || !rackSlots.TryGetValue(seat, out NetworkedPotion[] arr))
            {
                return;
            }

            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null)
                {
                    NetworkedPotion doomed = arr[i];
                    arr[i] = null;
                    doomed.RackSeat = -1;
                    doomed.RackSlot = -1;
                    DespawnPotion(doomed);
                }
            }
        }

        // A seat whose occupant is no longer connected, so a newcomer can take it over.
        private int FirstVacantSeat(HashSet<ulong> connected)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (!connected.Contains(turnOrder[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        private int IndexOfPlayer(ulong id)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (turnOrder[i] == id)
                {
                    return i;
                }
            }
            return -1;
        }

        private int ActivePlayerCount()
        {
            int n = 0;
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (!eliminated.Contains(turnOrder[i]))
                {
                    n++;
                }
            }
            return n;
        }

        private void BuildDeckWithoutCurses()
        {
            deck.Clear();
            AddCards(PotionType.Hex, 5);
            AddCards(PotionType.Tribute, 4);
            AddCards(PotionType.Dispel, 4);
            AddCards(PotionType.Foresight, 5);
            AddCards(PotionType.Warp, 4);
            AddCards(PotionType.Phase, 4);
            AddCards(PotionType.Reflection, 4);
            AddCards(PotionType.Counterspell, 6);
            ShuffleDeck();
        }

        private void AddCards(PotionType type, int count)
        {
            for (int i = 0; i < count; i++)
            {
                deck.Add(type);
            }
        }

        private void ShuffleDeck()
        {
            for (int i = 0; i < deck.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, deck.Count);
                (deck[i], deck[r]) = (deck[r], deck[i]);
            }
        }

        private bool TakeSpecificFromDeck(PotionType type)
        {
            for (int i = 0; i < deck.Count; i++)
            {
                if (deck[i] == type)
                {
                    deck.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private void DealStartingHands()
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                DealHandTo(turnOrder[i]);
            }
        }

        private void DealHandTo(ulong playerId)
        {
            int dealt = 0;

            if (guaranteeStartingCounterspell && TakeSpecificFromDeck(PotionType.Counterspell))
            {
                SpawnPotionForPlayer(PotionType.Counterspell, playerId, false, null);
                dealt++;
            }

            // A Curse is a threat you draw, never a card you are dealt. The opening deal
            // happens before Curses are added, but a late joiner is dealt from the live
            // deck, so any Curse turned up here goes back and the deck is reshuffled.
            var setAside = new List<PotionType>();
            while (dealt < startingHandSize && deck.Count > 0)
            {
                PotionType type = deck[0];
                deck.RemoveAt(0);

                if (type == PotionType.Curse)
                {
                    setAside.Add(type);
                    continue;
                }

                SpawnPotionForPlayer(type, playerId, false, null);
                dealt++;
            }

            if (setAside.Count > 0)
            {
                deck.AddRange(setAside);
                ShuffleDeck();
            }
        }

        // Despawn every potion still in the world (a restart must not leave the racks full).
        private void ClearAllPotions()
        {
            // Collect first: despawning while iterating a FindObjectsByType array invalidates it.
            var potions = UnityEngine.Object.FindObjectsByType<NetworkedPotion>(FindObjectsSortMode.None);
            var doomed = new List<NetworkedPotion>(potions);
            for (int i = 0; i < doomed.Count; i++)
            {
                DespawnPotion(doomed[i]);
            }
        }

        private void DespawnPotion(NetworkedPotion potion)
        {
            if (potion == null)
            {
                return;
            }

            NetworkObject netObj = potion.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
            {
                return;
            }

            if (netObj.IsOwner)
            {
                netObj.Despawn();
            }
            else
            {
                // A player has grabbed it, so ownership moved to them; ask them to despawn it.
                potion.DespawnRpc();
            }
        }

        // ===================== Draw (client -> authority) =====================

        /// <summary>
        /// Instant draw with no brewing (used by the keyboard test harness). The normal
        /// in-world path is to dip a hand in the cauldron via <see cref="RequestBrew"/>.
        /// </summary>
        public void RequestDraw()
        {
            RequestDrawRpc(NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Authority)]
        private void RequestDrawRpc(ulong playerId)
        {
            if (brewing || !CanPlayerDraw(playerId))
            {
                return;
            }
            drawInProgress = true; // held until the turn actually passes
            PerformDraw(playerId, false);
        }

        // ===================== Brew (client -> authority) =====================

        /// <summary>
        /// Called when the local player dips a hand in the cauldron. The authority
        /// stirs the ladle for a moment, then a potion floats out to the rack.
        /// </summary>
        public void RequestBrew()
        {
            RequestBrewRpc(NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Authority)]
        private void RequestBrewRpc(ulong playerId)
        {
            if (brewing || !CanPlayerDraw(playerId))
            {
                return; // already brewing, or not a legal draw right now
            }

            brewing = true;
            drawInProgress = true; // held until the turn actually passes
            stirring.Value = true; // drives the ladle animation on every client
            SetAnnouncement($"{PlayerLabel(playerId)} stirs the cauldron...");
            StartCoroutine(BrewRoutine(playerId));
        }

        // Authority-only: stir for a beat, then draw the potion (which floats out).
        private IEnumerator BrewRoutine(ulong playerId)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, stirDurationSeconds));

            stirring.Value = false;
            brewing = false;

            // The player may have left or been interrupted (e.g. cursed) mid-brew.
            if (!DrawPreconditionsMet(playerId))
            {
                drawInProgress = false;
                yield break;
            }
            PerformDraw(playerId, true);
        }

        // Shared draw preconditions for both the instant and brew-driven paths.
        private bool CanPlayerDraw(ulong playerId)
        {
            return !drawInProgress && DrawPreconditionsMet(playerId);
        }

        // The same checks WITHOUT the in-progress guard. The mid-brew re-check needs these,
        // because by then the draw in progress is this player's own and would fail the guard.
        private bool DrawPreconditionsMet(ulong playerId)
        {
            if (!HasAuthority || !gameActive.Value)
            {
                return false;
            }
            if (CurrentTurnClientId != playerId || eliminated.Contains(playerId))
            {
                return false; // not this player's turn
            }
            if (pendingActive)
            {
                return false; // a spell is still resolving
            }
            if (cursedPlayers.Contains(playerId))
            {
                SetAnnouncement($"{PlayerLabel(playerId)} must play a Counterspell before drawing!");
                return false;
            }
            if (deck.Count == 0)
            {
                SetAnnouncement("The cauldron is empty!");
                return false;
            }

            // Hand limit: a rack holds 8 tubes. Drawing with no free slot used to dump the
            // potion loose on the table, which lost track of it. The way out is to cast
            // something first — the player always can, since a full rack means 8 potions.
            int seat = GetSeatIndex(playerId);
            if (seat >= 0 && FirstFreeSlot(seat) < 0)
            {
                SetAnnouncement($"{PlayerLabel(playerId)}'s rack is full — cast a potion before drawing.");
                return false;
            }

            return true;
        }

        // Authority-only: take the top card and resolve it. Drawing is how a turn ENDS,
        // so the turn only passes once the potion has actually settled in the rack.
        private void PerformDraw(ulong playerId, bool floatFromPot)
        {
            PotionType drawn = deck[0];
            deck.RemoveAt(0);

            if (drawn == PotionType.Curse)
            {
                // A Curse is never added to a hand — it is an immediate threat.
                if (!PlayerHolds(playerId, PotionType.Counterspell))
                {
                    SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE with no Counterspell — destroyed!");
                    EliminatePlayer(playerId);
                    return;
                }

                AddCurse(playerId);
                SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE! Play a Counterspell to survive.");
                return; // the turn does not pass until the curse is answered
            }

            SetAnnouncement($"{PlayerLabel(playerId)} draws from the cauldron...");
            SpawnPotionForPlayer(drawn, playerId, floatFromPot, EndTurnAfterAction);
        }

        // ===================== Racks =====================

        private Transform[] SlotTransformsForSeat(int seat)
        {
            if (slotTransformCache.TryGetValue(seat, out Transform[] cached))
            {
                return cached;
            }

            Transform rack = RackForSeat(seat);
            var list = new List<Transform>();
            if (rack != null)
            {
                foreach (Transform child in rack)
                {
                    if (child.name.StartsWith("Slot"))
                    {
                        list.Add(child);
                    }
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            Transform[] result = list.ToArray();
            slotTransformCache[seat] = result;
            return result;
        }

        private Transform RackForSeat(int seat)
        {
            return (seatRacks != null && seat >= 0 && seat < seatRacks.Length) ? seatRacks[seat] : null;
        }

        private NetworkedPotion[] SlotsForSeat(int seat)
        {
            int count = Mathf.Max(1, SlotTransformsForSeat(seat).Length);
            if (rackSlots.TryGetValue(seat, out NetworkedPotion[] arr) && arr.Length == count)
            {
                return arr;
            }
            arr = new NetworkedPotion[count];
            rackSlots[seat] = arr;
            return arr;
        }

        private int FirstFreeSlot(int seat)
        {
            NetworkedPotion[] arr = SlotsForSeat(seat);
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Does this player still have a potion of the given type (racked or in hand)?</summary>
        private bool PlayerHolds(ulong playerId, PotionType type)
        {
            int seat = GetSeatIndex(playerId);
            if (seat < 0)
            {
                return false;
            }

            NetworkedPotion[] arr = SlotsForSeat(seat);
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null && arr[i].Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Called by a potion as it despawns so its rack slot is freed for the next draw.</summary>
        public void NotifyPotionDespawned(NetworkedPotion potion)
        {
            if (!HasAuthority || potion == null)
            {
                return;
            }

            int seat = potion.RackSeat;
            int slot = potion.RackSlot;
            if (seat < 0 || slot < 0)
            {
                return;
            }

            if (rackSlots.TryGetValue(seat, out NetworkedPotion[] arr) && slot < arr.Length && arr[slot] == potion)
            {
                arr[slot] = null;
            }
        }

        private float PotionHalfHeight()
        {
            if (cachedPotionHalfHeight > 0f)
            {
                return cachedPotionHalfHeight;
            }

            float half = 0.06f;
            if (networkedPotionPrefab != null)
            {
                Renderer[] renderers = networkedPotionPrefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        b.Encapsulate(renderers[i].bounds);
                    }
                    if (b.size.y > 0.001f)
                    {
                        half = b.size.y * 0.5f;
                    }
                }
            }

            cachedPotionHalfHeight = half;
            return half;
        }

        // Seat a tube in a rack slot so it PROTRUDES above the rack rim.
        //
        // The rack's slots are wells roughly 0.18 m deep while the tube is only ~0.12 m
        // tall, so a potion resting on the well floor disappears inside the rack entirely.
        // No rack scale fixes that: scaling changes the hole width and the well depth
        // together, so a correctly-proportioned tube is always shorter than its well.
        // Instead we measure the rim around the slot and hang the tube just inside it —
        // the potion is then frozen in place by NetworkedPotion.SetRacked.
        private Vector3 RestPositionInSlot(Transform slot)
        {
            float halfHeight = PotionHalfHeight();

            const int samples = 12;
            float rim = float.NegativeInfinity;
            for (int a = 0; a < samples; a++)
            {
                float angle = a * (360f / samples) * Mathf.Deg2Rad;
                Vector3 origin = slot.position + new Vector3(
                    Mathf.Cos(angle) * rimProbeRadius, 0.3f, Mathf.Sin(angle) * rimProbeRadius);

                RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.8f);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i].collider.GetComponentInParent<NetworkedPotion>() != null)
                    {
                        continue; // a tube already standing here is not the rack
                    }
                    if (hits[i].point.y > rim)
                    {
                        rim = hits[i].point.y;
                    }
                }
            }

            if (float.IsNegativeInfinity(rim))
            {
                return slot.position + Vector3.up * halfHeight;
            }

            return new Vector3(slot.position.x, rim - slotInsertionDepth + halfHeight, slot.position.z);
        }

        // Authority-only: spawn a networked potion of the given type into the player's rack.
        // It is grabbable; grabbing transfers ownership via the NetworkPhysicsInteractable
        // on the prefab and releases it back to physics.
        private void SpawnPotionForPlayer(PotionType type, ulong playerId, bool floatFromPot, Action onSeated)
        {
            if (networkedPotionPrefab == null)
            {
                Debug.LogWarning("[NetworkedSpellGame] No networked potion prefab assigned.", this);
                onSeated?.Invoke();
                return;
            }

            int seat = GetSeatIndex(playerId);
            Transform rack = RackForSeat(seat);
            Transform[] slotTransforms = SlotTransformsForSeat(seat);
            int slotIndex = seat >= 0 ? FirstFreeSlot(seat) : -1;

            Vector3 restPos;
            Quaternion restRot = Quaternion.identity;

            if (slotIndex >= 0 && slotIndex < slotTransforms.Length)
            {
                Transform slot = slotTransforms[slotIndex];
                restPos = RestPositionInSlot(slot);
                // Yaw only — the tube must stand upright even if the slot pivot is tilted.
                restRot = Quaternion.Euler(0f, slot.eulerAngles.y, 0f);
            }
            else if (rack != null)
            {
                // Rack full: set it down just in front so it is still reachable and castable.
                restPos = rack.position + rack.forward * 0.08f + Vector3.up * PotionHalfHeight();
                SetAnnouncement($"{PlayerLabel(playerId)}'s rack is full — potion set down beside it.");
            }
            else
            {
                restPos = transform.position;
            }

            bool canFloat = floatFromPot && cauldronRig != null;
            Vector3 spawnPos = canFloat ? cauldronRig.position + Vector3.up * 0.12f : restPos;

            GameObject potionObject = Instantiate(networkedPotionPrefab, spawnPos, restRot);
            NetworkObject netObj = potionObject.GetComponent<NetworkObject>();
            netObj.Spawn();

            NetworkedPotion potion = potionObject.GetComponent<NetworkedPotion>();
            if (potion != null)
            {
                potion.SetType(type);

                // Lets each client tell its own potions from everyone else's, so the hover
                // tooltip never describes an opponent's hand.
                potion.SetOwnerSeat(seat);

                // Where this potion snaps back to if it is ever dropped outside the play zone.
                potion.SetHome(restPos, restRot);

                if (slotIndex >= 0)
                {
                    NetworkedPotion[] arr = SlotsForSeat(seat);
                    if (slotIndex < arr.Length)
                    {
                        arr[slotIndex] = potion;
                        potion.RackSeat = seat;
                        potion.RackSlot = slotIndex;
                    }
                }
            }

            if (canFloat)
            {
                // Authority owns the fresh potion; ClientNetworkTransform replicates
                // this owner-driven motion so every client sees it float to the rack.
                StartCoroutine(FloatPotionToRack(potion, spawnPos, restPos, onSeated));
            }
            else
            {
                potion?.SetRacked(true);
                onSeated?.Invoke();
            }
        }

        private IEnumerator FloatPotionToRack(NetworkedPotion potion, Vector3 from, Vector3 to, Action onArrived)
        {
            Transform tf = potion != null ? potion.transform : null;
            Rigidbody body = potion != null ? potion.GetComponent<Rigidbody>() : null;

            // Kinematic for the trip so physics doesn't fight our per-frame position writes.
            if (body != null)
            {
                body.isKinematic = true;
            }

            // Hold it over the cauldron so everyone can read what was brewed. The reveal is
            // broadcast rather than shown locally, because the whole table should learn what
            // was drawn at the same moment.
            float reveal = Mathf.Max(0f, drawRevealSeconds);
            if (reveal > 0f && potion != null)
            {
                potion.RevealRpc((int)potion.Type, reveal);

                float held = 0f;
                while (potion != null && held < reveal)
                {
                    held += Time.deltaTime;
                    // A slow bob so it reads as suspended, not frozen.
                    float bob = Mathf.Sin(held * 3f) * 0.012f;
                    Vector3 hover = from + Vector3.up * bob;
                    tf.position = hover;
                    if (body != null)
                    {
                        body.position = hover;
                    }
                    yield return null;
                }
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, potionFloatSeconds);
            while (tf != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float eased = u * u * (3f - 2f * u); // smoothstep
                Vector3 pos = Vector3.Lerp(from, to, eased);
                pos.y += Mathf.Sin(u * Mathf.PI) * 0.08f; // gentle arc lift
                tf.position = pos;
                if (body != null)
                {
                    body.position = pos; // keep the rigidbody in step so the sync is clean
                }
                yield return null;
            }

            if (tf != null)
            {
                tf.position = to;
            }
            if (body != null)
            {
                body.position = to;
            }

            // Freeze it in the slot rather than handing it back to gravity — otherwise it
            // drops straight through the rim into the bottom of the well and vanishes.
            potion?.SetRacked(true);

            onArrived?.Invoke();
        }

        // ===================== Cast (client -> authority) =====================

        /// <summary>Sentinel for "this cast has no physical potion behind it" (keyboard harness).</summary>
        private const ulong NoPotion = ulong.MaxValue;

        /// <summary>Cast at an explicit target (used by the keyboard test harness).</summary>
        public void RequestCast(PotionType type, ulong targetId)
        {
            RequestCastRpc(type, NetworkManager.LocalClientId, targetId, NoPotion);
        }

        /// <summary>Cast targeting the next player automatically (used by the keyboard test harness).</summary>
        public void RequestCast(PotionType type)
        {
            RequestCastRpc(type, NetworkManager.LocalClientId, ulong.MaxValue, NoPotion);
        }

        /// <summary>
        /// Submit a physical potion dropped in the play zone. The authority decides the
        /// outcome: a legal cast consumes the potion, an illegal one sends it home. The
        /// potion is never destroyed client-side on a guess — that would silently eat a
        /// player's card whenever they dropped one at the wrong moment.
        /// </summary>
        public void RequestCastFromPotion(NetworkedPotion potion)
        {
            if (potion == null)
            {
                return;
            }

            NetworkObject netObj = potion.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
            {
                return;
            }

            RequestCastRpc(potion.Type, NetworkManager.LocalClientId, ulong.MaxValue, netObj.NetworkObjectId);
        }

        // Authority-only: look up a submitted potion by its network id.
        private NetworkedPotion FindPotion(ulong potionId)
        {
            if (potionId == NoPotion || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }

            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(potionId, out NetworkObject obj) && obj != null
                ? obj.GetComponent<NetworkedPotion>()
                : null;
        }

        // The cast was legal: the potion is spent.
        private void ConsumePotion(ulong potionId)
        {
            NetworkedPotion potion = FindPotion(potionId);
            if (potion != null)
            {
                DespawnPotion(potion);
            }
        }

        // The cast was refused: give the potion back rather than destroying it.
        private void RejectPotion(ulong potionId)
        {
            NetworkedPotion potion = FindPotion(potionId);
            if (potion != null)
            {
                potion.ReturnHomeRpc();
            }
        }

        /// <summary>
        /// Testing helper: forces the local player to draw a Curse, reproducing the
        /// real threat (a Curse is only ever drawn, never cast from hand). The player
        /// must then play a Counterspell to survive.
        /// </summary>
        public void DebugDrawCurse()
        {
            DebugDrawCurseRpc(NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Authority)]
        private void DebugDrawCurseRpc(ulong playerId)
        {
            if (!HasAuthority || !gameActive.Value)
            {
                return;
            }
            if (CurrentTurnClientId != playerId)
            {
                return; // only on your own turn
            }
            if (cursedPlayers.Contains(playerId))
            {
                SetAnnouncement($"{PlayerLabel(playerId)} is already cursed — play a Counterspell!");
                return;
            }

            AddCurse(playerId);
            SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE! Play a Counterspell to survive.");
        }

        [Rpc(SendTo.Authority)]
        private void RequestCastRpc(PotionType type, ulong casterId, ulong targetId, ulong potionId)
        {
            if (!HasAuthority || !gameActive.Value || eliminated.Contains(casterId))
            {
                RejectPotion(potionId);
                return;
            }

            // 1) Interrupts resolve first, and may be played out of turn.
            if (pendingActive && (type == PotionType.Dispel || type == PotionType.Reflection))
            {
                ConsumePotion(potionId);
                HandleInterrupt(type, casterId);
                return;
            }

            // 2) Curse defence takes priority even over turn order.
            if (cursedPlayers.Contains(casterId))
            {
                if (type == PotionType.Counterspell)
                {
                    ConsumePotion(potionId);
                    RemoveCurse(casterId);
                    deck.Add(PotionType.Curse);
                    ShuffleDeck();
                    SetAnnouncement($"{PlayerLabel(casterId)} countered the curse! It is back in the cauldron.");
                    EndTurnAfterAction();
                }
                else
                {
                    ConsumePotion(potionId);
                    SetAnnouncement($"{PlayerLabel(casterId)} used the wrong potion against a curse — destroyed!");
                    EliminatePlayer(casterId);
                }
                return;
            }

            // 3) Everything else is a play-phase action on your own turn.
            if (CurrentTurnClientId != casterId)
            {
                LogCast($"REFUSED {type} from {PlayerLabel(casterId)}: out of turn.");
                SetAnnouncement($"{PlayerLabel(casterId)} cannot cast out of turn.");
                RejectPotion(potionId);
                return;
            }
            if (pendingActive)
            {
                LogCast($"REFUSED {type} from {PlayerLabel(casterId)}: a spell is still resolving.");
                RejectPotion(potionId); // wait for the spell already on the table to resolve
                return;
            }

            // The three answering potions only ever respond to something: a Dispel or Reflection
            // needs a spell on the table, a Counterspell needs a Curse. Both of those cases are
            // handled above, so reaching here means there is nothing to answer. Refuse instead of
            // consuming — CanLocalPlayerCastNow already tells the player these are not castable
            // right now, and spending the potion for no effect would quietly cost them a card
            // (and, for a Counterspell, the only thing standing between them and a Curse).
            if (type == PotionType.Dispel || type == PotionType.Reflection || type == PotionType.Counterspell)
            {
                LogCast($"REFUSED {type} from {PlayerLabel(casterId)}: nothing to answer.");
                SetAnnouncement($"{PlayerLabel(casterId)} has nothing to answer — the {type} is not spent.");
                RejectPotion(potionId);
                return;
            }

            LogCast($"ACCEPTED {type} from {PlayerLabel(casterId)} " +
                    $"(turnsRemaining={turnsRemaining.Value}, target={PlayerLabel(targetId)}).");
            ConsumePotion(potionId);
            BeginCast(type, casterId, targetId);
        }

        // Authority-side trace of the cast/turn machinery. The reported "cast worked but my turn
        // carried on" cannot be pinned down from the code alone, because Tribute, Warp and
        // Foresight are all SUPPOSED to leave the turn with the caster — only a log from a real
        // session can say which potion and which branch was involved.
        private void LogCast(string message)
        {
            if (!logCastDecisions)
            {
                return;
            }
            Debug.Log($"[SpellGame] {message}");
        }

        // Put a spell on the table and give everyone else a beat to answer it.
        private void BeginCast(PotionType type, ulong casterId, ulong targetId)
        {
            if (targetId == ulong.MaxValue)
            {
                targetId = NextActivePlayerId(casterId);
            }

            pendingCopyCasters.Clear();
            pending = new PendingSpell
            {
                Type = type,
                Caster = casterId,
                Target = targetId,
                Cancelled = false,
                Interrupts = 0
            };

            // No point stalling the game if nobody can actually answer.
            if (!AnyoneCanInterrupt(casterId))
            {
                LogCast($"{type}: nobody can answer, resolving immediately.");
                pendingActive = false;
                ApplyEffect(type, casterId, targetId);
                return;
            }

            LogCast($"{type}: interrupt window open for {interruptWindowSeconds}s — " +
                    "nothing happens until it closes.");
            pendingActive = true;
            interruptWindowOpen.Value = true;
            SetAnnouncement($"{PlayerLabel(casterId)} casts {type} on {PlayerLabel(targetId)} — Dispel or Reflect now!");
            RestartWindow();
        }

        private void RestartWindow()
        {
            if (windowRoutine != null)
            {
                StopCoroutine(windowRoutine);
            }
            windowRoutine = StartCoroutine(ResolveAfterWindow());
        }

        private IEnumerator ResolveAfterWindow()
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, interruptWindowSeconds));
            ResolvePending();
        }

        private void ResolvePending()
        {
            windowRoutine = null;
            pendingActive = false;
            interruptWindowOpen.Value = false;

            var copyCasters = new List<ulong>(pendingCopyCasters);
            pendingCopyCasters.Clear();

            if (pending.Cancelled)
            {
                // A dispelled spell simply fizzles; the caster's turn carries on. Its Reflection
                // copies go with it — the Dispel erases the card they were copying.
                LogCast($"{pending.Type} cancelled by Dispel; {copyCasters.Count} copy(ies) discarded.");
                SetAnnouncement($"{pending.Type} fizzles out. {PlayerLabel(CurrentTurnClientId)} is still up.");
                return;
            }

            // Hex is settled in one move rather than several: each copy makes the single attack two
            // turns heavier, because two Hexes cannot each hand the turn to a different player.
            if (pending.Type == PotionType.Hex)
            {
                ApplyEffect(pending.Type, pending.Caster, pending.Target, copyCasters.Count);
                return;
            }

            ApplyEffect(pending.Type, pending.Caster, pending.Target);

            for (int i = 0; i < copyCasters.Count; i++)
            {
                ulong copyCaster = copyCasters[i];
                if (eliminated.Contains(copyCaster))
                {
                    continue;
                }

                // A copied Phase would end a turn the reflector does not hold, so it does nothing.
                if (pending.Type == PotionType.Phase)
                {
                    LogCast($"Copied Phase from {PlayerLabel(copyCaster)} ignored — not their turn.");
                    continue;
                }

                LogCast($"COPY of {pending.Type} resolving for {PlayerLabel(copyCaster)}.");
                ApplyEffect(pending.Type, copyCaster, NextActivePlayerId(copyCaster));
            }
        }

        // Dispel cancels the spell on the table (and can cancel a Dispel, flipping it back on).
        // Reflection copies it: the same spell resolves a second time with the reflector as its
        // caster. It is recorded rather than applied here so a later Dispel still wipes everything.
        private void HandleInterrupt(PotionType type, ulong interrupterId)
        {
            pending.Interrupts++;

            if (type == PotionType.Dispel)
            {
                pending.Cancelled = !pending.Cancelled;
                LogCast($"DISPEL by {PlayerLabel(interrupterId)} — cancelled is now {pending.Cancelled}.");
                SetAnnouncement(pending.Cancelled
                    ? $"{PlayerLabel(interrupterId)} DISPELS {pending.Type}!"
                    : $"{PlayerLabel(interrupterId)} dispels the dispel — {pending.Type} is back on!");
            }
            else
            {
                pendingCopyCasters.Add(interrupterId);
                LogCast($"REFLECT by {PlayerLabel(interrupterId)} — {pending.Type} will resolve " +
                        $"{pendingCopyCasters.Count + 1} time(s).");
                SetAnnouncement($"{PlayerLabel(interrupterId)} REFLECTS {pending.Type} — it strikes again!");
            }

            // Reopen the window so the answer can itself be answered.
            if (pending.Interrupts < maxInterruptChain && AnyoneCanInterrupt(interrupterId))
            {
                RestartWindow();
            }
            else
            {
                if (windowRoutine != null)
                {
                    StopCoroutine(windowRoutine);
                }
                ResolvePending();
            }
        }

        private bool AnyoneCanInterrupt(ulong exceptPlayerId)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong id = turnOrder[i];
                if (id == exceptPlayerId || eliminated.Contains(id))
                {
                    continue;
                }
                if (PlayerHolds(id, PotionType.Dispel) || PlayerHolds(id, PotionType.Reflection))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyEffect(PotionType type, ulong casterId, ulong targetId, int reflectionCopies = 0)
        {
            bool endsTurn = type == PotionType.Hex || type == PotionType.Phase;
            LogCast($"RESOLVE {type} by {PlayerLabel(casterId)} on {PlayerLabel(targetId)} — " +
                    (endsTurn
                        ? "this ends the caster's turn."
                        : "the caster KEEPS the turn and must still draw to end it."));

            switch (type)
            {
                case PotionType.Hex:
                {
                    // Attack: forfeit your turns and pile them onto the target. The target takes
                    // the attack you were under plus two, so an unhexed caster passes on 2 and the
                    // ladder runs 2 -> 4 -> 6 however late in your owed turns you play it.
                    int pass = hexAttackSize.Value + 2 + (reflectionCopies * 2);
                    turnsRemaining.Value = 0;
                    SetAnnouncement($"{PlayerLabel(casterId)} hexes {PlayerLabel(targetId)} — {pass} turns in a row!");
                    MoveToPlayer(targetId, pass);
                    return;
                }

                case PotionType.Phase:
                    SetAnnouncement($"{PlayerLabel(casterId)} phased out — turn ended without drawing.");
                    EndTurnAfterAction();
                    return;

                case PotionType.Warp:
                    ShuffleDeck();
                    SetAnnouncement($"{PlayerLabel(casterId)} warped the cauldron — the brew is reshuffled.");
                    return; // still their turn

                case PotionType.Foresight:
                    RevealTopCards(casterId, 3);
                    return; // still their turn

                case PotionType.Tribute:
                    ResolveTribute(casterId, targetId);
                    return; // still their turn

                case PotionType.Counterspell:
                    SetAnnouncement($"{PlayerLabel(casterId)} played a Counterspell with nothing to counter.");
                    return;

                case PotionType.Dispel:
                case PotionType.Reflection:
                    SetAnnouncement($"{PlayerLabel(casterId)} played {type} with nothing to answer.");
                    return;

                case PotionType.Curse:
                    AddCurse(casterId);
                    SetAnnouncement($"{PlayerLabel(casterId)} unleashed a Curse — counter it!");
                    return;
            }
        }

        // Foresight: only the caster may see the top of the deck.
        private void RevealTopCards(ulong playerId, int count)
        {
            int n = Mathf.Min(count, deck.Count);
            var types = new int[n];
            for (int i = 0; i < n; i++)
            {
                types[i] = (int)deck[i];
            }

            SetAnnouncement($"{PlayerLabel(playerId)} peers into the cauldron...");
            ForesightRpc(types, RpcTarget.Single(playerId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ForesightRpc(int[] types, RpcParams rpcParams)
        {
            var revealed = new PotionType[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                revealed[i] = (PotionType)types[i];
            }
            ForesightRevealed?.Invoke(revealed);
        }

        // Tribute: the target hands over one of their potions (picked at random, since
        // there is no selection UI yet) and it reappears in the caster's rack.
        private void ResolveTribute(ulong casterId, ulong targetId)
        {
            int targetSeat = GetSeatIndex(targetId);
            if (targetSeat < 0)
            {
                SetAnnouncement($"{PlayerLabel(casterId)} demands tribute, but there is nobody to pay it.");
                return;
            }

            NetworkedPotion[] slots = SlotsForSeat(targetSeat);
            var filled = new List<int>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    filled.Add(i);
                }
            }

            if (filled.Count == 0)
            {
                SetAnnouncement($"{PlayerLabel(targetId)} has no potions to give.");
                return;
            }

            int pick = filled[UnityEngine.Random.Range(0, filled.Count)];
            NetworkedPotion given = slots[pick];
            PotionType givenType = given.Type;

            slots[pick] = null;
            given.RackSeat = -1;
            given.RackSlot = -1;
            DespawnPotion(given);

            SpawnPotionForPlayer(givenType, casterId, false, null);
            SetAnnouncement($"{PlayerLabel(targetId)} pays tribute to {PlayerLabel(casterId)}.");
        }

        // ===================== Turn + elimination =====================

        /// <summary>
        /// End the current turn. A player under a Hex owes several turns in a row, so this
        /// only passes play on once the last of them is used up.
        /// </summary>
        private void EndTurnAfterAction()
        {
            if (turnOrder.Count == 0)
            {
                return;
            }

            turnsRemaining.Value = Mathf.Max(0, turnsRemaining.Value - 1);
            if (turnsRemaining.Value > 0)
            {
                stirring.Value = false;
                brewing = false;
                drawInProgress = false;
                LogCast($"TURN HELD: {PlayerLabel(CurrentTurnClientId)} still owes {turnsRemaining.Value}.");
                SetAnnouncement($"{PlayerLabel(CurrentTurnClientId)} still owes {turnsRemaining.Value} more turn(s)!");
                return;
            }

            MoveToNextPlayer(1);
        }

        private void MoveToNextPlayer(int turns)
        {
            if (turnOrder.Count == 0)
            {
                return;
            }

            stirring.Value = false;
            brewing = false;
            drawInProgress = false;

            int index = currentTurnIndex.Value;
            for (int step = 0; step < turnOrder.Count; step++)
            {
                index = (index + 1) % turnOrder.Count;
                if (!eliminated.Contains(turnOrder[index]))
                {
                    break;
                }
            }

            currentTurnIndex.Value = index;
            turnsRemaining.Value = Mathf.Max(1, turns);
            hexAttackSize.Value = 0; // play reached them normally, so they are not under a Hex
            LogCast($"TURN PASSED to {PlayerLabel(CurrentTurnClientId)} for {turnsRemaining.Value} turn(s).");
            AnnounceCurrentTurn();
        }

        private void MoveToPlayer(ulong playerId, int turns)
        {
            int index = IndexOfPlayer(playerId);
            if (index < 0 || eliminated.Contains(playerId))
            {
                MoveToNextPlayer(1);
                return;
            }

            stirring.Value = false;
            brewing = false;
            drawInProgress = false;
            currentTurnIndex.Value = index;
            turnsRemaining.Value = Mathf.Max(1, turns);
            hexAttackSize.Value = turnsRemaining.Value; // they are now under a Hex of this size
            LogCast($"TURN FORCED to {PlayerLabel(playerId)} for {turnsRemaining.Value} turn(s) " +
                    $"(hex attack size {hexAttackSize.Value}).");
            AnnounceCurrentTurn();
        }

        private void EliminatePlayer(ulong playerId)
        {
            RemoveCurse(playerId);
            eliminated.Add(playerId);

            // Their potions leave the table with them.
            ClearSeatPotions(GetSeatIndex(playerId));

            if (CheckForWinner())
            {
                return;
            }

            // If the eliminated player was the one holding the turn, play moves on.
            if (CurrentTurnClientId == playerId)
            {
                MoveToNextPlayer(1);
            }
            else
            {
                AnnounceCurrentTurn();
            }
        }

        private bool CheckForWinner()
        {
            if (ActivePlayerCount() > 1)
            {
                return false;
            }

            gameActive.Value = false;
            interruptWindowOpen.Value = false;
            pendingActive = false;
            drawInProgress = false;

            string winner = "Nobody";
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (!eliminated.Contains(turnOrder[i]))
                {
                    winner = PlayerLabel(turnOrder[i]);
                    break;
                }
            }

            SetAnnouncement($"{winner} wins! Last mage standing.");
            return true;
        }

        private ulong NextActivePlayerId(ulong fromId)
        {
            if (turnOrder.Count == 0)
            {
                return ulong.MaxValue;
            }

            int index = Mathf.Max(0, IndexOfPlayer(fromId));
            for (int step = 0; step < turnOrder.Count; step++)
            {
                index = (index + 1) % turnOrder.Count;
                if (!eliminated.Contains(turnOrder[index]))
                {
                    return turnOrder[index];
                }
            }
            return fromId;
        }

        private void AnnounceCurrentTurn()
        {
            if (!gameActive.Value)
            {
                return;
            }

            string who = PlayerLabel(CurrentTurnClientId);
            if (turnsRemaining.Value > 1)
            {
                SetAnnouncement($"{who}'s turn — {turnsRemaining.Value} turns to survive. Cast, or dip to draw.");
            }
            else
            {
                SetAnnouncement($"{who}'s turn — cast from your rack, then dip your hand to draw.");
            }
        }

        private void SetAnnouncement(string text)
        {
            announcement.Value = new FixedString512Bytes(text);
        }

        // Resolve a player's display name from their networked profile, falling back
        // to a generic label before the name has replicated. Public so client-side UI can
        // name the player it is waiting on without duplicating the lookup.
        public string PlayerLabel(ulong id)
        {
            if (XRMultiplayer.XRINetworkGameManager.Instance != null &&
                XRMultiplayer.XRINetworkGameManager.Instance.TryGetPlayerByID(id, out XRMultiplayer.XRINetworkPlayer player) &&
                player != null && !string.IsNullOrEmpty(player.playerName))
            {
                return player.playerName;
            }
            return $"Player {id}";
        }
    }
}
