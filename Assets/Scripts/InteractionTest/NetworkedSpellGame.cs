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
    ///  - An authority RPC NEVER takes the acting player's id as an argument. It reads
    ///    <c>RpcParams.Receive.SenderClientId</c>, which the transport fills in and a client
    ///    cannot forge. Passing the id let any client act as any other player.
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
        [Tooltip("Seconds other players get to answer a cast spell with a Dispel — the only card playable out of turn. Skipped automatically if nobody holds one.")]
        [SerializeField] private float interruptWindowSeconds = 3f;

        [Tooltip("Safety cap on how long a Dispel-answering-a-Dispel chain can get.")]
        [SerializeField] private int maxInterruptChain = 6;

        [Tooltip("Seconds the caster of a Tribute gets to pick their victim before one is chosen " +
                 "at random.")]
        [SerializeField] private float tributeTargetSeconds = 20f;

        [Tooltip("Seconds the target of a Tribute gets to choose which potion to hand over " +
                 "before one is taken at random.")]
        [SerializeField] private float tributeChoiceSeconds = 20f;

        [Tooltip("Seconds the Foresight potions hang above the cauldron before sinking back.")]
        [SerializeField] private float foresightRevealSeconds = 4f;

        [Tooltip("Seconds a player gets to choose where a countered Curse goes back into the deck " +
                 "before it is placed at random for them. Stops a match stalling on an idle player.")]
        [SerializeField] private float cursePlacementSeconds = 15f;

        [Header("Diagnostics")]
        [Tooltip("Authority-side log of every cast decision and turn change. Turn this on to " +
                 "find out which branch ran when a turn behaves unexpectedly in a playtest; " +
                 "the log only appears on the authority's console.")]
        [SerializeField] private bool logCastDecisions = true;

        [Tooltip("Show the Tribute victim picker even when there is only one candidate. The " +
                 "picker normally needs 3+ players, so on a machine that cannot run three " +
                 "clones smoothly it never appears and cannot be tested. Leave OFF for real " +
                 "play: it makes every 2-player Tribute a pointless confirmation step.")]
        [SerializeField] private bool alwaysShowTributePicker = false;

        [Tooltip("Seconds a dropped player's seat is held open before they are eliminated for " +
                 "good. Until it expires their turn is skipped but no winner is declared, so a " +
                 "brief network blip no longer ends a 2-player match outright.")]
        [SerializeField] private float disconnectGraceSeconds = 45f;

        [Tooltip("Seconds the authority waits for a joining client to report who it is before " +
                 "seating it as a brand-new player. Only this long, so a client that never " +
                 "reports still gets a seat rather than being locked out of the match.")]
        [SerializeField] private float identifyGraceSeconds = 8f;

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

        private const ulong NoPlayer = ulong.MaxValue;
        private const int NoSpell = -1;

        // The last spell that actually resolved, as (int)PotionType — what a Reflection copies.
        // Replicated because each client decides for itself whether its Reflection is castable
        // right now, and that answer depends on there being something to copy.
        private readonly NetworkVariable<int> lastResolvedSpell = new(
            NoSpell, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Whose Counterspell is waiting on a "where does the Curse go?" choice, or NoPlayer.
        // Replicated so the chooser's own client can offer the choice and everyone else can be
        // told to wait, and so the turn cannot pass until the Curse has actually been placed.
        private readonly NetworkVariable<ulong> cursePlacementPlayer = new(
            NoPlayer, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Who owes a potion to a Tribute, and who is collecting it. Replicated so the payer's
        // client can offer the choice and everyone else can be told to wait.
        private readonly NetworkVariable<ulong> tributePayer = new(
            NoPlayer, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<ulong> tributeReceiver = new(
            NoPlayer, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // The Tribute caster while they are still picking a victim.
        private readonly NetworkVariable<ulong> tributeChooser = new(
            NoPlayer, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> gameActive = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Replicated so the answer is the SAME everywhere and can be shown on screen. The
        // serialized field is only the starting value, and it is read by whichever process holds
        // authority — which alternates between the main editor and the MPPM clone, and the clone
        // loads the scene from disk. That made an Inspector tick appear to work or not at random.
        private readonly NetworkVariable<bool> forceTributePicker = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Whether the Tribute picker is currently forced on. Shown by the test HUD.</summary>
        public bool TributePickerForced => forceTributePicker.Value;

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

        // The interrupt window's deadline on the shared network clock. Replicated rather than
        // broadcast per frame: every client runs its own countdown off this one number, which is
        // the deterministic-local-animation-from-replicated-state rule.
        private readonly NetworkVariable<double> interruptWindowEnd = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // How long the window was opened for. This is NOT read from interruptWindowSeconds on the
        // client: that is a serialized field, and a serialized field is a different value in every
        // process (authority alternates between the main editor and the MPPM clone, which loads
        // the scene from disk). Reading it locally would draw a bar that empties at the wrong rate
        // on some machines and not others.
        private readonly NetworkVariable<float> interruptWindowLength = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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

        /// <summary>True while a cast spell can still be answered. Dispel is the only answer.</summary>
        public bool InterruptWindowOpen => interruptWindowOpen.Value;

        /// <summary>
        /// Seconds left to answer the spell on the table; 0 when no window is open. Computed from
        /// the replicated deadline against the network clock, so it reads the same on every client.
        /// </summary>
        public float InterruptSecondsRemaining
        {
            get
            {
                if (!interruptWindowOpen.Value || NetworkManager == null || !NetworkManager.IsListening)
                {
                    return 0f;
                }

                double remaining = interruptWindowEnd.Value - NetworkManager.ServerTime.Time;
                return remaining <= 0d ? 0f : (float)remaining;
            }
        }

        /// <summary>Interrupt window remaining as 1 → 0, for drawing a bar. 0 when closed.</summary>
        public float InterruptWindowFraction
        {
            get
            {
                float length = interruptWindowLength.Value;
                return length <= 0f ? 0f : Mathf.Clamp01(InterruptSecondsRemaining / length);
            }
        }

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

            // Owing a Tribute: any potion in your rack is a legal thing to hand over, so they all
            // glow. This is the one time potions are playable on someone else's turn other than
            // a Dispel, and the choice of which to give is the whole point of the card.
            if (IsLocalPlayerPayingTribute)
            {
                return true;
            }

            // Cursed: nothing but a Counterspell will save you, so nothing else is playable.
            if (IsLocalPlayerCursed)
            {
                return type == PotionType.Counterspell;
            }

            // A spell is waiting on the table. Dispel is the ONLY card that plays out of turn —
            // this is what a player wants to spot during someone else's turn.
            //
            // "Out of turn" excludes the caster, who is by definition the player whose turn it
            // is. Without that clause a caster's own Dispel lit up while their spell was on the
            // table, inviting them to cancel it — which the authority then allowed.
            if (InterruptWindowOpen)
            {
                return type == PotionType.Dispel && !IsLocalPlayersTurn;
            }

            if (!IsLocalPlayersTurn)
            {
                return false;
            }

            // Reflection copies the last spell that resolved, so it is dead until something has.
            if (type == PotionType.Reflection)
            {
                return lastResolvedSpell.Value != NoSpell;
            }

            // Your turn, nothing pending: everything except Dispel, which answers a spell, and
            // Counterspell, which answers a Curse.
            return type != PotionType.Dispel && type != PotionType.Counterspell;
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

        // Seated players whose connection has dropped but whose seat is being held open. They are
        // deliberately NOT in `eliminated`, so ActivePlayerCount still counts them and no winner is
        // declared while somebody is merely reconnecting.
        private readonly HashSet<ulong> dormant = new();
        private readonly Dictionary<ulong, Coroutine> dormantTimers = new();

        // Persistent identity per connected client. A reconnecting player arrives with a BRAND NEW
        // clientId, so clientId alone can never recognise them; the authentication id survives the
        // round trip and is what lets a returning player be handed back their own seat and rack.
        // Entries outlive the disconnect on purpose — that is the whole point — and are dropped
        // only when the seat is genuinely given away.
        private readonly Dictionary<ulong, string> clientAuthIds = new();
        private readonly Dictionary<ulong, Coroutine> identifyTimers = new();
        private readonly HashSet<ulong> identifyExpired = new();

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

        private PendingSpell pending;
        private bool pendingActive;
        private Coroutine windowRoutine;

        /// <summary>Pass to <see cref="ChooseCursePlacement"/> to bury the Curse at random.</summary>
        public const int RandomPlacement = 3;

        private Coroutine cursePlacementRoutine;
        private Coroutine tributeRoutine;
        private Coroutine tributeTargetRoutine;
        private ForesightDisplay foresightDisplay;
        // One picker instance serves both Tribute and Curse placement — they can never be on
        // screen at the same time, and sharing it means a stale one cannot be left behind.
        private TributeTargetPicker choicePicker;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            announcement.OnValueChanged += OnAnnouncementValueChanged;
            currentTurnIndex.OnValueChanged += OnTurnIndexValueChanged;

            // Curse placement shows its picker straight off the replicated state rather than a
            // dedicated RPC: the authority already has to publish who is choosing so everyone
            // else can be told to wait, so the chooser's own client can act on the same value.
            cursePlacementPlayer.OnValueChanged += OnCursePlacementPlayerChanged;

            if (HasAuthority)
            {
                // Seed the replicated flag from the Inspector value on whichever process actually
                // holds authority. Set once here, not in StartGame, so a runtime toggle survives
                // a match restart.
                forceTributePicker.Value = alwaysShowTributePicker;
                NetworkManager.OnConnectionEvent += OnConnectionEvent;
                StartGame();
            }

            // Tell the authority who we are. Sent by EVERY client, the authority included, so a
            // seat can be matched back to a person rather than to a client id that changes on
            // every reconnect. Seating during a match waits on this.
            ReportIdentity();

            // Fire initial state for late subscribers / joiners.
            AnnouncementChanged?.Invoke(CurrentAnnouncement);
            TurnChanged?.Invoke(CurrentTurnClientId);
        }

        private void ReportIdentity()
        {
            string authId = XRMultiplayer.XRINetworkGameManager.AuthenicationId;
            if (string.IsNullOrEmpty(authId))
            {
                // Not signed in (an offline test run). Nothing to key a seat on, so let the
                // authority's identify grace expire and seat us as a newcomer.
                return;
            }
            IdentifyRpc(new FixedString64Bytes(authId));
        }

        /// <summary>
        /// A client announcing its persistent identity. If it matches a seat being held open for
        /// a dropped player, that seat — and the rack still standing at it — is handed straight
        /// back, which is what makes a reconnect keep its hand instead of being dealt a fresh one.
        /// </summary>
        [Rpc(SendTo.Authority)]
        private void IdentifyRpc(FixedString64Bytes authId, RpcParams rpcParams = default)
        {
            if (!HasAuthority)
            {
                return;
            }

            ulong sender = rpcParams.Receive.SenderClientId;
            string id = authId.ToString();
            clientAuthIds[sender] = id;
            StopIdentifyTimer(sender);

            if (gameActive.Value && IndexOfPlayer(sender) < 0)
            {
                int seat = SeatHeldFor(id);
                if (seat >= 0)
                {
                    ulong previous = turnOrder[seat];
                    ClearDormant(previous);
                    clientAuthIds.Remove(previous);

                    // Overwrite in place. Seat index == turn-order index == rack index, so
                    // replacing the entry keeps the rack they left behind attached to them.
                    turnOrder[seat] = sender;
                    clientAuthIds[sender] = id;

                    LogCast($"RECONNECT: {PlayerLabel(sender)} reclaimed seat {seat} " +
                            $"(was client {previous}) with their rack intact.");
                    SetAnnouncement($"{PlayerLabel(sender)} is back at their seat.");
                    AnnounceCurrentTurn();
                    return;
                }
            }

            // Not a returning player: fall through to ordinary seating.
            SyncSeatingWithConnectedClients();
            if (!gameActive.Value && turnOrder.Count >= minPlayersToStart)
            {
                StartGame();
            }
        }

        // A seat being held for a dropped player with this persistent identity, or -1.
        private int SeatHeldFor(string authId)
        {
            if (string.IsNullOrEmpty(authId))
            {
                return -1;
            }
            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong occupant = turnOrder[i];
                if (dormant.Contains(occupant) &&
                    clientAuthIds.TryGetValue(occupant, out string held) && held == authId)
                {
                    return i;
                }
            }
            return -1;
        }

        private void StopIdentifyTimer(ulong clientId)
        {
            if (identifyTimers.TryGetValue(clientId, out Coroutine routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
                identifyTimers.Remove(clientId);
            }
        }

        // A client that never reports an identity must still get a seat eventually, or a bad
        // build or a signed-out player would sit outside the match forever.
        private IEnumerator IdentifyTimeout(ulong clientId)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, identifyGraceSeconds));

            identifyTimers.Remove(clientId);
            if (clientAuthIds.ContainsKey(clientId))
            {
                yield break;
            }

            identifyExpired.Add(clientId);
            LogCast($"IDENTIFY: client {clientId} never reported an identity — seating as new.");
            SyncSeatingWithConnectedClients();
        }

        public override void OnNetworkDespawn()
        {
            announcement.OnValueChanged -= OnAnnouncementValueChanged;
            currentTurnIndex.OnValueChanged -= OnTurnIndexValueChanged;
            cursePlacementPlayer.OnValueChanged -= OnCursePlacementPlayerChanged;

            // A picker left on screen would outlive the match it belongs to.
            if (choicePicker != null)
            {
                choicePicker.Hide();
            }

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

            // "Randomly decide the starting player" — this used to always be seat 0, which handed
            // the same player the first move (and the first Curse risk) every single match.
            currentTurnIndex.Value = turnOrder.Count > 0 ? UnityEngine.Random.Range(0, turnOrder.Count) : 0;
            turnsRemaining.Value = 1;
            cursePlacementPlayer.Value = NoPlayer;
            tributePayer.Value = NoPlayer;
            tributeReceiver.Value = NoPlayer;
            tributeChooser.Value = NoPlayer;
            stirring.Value = false;
            interruptWindowOpen.Value = false;
            brewing = false;
            drawInProgress = false;
            pendingActive = false;
            lastResolvedSpell.Value = NoSpell;
            ClearCurses();
            eliminated.Clear();
            ClearAllDormant();

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

            // A player who drops mid-match is held dormant, not eliminated. Eliminating them
            // immediately meant ANY disconnect in a 2-player match instantly declared the other
            // player the winner — the match was over before anyone noticed the drop.
            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong id = turnOrder[i];
                if (!connected.Contains(id) && !eliminated.Contains(id))
                {
                    MarkDormant(id);
                }
            }

            // Anyone dormant who is connected again is back in play.
            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong id = turnOrder[i];
                if (connected.Contains(id) && dormant.Contains(id))
                {
                    ClearDormant(id);
                    SetAnnouncement($"{PlayerLabel(id)} is back.");
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

                // Do not seat anyone until we know who they are. A returning player would
                // otherwise be treated as a newcomer and handed the FIRST vacant seat, which is
                // usually their own dormant one — clearing the very rack we are holding for them.
                if (!clientAuthIds.ContainsKey(id) && !identifyExpired.Contains(id))
                {
                    if (!identifyTimers.ContainsKey(id))
                    {
                        identifyTimers[id] = StartCoroutine(IdentifyTimeout(id));
                    }
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
                // Their grace timer must die with the seat, or it would later "eliminate" an id
                // that now belongs to whoever took the seat over. The identity goes too: the seat
                // is being given away, so it must stop being reclaimable.
                ClearDormant(departed);
                clientAuthIds.Remove(departed);
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
            RequestDrawRpc();
        }

        [Rpc(SendTo.Authority)]
        private void RequestDrawRpc(RpcParams rpcParams = default)
        {
            ulong playerId = rpcParams.Receive.SenderClientId;
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
            RequestBrewRpc();
        }

        [Rpc(SendTo.Authority)]
        private void RequestBrewRpc(RpcParams rpcParams = default)
        {
            ulong playerId = rpcParams.Receive.SenderClientId;
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
            if (CursePlacementPending)
            {
                return false; // a countered Curse has not been put back yet
            }
            if (TributePending || TributeTargetPending)
            {
                return false; // a Tribute is still being aimed or paid
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
                StartCoroutine(FloatPotionToRack(potion, playerId, spawnPos, restPos, onSeated));
            }
            else
            {
                potion?.SetRacked(true);
                onSeated?.Invoke();
            }
        }

        private IEnumerator FloatPotionToRack(NetworkedPotion potion, ulong drawerId, Vector3 from,
                                              Vector3 to, Action onArrived)
        {
            Transform tf = potion != null ? potion.transform : null;
            Rigidbody body = potion != null ? potion.GetComponent<Rigidbody>() : null;

            // Kinematic for the trip so physics doesn't fight our per-frame position writes.
            if (body != null)
            {
                body.isKinematic = true;
            }

            // Hold it over the cauldron so the drawer can read what they brewed. The pause is
            // public — everyone watches the tube rise and hang — but the caption naming the
            // potion goes to the drawer alone. Broadcasting it told the whole table what card
            // just entered an opponent's hand.
            float reveal = Mathf.Max(0f, drawRevealSeconds);
            if (reveal > 0f && potion != null)
            {
                potion.RevealToDrawerRpc((int)potion.Type, reveal,
                    RpcTarget.Single(drawerId, RpcTargetUse.Temp));

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
            RequestCastRpc(type, targetId, NoPotion);
        }

        /// <summary>Cast targeting the next player automatically (used by the keyboard test harness).</summary>
        public void RequestCast(PotionType type)
        {
            RequestCastRpc(type, ulong.MaxValue, NoPotion);
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

            // Tribute's victim is chosen from a picker after the cast resolves, not at release —
            // see TributeTargetPicker for why aiming cannot work here.
            RequestCastRpc(potion.Type, ulong.MaxValue, netObj.NetworkObjectId);
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
            DebugDrawCurseRpc();
        }

        [Rpc(SendTo.Authority)]
        private void DebugDrawCurseRpc(RpcParams rpcParams = default)
        {
            ulong playerId = rpcParams.Receive.SenderClientId;
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
        private void RequestCastRpc(PotionType type, ulong targetId, ulong potionId,
                                    RpcParams rpcParams = default)
        {
            // The caster is whoever actually sent this, per the transport — never a value the
            // sender supplied. Taking it from an argument let any client cast as any player.
            ulong casterId = rpcParams.Receive.SenderClientId;
            if (!HasAuthority || !gameActive.Value || eliminated.Contains(casterId))
            {
                RejectPotion(potionId);
                return;
            }

            // 0) Paying a Tribute comes before everything, and happens on someone else's turn:
            // the potion you dropped is the one you are handing over, whatever it is.
            if (potionId != NoPotion && casterId == tributePayer.Value)
            {
                NetworkedPotion offered = FindPotion(potionId);
                if (offered != null && offered.RackSeat == GetSeatIndex(casterId))
                {
                    LogCast($"TRIBUTE paid: {PlayerLabel(casterId)} hands over {offered.Type}.");
                    HandOverTribute(offered);
                }
                else
                {
                    RejectPotion(potionId); // not one of theirs to give
                }
                return;
            }

            // 1) Interrupts resolve first. Dispel is the only card playable out of turn.
            if (pendingActive && type == PotionType.Dispel)
            {
                // ...and OUT OF TURN is the whole of it: the caster may not answer their own
                // spell. Nothing stopped them before, so casting Foresight and then dispelling
                // it yourself was legal — you kept the peek at the deck, cancelled your own
                // spell, and spent a Dispel doing it, because the potion was consumed above
                // this check. Refuse without consuming, the same as any other card played with
                // nothing to answer.
                if (casterId == pending.Caster)
                {
                    LogCast($"REFUSED Dispel from {PlayerLabel(casterId)}: cannot dispel your own spell.");
                    SetAnnouncement($"{PlayerLabel(casterId)} cannot dispel their own spell.");
                    RejectPotion(potionId);
                    return;
                }

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
                    LogCast($"COUNTERSPELL by {PlayerLabel(casterId)} — awaiting Curse placement.");
                    BeginCursePlacement(casterId);
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

            // The two answering potions only ever respond to something: a Dispel needs a spell on
            // the table, a Counterspell needs a Curse. Both cases are handled above, so reaching
            // here means there is nothing to answer. Refuse instead of consuming — spending the
            // potion for no effect would quietly cost the player a card (and for a Counterspell,
            // the only thing standing between them and a Curse).
            if (type == PotionType.Dispel || type == PotionType.Counterspell)
            {
                LogCast($"REFUSED {type} from {PlayerLabel(casterId)}: nothing to answer.");
                SetAnnouncement($"{PlayerLabel(casterId)} has nothing to answer — the {type} is not spent.");
                RejectPotion(potionId);
                return;
            }

            // Reflection needs something to copy.
            if (type == PotionType.Reflection && lastResolvedSpell.Value == NoSpell)
            {
                LogCast($"REFUSED Reflection from {PlayerLabel(casterId)}: no spell has resolved yet.");
                SetAnnouncement($"{PlayerLabel(casterId)} has no spell to reflect yet.");
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
            SetAnnouncement($"{PlayerLabel(casterId)} casts {type} on {PlayerLabel(targetId)} — Dispel it now!");
            RestartWindow();
        }

        // Every path that opens or reopens the window comes through here, so this is the one place
        // the countdown's deadline has to be published. A Dispel restarts the window, and the bar
        // on every client refills because this number moved.
        private void RestartWindow()
        {
            if (windowRoutine != null)
            {
                StopCoroutine(windowRoutine);
            }

            float length = Mathf.Max(0.1f, interruptWindowSeconds);
            interruptWindowLength.Value = length;
            interruptWindowEnd.Value = NetworkManager.ServerTime.Time + length;

            windowRoutine = StartCoroutine(ResolveAfterWindow(length));
        }

        // Takes the length rather than re-reading the field, so the coroutine that actually
        // resolves the spell and the bar the players are watching can never disagree.
        private IEnumerator ResolveAfterWindow(float length)
        {
            yield return new WaitForSeconds(length);
            ResolvePending();
        }

        private void ResolvePending()
        {
            windowRoutine = null;
            pendingActive = false;
            interruptWindowOpen.Value = false;

            if (pending.Cancelled)
            {
                // A dispelled spell simply fizzles; the caster's turn carries on.
                LogCast($"{pending.Type} cancelled by Dispel.");
                SetAnnouncement($"{pending.Type} fizzles out. {PlayerLabel(CurrentTurnClientId)} is still up.");
                return;
            }

            ApplyEffect(pending.Type, pending.Caster, pending.Target);
        }

        // Dispel cancels the spell on the table, and can cancel a Dispel, flipping it back on.
        // It is the only card that plays out of turn, so this is the whole interrupt system.
        private void HandleInterrupt(PotionType type, ulong interrupterId)
        {
            pending.Interrupts++;
            pending.Cancelled = !pending.Cancelled;
            LogCast($"DISPEL by {PlayerLabel(interrupterId)} — cancelled is now {pending.Cancelled}.");
            SetAnnouncement(pending.Cancelled
                ? $"{PlayerLabel(interrupterId)} DISPELS {pending.Type}!"
                : $"{PlayerLabel(interrupterId)} dispels the dispel — {pending.Type} is back on!");

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
                if (PlayerHolds(id, PotionType.Dispel))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyEffect(PotionType type, ulong casterId, ulong targetId)
        {
            bool endsTurn = type == PotionType.Hex || type == PotionType.Phase;
            LogCast($"RESOLVE {type} by {PlayerLabel(casterId)} on {PlayerLabel(targetId)} — " +
                    (endsTurn
                        ? "this ends the caster's turn."
                        : "the caster KEEPS the turn and must still draw to end it."));

            // Remember what a Reflection would copy. Four types are deliberately never recorded,
            // so the reflectable set is exactly Hex, Phase, Warp, Foresight and Tribute:
            //   Reflection   resolves INTO another spell, and that spell records itself on the way
            //                through, so reflecting a reflected Hex still copies the Hex rather
            //                than chasing its own tail.
            //   Dispel       answers a spell rather than being one.
            //   Counterspell answers a Curse. It cannot currently reach this method at all (the
            //                curse-defence branch handles it and the "nothing to counter" case is
            //                refused before the potion is spent), so listing it changes nothing
            //                today — it is here so that making Counterspell resolve normally
            //                cannot silently turn it into a reflectable card.
            //   Curse        is only ever drawn, never cast, and reaching here would let a
            //                Reflection copy it — which resolves as AddCurse(caster) and would
            //                curse the reflector themselves.
            if (type != PotionType.Reflection && type != PotionType.Dispel &&
                type != PotionType.Counterspell && type != PotionType.Curse)
            {
                lastResolvedSpell.Value = (int)type;
            }

            switch (type)
            {
                case PotionType.Hex:
                {
                    // Attack: forfeit every turn you still owe and pile them onto the target,
                    // plus two. WHEN you play it matters — hexing on the first of four owed turns
                    // passes on 6, hexing on the last of them passes on 3. An ordinary unhexed
                    // turn owes 1, so the base case is 3.
                    //
                    // Read turnsRemaining BEFORE zeroing it below: it is the whole input to the
                    // sum, and MoveToPlayer overwrites it for the target a moment later.
                    int pass = turnsRemaining.Value + 2;
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
                    // targetId is ignored: the caster picks their victim from the picker rather
                    // than it being decided by turn order at cast time.
                    BeginTributeChoice(casterId);
                    return; // still their turn

                case PotionType.Counterspell:
                    SetAnnouncement($"{PlayerLabel(casterId)} played a Counterspell with nothing to counter.");
                    return;

                case PotionType.Reflection:
                {
                    // Copy the last spell that resolved, as though the reflector had cast it.
                    // Guarded on the way in, so there is always something to copy here.
                    var copied = (PotionType)lastResolvedSpell.Value;
                    SetAnnouncement($"{PlayerLabel(casterId)} reflects {copied}!");
                    ApplyEffect(copied, casterId, NextActivePlayerId(casterId));
                    return;
                }

                case PotionType.Dispel:
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

            // Two separate messages, deliberately. Everyone is told only HOW MANY potions rose out
            // of the pot; the types go to the caster alone. Sending the types to everyone and
            // hiding them client-side would put the secret on every machine in the session.
            // The two handlers are independent, so their arrival order does not matter (§ the rule
            // about never rendering a NetworkVariable an RPC just triggered).
            ForesightPreviewRpc(n, playerId);
            ForesightRpc(types, RpcTarget.Single(playerId, RpcTargetUse.Temp));
        }

        // Everyone except the caster: anonymous tubes, no types on the wire.
        [Rpc(SendTo.Everyone)]
        private void ForesightPreviewRpc(int count, ulong casterId)
        {
            if (NetworkManager.LocalClientId == casterId)
            {
                return; // the caster gets the revealed version instead
            }
            ShowForesight(null, count);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ForesightRpc(int[] types, RpcParams rpcParams)
        {
            var revealed = new PotionType[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                revealed[i] = (PotionType)types[i];
            }

            ShowForesight(revealed, revealed.Length);
            ForesightRevealed?.Invoke(revealed);
        }

        // Local presentation only. The display component is created on demand so the reveal needs
        // no scene wiring — drop the game manager in and it works.
        private void ShowForesight(PotionType[] types, int count)
        {
            if (foresightDisplay == null)
            {
                foresightDisplay = gameObject.AddComponent<ForesightDisplay>();
            }

            foresightDisplay.Reveal(
                types,
                count,
                networkedPotionPrefab,
                cauldronRig != null ? cauldronRig : transform,
                foresightRevealSeconds);
        }

        /// <summary>Is the local player the one who owes a potion to a Tribute right now?</summary>
        public bool IsLocalPlayerPayingTribute =>
            NetworkManager != null && tributePayer.Value == NetworkManager.LocalClientId;

        /// <summary>True while any player owes a Tribute (everyone else waits).</summary>
        public bool TributePending => tributePayer.Value != NoPlayer;

        /// <summary>Is the local player choosing who a Tribute lands on?</summary>
        public bool IsLocalPlayerChoosingTributeTarget =>
            NetworkManager != null && tributeChooser.Value == NetworkManager.LocalClientId;

        /// <summary>True while a Tribute caster is still picking their victim.</summary>
        public bool TributeTargetPending => tributeChooser.Value != NoPlayer;

        // Step 1 of a Tribute: WHO pays. Only players who actually hold something are offered —
        // robbing an empty rack does nothing and would just waste the card.
        private void BeginTributeChoice(ulong casterId)
        {
            var candidates = new List<ulong>();
            for (int i = 0; i < turnOrder.Count; i++)
            {
                ulong id = turnOrder[i];
                if (id == casterId || eliminated.Contains(id))
                {
                    continue;
                }
                if (CountPotions(GetSeatIndex(id)) > 0)
                {
                    candidates.Add(id);
                }
            }

            if (candidates.Count == 0)
            {
                SetAnnouncement($"{PlayerLabel(casterId)} demands tribute, but nobody has a potion to give.");
                return;
            }

            // Nothing to choose between with only one candidate — do not make a player confirm
            // the only option available, which is every 2-player match.
            // alwaysShowTributePicker overrides this: the picker needs 3+ players to appear at
            // all, and a machine that cannot run three clones without lagging can never see it.
            if (candidates.Count == 1 && !forceTributePicker.Value)
            {
                LogCast($"TRIBUTE: only one candidate, skipping the picker " +
                        $"(forceTributePicker is OFF — press Ctrl+P to force it on).");
                ResolveTribute(casterId, candidates[0]);
                return;
            }

            tributeChooser.Value = casterId;
            SetAnnouncement($"{PlayerLabel(casterId)} demands tribute — choosing a victim...");
            LogCast($"TRIBUTE: {PlayerLabel(casterId)} picking from {candidates.Count} candidates.");

            var ids = candidates.ToArray();
            TributeChoiceRpc(ids, RpcTarget.Single(casterId, RpcTargetUse.Temp));

            if (tributeTargetRoutine != null)
            {
                StopCoroutine(tributeTargetRoutine);
            }
            tributeTargetRoutine = StartCoroutine(TributeTargetTimeout(casterId, ids));
        }

        // Shown only to the caster: the row of boxes to look at and trigger. Only the ids go over
        // the wire — Netcode cannot serialize a string[], and the names are better resolved here
        // anyway, since PlayerLabel reads the session's own player list on every client.
        [Rpc(SendTo.SpecifiedInParams)]
        private void TributeChoiceRpc(ulong[] ids, RpcParams rpcParams)
        {
            if (choicePicker == null)
            {
                choicePicker = gameObject.AddComponent<TributeTargetPicker>();
            }

            var names = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                names[i] = PlayerLabel(ids[i]);
            }

            choicePicker.Show(ids, names, ChooseTributeTarget);
        }

        /// <summary>
        /// Flip <c>alwaysShowTributePicker</c> on the authority at runtime. Ticking the field in
        /// the Inspector is not enough on its own: MPPM clones are separate processes that load
        /// the scene FROM DISK, so an unsaved tick is invisible to them — and when the clone is
        /// the authority (which alternates run to run) the picker is still skipped. This reaches
        /// whichever process is actually deciding, with no scene edit to remember to undo.
        /// </summary>
        public void ToggleTributePicker()
        {
            ToggleTributePickerRpc();
        }

        [Rpc(SendTo.Authority)]
        private void ToggleTributePickerRpc()
        {
            if (!HasAuthority)
            {
                return;
            }
            forceTributePicker.Value = !forceTributePicker.Value;
            LogCast($"DEBUG: forceTributePicker = {forceTributePicker.Value}.");
            SetAnnouncement(forceTributePicker.Value
                ? "DEBUG: Tribute picker forced ON (shows even with one candidate)."
                : "DEBUG: Tribute picker back to normal (skipped with one candidate).");
        }

        /// <summary>Called by the picker once the caster has settled on a victim.</summary>
        public void ChooseTributeTarget(ulong targetId)
        {
            ChooseTributeTargetRpc(targetId);
        }

        [Rpc(SendTo.Authority)]
        private void ChooseTributeTargetRpc(ulong targetId, RpcParams rpcParams = default)
        {
            ulong casterId = rpcParams.Receive.SenderClientId;
            if (!HasAuthority || tributeChooser.Value != casterId)
            {
                return; // not this player's choice to make
            }
            if (eliminated.Contains(targetId) || targetId == casterId)
            {
                return;
            }

            ClearTributeChoice();
            ResolveTribute(casterId, targetId);
        }

        private IEnumerator TributeTargetTimeout(ulong casterId, ulong[] candidates)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, tributeTargetSeconds));

            if (tributeChooser.Value != casterId)
            {
                yield break;
            }

            ulong pick = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            LogCast($"TRIBUTE target choice timed out for {PlayerLabel(casterId)} — picking at random.");
            ClearTributeChoice();
            ResolveTribute(casterId, pick);
        }

        private void ClearTributeChoice()
        {
            if (tributeTargetRoutine != null)
            {
                StopCoroutine(tributeTargetRoutine);
                tributeTargetRoutine = null;
            }
            tributeChooser.Value = NoPlayer;
        }

        // Step 2 of a Tribute: the chosen player hands over one of their potions — THEY choose
        // which, by dropping it in the play zone exactly as they would cast it. Reusing the ring
        // means no new gesture to teach, and the payer keeps the real decision the card is about.
        private void ResolveTribute(ulong casterId, ulong targetId)
        {
            int targetSeat = GetSeatIndex(targetId);
            if (targetSeat < 0 || targetId == casterId)
            {
                SetAnnouncement($"{PlayerLabel(casterId)} demands tribute, but there is nobody to pay it.");
                return;
            }

            if (CountPotions(targetSeat) == 0)
            {
                SetAnnouncement($"{PlayerLabel(targetId)} has no potions to give.");
                return;
            }

            tributePayer.Value = targetId;
            tributeReceiver.Value = casterId;
            SetAnnouncement($"{PlayerLabel(casterId)} demands tribute from {PlayerLabel(targetId)} — " +
                            "drop a potion in the ring to hand it over.");
            LogCast($"TRIBUTE: {PlayerLabel(targetId)} owes {PlayerLabel(casterId)} a potion.");

            if (tributeRoutine != null)
            {
                StopCoroutine(tributeRoutine);
            }
            tributeRoutine = StartCoroutine(TributeTimeout(targetId));
        }

        // An idle or absent payer must not stall the match.
        private IEnumerator TributeTimeout(ulong payerId)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, tributeChoiceSeconds));

            if (tributePayer.Value != payerId)
            {
                yield break;
            }

            int seat = GetSeatIndex(payerId);
            NetworkedPotion[] slots = SlotsForSeat(seat);
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
                LogCast("TRIBUTE timed out with an empty rack — nothing to take.");
                ClearTribute();
                SetAnnouncement($"{PlayerLabel(payerId)} has nothing left to give.");
                yield break;
            }

            LogCast($"TRIBUTE timed out for {PlayerLabel(payerId)} — taking one at random.");
            HandOverTribute(slots[filled[UnityEngine.Random.Range(0, filled.Count)]]);
        }

        // Authority-only: move the chosen potion from the payer's rack to the receiver's.
        private void HandOverTribute(NetworkedPotion given)
        {
            if (given == null)
            {
                ClearTribute();
                return;
            }

            ulong receiver = tributeReceiver.Value;
            ulong payer = tributePayer.Value;
            PotionType givenType = given.Type;

            int seat = given.RackSeat;
            int slot = given.RackSlot;
            if (seat >= 0 && slot >= 0)
            {
                NetworkedPotion[] slots = SlotsForSeat(seat);
                if (slot < slots.Length)
                {
                    slots[slot] = null;
                }
            }

            given.RackSeat = -1;
            given.RackSlot = -1;
            DespawnPotion(given);

            ClearTribute();

            if (receiver != NoPlayer)
            {
                SpawnPotionForPlayer(givenType, receiver, false, null);
                SetAnnouncement($"{PlayerLabel(payer)} pays tribute to {PlayerLabel(receiver)}.");
            }
        }

        private void ClearTribute()
        {
            if (tributeRoutine != null)
            {
                StopCoroutine(tributeRoutine);
                tributeRoutine = null;
            }
            tributePayer.Value = NoPlayer;
            tributeReceiver.Value = NoPlayer;
        }

        private int CountPotions(int seat)
        {
            NetworkedPotion[] slots = SlotsForSeat(seat);
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    n++;
                }
            }
            return n;
        }

        // ===================== Curse placement (after a Counterspell) =====================

        /// <summary>Is the local player the one choosing where a countered Curse goes back?</summary>
        public bool IsLocalPlayerPlacingCurse =>
            NetworkManager != null && cursePlacementPlayer.Value == NetworkManager.LocalClientId;

        /// <summary>True while any player is choosing a Curse placement (everyone else waits).</summary>
        public bool CursePlacementPending => cursePlacementPlayer.Value != NoPlayer;

        /// <summary>
        /// Countering a Curse used to drop it back and reshuffle the whole deck, which threw away
        /// the most interesting decision in the game — burying it or dropping it right on top of
        /// the next player — and randomised the deck order that Foresight depends on. The player
        /// now chooses, and the turn does not pass until they have.
        /// </summary>
        // Runs on EVERY client. Only the player actually choosing builds anything; the rest just
        // make sure no stale picker is left standing if the choice resolved without them.
        private void OnCursePlacementPlayerChanged(ulong previous, ulong current)
        {
            if (NetworkManager == null)
            {
                return;
            }

            if (current == NetworkManager.LocalClientId)
            {
                ShowCursePlacementPicker();
            }
            else if (choicePicker != null)
            {
                choicePicker.Hide();
            }
        }

        // The four placements, as a row of boxes the chooser looks at and triggers. Before this
        // existed the only way to answer was the keyboard (Ctrl+1..4), which does not exist in a
        // headset — so in VR the announcement asked for a choice that could not be made, and every
        // Counterspell sat until CursePlacementTimeout buried the Curse at random.
        //
        // Deliberately does NOT say how many cards are in the deck: the whole point of choosing is
        // that only the chooser knows where the Curse went.
        private void ShowCursePlacementPicker()
        {
            if (choicePicker == null)
            {
                choicePicker = gameObject.AddComponent<TributeTargetPicker>();
            }

            var ids = new ulong[] { 0, 1, 2, (ulong)RandomPlacement };
            var names = new[]
            {
                "TOP\nthe very next draw",
                "SECOND\none draw away",
                "THIRD\ntwo draws away",
                "LOST\nanywhere in the cauldron"
            };

            choicePicker.Show(ids, names, id => ChooseCursePlacement((int)id),
                "POINT at a slot and PRESS THE TRIGGER to hide the Curse");
        }

        private void BeginCursePlacement(ulong playerId)
        {
            cursePlacementPlayer.Value = playerId;
            SetAnnouncement($"{PlayerLabel(playerId)} countered the Curse! Choose where it goes back: " +
                            "top, 2nd, 3rd, or lost in the cauldron.");

            if (cursePlacementRoutine != null)
            {
                StopCoroutine(cursePlacementRoutine);
            }
            cursePlacementRoutine = StartCoroutine(CursePlacementTimeout(playerId));
        }

        // A player who never answers must not stall the match.
        private IEnumerator CursePlacementTimeout(ulong playerId)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, cursePlacementSeconds));

            if (cursePlacementPlayer.Value == playerId)
            {
                LogCast($"Curse placement timed out for {PlayerLabel(playerId)} — placing at random.");
                CompleteCursePlacement(playerId, RandomPlacement);
            }
        }

        /// <summary>
        /// Choose where the countered Curse is returned: 0 = top (the very next draw), 1 = second,
        /// 2 = third, or <see cref="RandomPlacement"/> for anywhere in the deck.
        /// </summary>
        public void ChooseCursePlacement(int choice)
        {
            ChooseCursePlacementRpc(choice);
        }

        [Rpc(SendTo.Authority)]
        private void ChooseCursePlacementRpc(int choice, RpcParams rpcParams = default)
        {
            ulong playerId = rpcParams.Receive.SenderClientId;
            if (!HasAuthority || cursePlacementPlayer.Value != playerId)
            {
                return; // not this player's choice to make
            }
            CompleteCursePlacement(playerId, choice);
        }

        private void CompleteCursePlacement(ulong playerId, int choice)
        {
            if (cursePlacementRoutine != null)
            {
                StopCoroutine(cursePlacementRoutine);
                cursePlacementRoutine = null;
            }
            cursePlacementPlayer.Value = NoPlayer;

            // Clamped, because the deck can be shorter than the requested depth late in a match.
            int index = (choice >= 0 && choice <= 2)
                ? Mathf.Min(choice, deck.Count)
                : UnityEngine.Random.Range(0, deck.Count + 1);

            deck.Insert(index, PotionType.Curse);
            LogCast($"Curse placed at index {index} of {deck.Count} by {PlayerLabel(playerId)}.");

            // The announcement deliberately does NOT say where it went — only the player who
            // placed it should know that. The authority log above is for debugging, not players.
            SetAnnouncement($"{PlayerLabel(playerId)} slips the Curse back into the cauldron...");
            EndTurnAfterAction();
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

            // Prefer someone who is actually here: skip both eliminated and dormant seats.
            int index = currentTurnIndex.Value;
            bool found = false;
            for (int step = 0; step < turnOrder.Count; step++)
            {
                index = (index + 1) % turnOrder.Count;
                ulong id = turnOrder[index];
                if (!eliminated.Contains(id) && !dormant.Contains(id))
                {
                    found = true;
                    break;
                }
            }

            // Everyone still in the match is dormant. Rather than stall, hand the turn to the
            // first player who has not been eliminated; the grace timers will resolve the match.
            if (!found)
            {
                index = currentTurnIndex.Value;
                for (int step = 0; step < turnOrder.Count; step++)
                {
                    index = (index + 1) % turnOrder.Count;
                    if (!eliminated.Contains(turnOrder[index]))
                    {
                        break;
                    }
                }
            }

            currentTurnIndex.Value = index;
            turnsRemaining.Value = Mathf.Max(1, turns);
            LogCast($"TURN PASSED to {PlayerLabel(CurrentTurnClientId)} for {turnsRemaining.Value} turn(s).");
            AnnounceCurrentTurn();
        }

        private void MoveToPlayer(ulong playerId, int turns)
        {
            // A Hex aimed at someone who has dropped would stall the match on an empty seat.
            int index = IndexOfPlayer(playerId);
            if (index < 0 || eliminated.Contains(playerId) || dormant.Contains(playerId))
            {
                MoveToNextPlayer(1);
                return;
            }

            stirring.Value = false;
            brewing = false;
            drawInProgress = false;
            currentTurnIndex.Value = index;
            turnsRemaining.Value = Mathf.Max(1, turns);
            LogCast($"TURN FORCED to {PlayerLabel(playerId)} for {turnsRemaining.Value} turn(s).");
            AnnounceCurrentTurn();
        }

        /// <summary>
        /// Hold a dropped player's seat open. Their rack is untouched (potions are spawned by the
        /// authority, so they do not leave with the client), their turn is skipped, and the win
        /// check still counts them — so nobody wins by outlasting a network blip.
        /// </summary>
        private void MarkDormant(ulong playerId)
        {
            if (dormant.Contains(playerId) || eliminated.Contains(playerId))
            {
                return;
            }

            dormant.Add(playerId);
            RemoveCurse(playerId); // they cannot answer a Curse while gone
            LogCast($"DROPPED: {PlayerLabel(playerId)} — holding their seat for {disconnectGraceSeconds}s.");
            SetAnnouncement($"{PlayerLabel(playerId)} lost connection — holding their seat...");

            if (dormantTimers.TryGetValue(playerId, out Coroutine existing) && existing != null)
            {
                StopCoroutine(existing);
            }
            dormantTimers[playerId] = StartCoroutine(DormantTimeout(playerId));

            // If it was their turn, play must move on or the match stalls on an absent player.
            if (CurrentTurnClientId == playerId)
            {
                MoveToNextPlayer(1);
            }
        }

        private void ClearAllDormant()
        {
            foreach (Coroutine routine in dormantTimers.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }
            dormantTimers.Clear();
            dormant.Clear();

            // Identify bookkeeping is per-match too. clientAuthIds is deliberately NOT cleared:
            // it maps live clients to who they are, and a fresh match does not change that.
            foreach (Coroutine routine in identifyTimers.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }
            identifyTimers.Clear();
            identifyExpired.Clear();
        }

        private void ClearDormant(ulong playerId)
        {
            dormant.Remove(playerId);
            if (dormantTimers.TryGetValue(playerId, out Coroutine routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
                dormantTimers.Remove(playerId);
            }
        }

        private IEnumerator DormantTimeout(ulong playerId)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, disconnectGraceSeconds));

            dormantTimers.Remove(playerId);
            if (!dormant.Contains(playerId))
            {
                yield break; // they came back
            }

            dormant.Remove(playerId);
            LogCast($"DROPPED: {PlayerLabel(playerId)} did not return — eliminating.");
            SetAnnouncement($"{PlayerLabel(playerId)} did not return.");
            EliminatePlayer(playerId);
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
