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

        [Header("Cauldron brew")]
        [Tooltip("The floating cauldron rig; drawn potions spawn here and float out to the rack.")]
        [SerializeField] private Transform cauldronRig;

        [Tooltip("Seconds the ladle stirs after the player dips a hand, before the potion floats out.")]
        [SerializeField] private float stirDurationSeconds = 2.5f;

        [Tooltip("Seconds for a drawn potion to float from the pot into the rack slot.")]
        [SerializeField] private float potionFloatSeconds = 2f;

        // Next slot index to fill for each seat, so potions land in tidy tube slots.
        private readonly Dictionary<int, int> nextSlotBySeat = new Dictionary<int, int>();

        // ---- Replicated state (authority writes, everyone reads) ----

        // Turn order by client id; currentTurnIndex indexes into this.
        private readonly NetworkList<ulong> turnOrder = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<int> currentTurnIndex = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Queued extra turns produced by Hex (stacks), applied as the turn advances.
        private readonly NetworkVariable<int> pendingExtraTurns = new(
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

        // Authority-only guard so overlapping dips can't start two brews at once.
        private bool brewing;

        // ---- Client-facing events (UI subscribes to these) ----
        public static event Action<string> AnnouncementChanged;
        public static event Action<ulong> TurnChanged;

        public string CurrentAnnouncement => announcement.Value.ToString();
        public bool GameActive => gameActive.Value;

        public ulong CurrentTurnClientId =>
            (turnOrder.Count > 0 && currentTurnIndex.Value >= 0 && currentTurnIndex.Value < turnOrder.Count)
                ? turnOrder[currentTurnIndex.Value]
                : ulong.MaxValue;

        public bool IsLocalPlayersTurn =>
            NetworkManager != null && CurrentTurnClientId == NetworkManager.LocalClientId;

        /// <summary>Seat (turn-order) index whose turn it is, or -1 before the game starts.</summary>
        public int CurrentSeatIndex =>
            (gameActive.Value && turnOrder.Count > 0) ? currentTurnIndex.Value : -1;

        /// <summary>True while the cauldron is stirring (drives the ladle animation everywhere).</summary>
        public bool IsStirring => stirring.Value;

        /// <summary>Whether a fresh dip can start a brew right now (no brew already in progress).</summary>
        public bool CanBrew => gameActive.Value && !stirring.Value;

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

            SyncTurnOrderWithConnectedClients();
            BuildDeck();

            currentTurnIndex.Value = 0;
            pendingExtraTurns.Value = 0;
            stirring.Value = false;
            brewing = false;
            cursedPlayers.Clear();
            gameActive.Value = turnOrder.Count >= minPlayersToStart;

            if (gameActive.Value)
            {
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

            // Re-sync on any connect/disconnect so it is robust to the different
            // Distributed Authority event types (Client* vs Peer*).
            SyncTurnOrderWithConnectedClients();

            if (!gameActive.Value)
            {
                // Enough mages have gathered — begin the match.
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

            // Game already running: if everyone but one left, that one wins.
            if (turnOrder.Count <= 1)
            {
                gameActive.Value = false;
                string winner = turnOrder.Count == 1 ? PlayerLabel(turnOrder[0]) : "Nobody";
                SetAnnouncement($"{winner} wins! Last mage standing.");
            }
        }

        private void SyncTurnOrderWithConnectedClients()
        {
            var connected = new HashSet<ulong>(NetworkManager.ConnectedClientsIds);

            // Remove players who left.
            for (int i = turnOrder.Count - 1; i >= 0; i--)
            {
                if (!connected.Contains(turnOrder[i]))
                {
                    turnOrder.RemoveAt(i);
                }
            }

            // Add players who joined.
            foreach (ulong id in connected)
            {
                if (!TurnOrderContains(id))
                {
                    turnOrder.Add(id);
                }
            }

            if (turnOrder.Count > 0 && currentTurnIndex.Value >= turnOrder.Count)
            {
                currentTurnIndex.Value %= turnOrder.Count;
            }
        }

        private bool TurnOrderContains(ulong id)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (turnOrder[i] == id)
                {
                    return true;
                }
            }
            return false;
        }

        private void BuildDeck()
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
            AddCards(PotionType.Curse, 4);
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
            if (!CanPlayerDraw(playerId))
            {
                yield break;
            }
            PerformDraw(playerId, true);
        }

        // Shared draw preconditions for both the instant and brew-driven paths.
        private bool CanPlayerDraw(ulong playerId)
        {
            if (!HasAuthority || !gameActive.Value)
            {
                return false;
            }
            if (CurrentTurnClientId != playerId)
            {
                return false; // not this player's turn
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
            return true;
        }

        // Authority-only: take the top card and resolve it. When floatFromPot is true
        // the potion spawns at the cauldron and animates into the rack for everyone.
        private void PerformDraw(ulong playerId, bool floatFromPot)
        {
            PotionType drawn = deck[0];
            deck.RemoveAt(0);

            if (drawn == PotionType.Curse)
            {
                // The curse is an immediate threat and is not added to the hand.
                cursedPlayers.Add(playerId);
                SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE! Play a Counterspell to survive.");
            }
            else
            {
                SpawnPotionForPlayer(drawn, playerId, floatFromPot);
                SetAnnouncement($"{PlayerLabel(playerId)} drew a potion.");
                AdvanceTurn();
            }
        }

        // Authority-only: spawn a networked potion of the given type in front of the
        // player's seat. It is grabbable; grabbing transfers ownership via the
        // NetworkPhysicsInteractable on the prefab.
        private void SpawnPotionForPlayer(PotionType type, ulong playerId, bool floatFromPot)
        {
            if (networkedPotionPrefab == null)
            {
                Debug.LogWarning("[NetworkedSpellGame] No networked potion prefab assigned.", this);
                return;
            }

            int seat = GetSeatIndex(playerId);
            Transform rack = (seatRacks != null && seat >= 0 && seat < seatRacks.Length) ? seatRacks[seat] : null;

            // Resting pose in the rack slot (where the potion ends up).
            Vector3 restPos;
            Quaternion restRot = Quaternion.identity;

            Transform slot = GetNextRackSlot(seat, rack);
            if (slot != null)
            {
                restPos = slot.position + Vector3.up * 0.04f;
                restRot = slot.rotation;
            }
            else if (rack != null)
            {
                restPos = rack.position + Vector3.up * 0.1f;
            }
            else
            {
                restPos = transform.position;
            }

            // Spawn at the cauldron if floating out, otherwise straight into the slot.
            bool canFloat = floatFromPot && cauldronRig != null;
            Vector3 spawnPos = canFloat ? cauldronRig.position + Vector3.up * 0.12f : restPos;

            GameObject potionObject = Instantiate(networkedPotionPrefab, spawnPos, restRot);
            NetworkObject netObj = potionObject.GetComponent<NetworkObject>();
            netObj.Spawn();

            NetworkedPotion potion = potionObject.GetComponent<NetworkedPotion>();
            if (potion != null)
            {
                potion.SetType(type);
            }

            if (canFloat)
            {
                // Authority owns the fresh potion; ClientNetworkTransform replicates
                // this owner-driven motion so every client sees it float to the rack.
                Rigidbody body = potionObject.GetComponent<Rigidbody>();
                StartCoroutine(FloatPotionToRack(potionObject.transform, body, spawnPos, restPos));
            }
        }

        private IEnumerator FloatPotionToRack(Transform potion, Rigidbody body, Vector3 from, Vector3 to)
        {
            // Make it kinematic for the trip so physics doesn't fight our per-frame
            // position writes (otherwise it snaps instead of floating).
            bool wasKinematic = body != null && body.isKinematic;
            if (body != null)
            {
                body.isKinematic = true;
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, potionFloatSeconds);
            while (potion != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float eased = u * u * (3f - 2f * u); // smoothstep
                Vector3 pos = Vector3.Lerp(from, to, eased);
                pos.y += Mathf.Sin(u * Mathf.PI) * 0.08f; // gentle arc lift
                potion.position = pos;
                if (body != null)
                {
                    body.position = pos; // keep the rigidbody in step so the sync is clean
                }
                yield return null;
            }

            if (potion != null)
            {
                potion.position = to;
            }
            if (body != null)
            {
                body.position = to;
                body.isKinematic = wasKinematic; // restore so it can be grabbed normally
            }
        }

        // Rotate through a rack's "Slot" children so drawn potions fill tidy tube slots.
        private Transform GetNextRackSlot(int seat, Transform rack)
        {
            if (rack == null)
            {
                return null;
            }

            var slots = new List<Transform>();
            foreach (Transform child in rack)
            {
                if (child.name.StartsWith("Slot"))
                {
                    slots.Add(child);
                }
            }
            if (slots.Count == 0)
            {
                return null;
            }

            int index = nextSlotBySeat.TryGetValue(seat, out int n) ? n : 0;
            nextSlotBySeat[seat] = index + 1;
            return slots[index % slots.Count];
        }

        // ===================== Cast (client -> authority) =====================

        /// <summary>Cast at an explicit target (used by the keyboard test harness).</summary>
        public void RequestCast(PotionType type, ulong targetId)
        {
            RequestCastRpc(type, NetworkManager.LocalClientId, targetId);
        }

        /// <summary>Cast targeting the next player automatically (used by the play zone).</summary>
        public void RequestCast(PotionType type)
        {
            RequestCastRpc(type, NetworkManager.LocalClientId, ulong.MaxValue);
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

            cursedPlayers.Add(playerId);
            SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE! Play a Counterspell to survive.");
        }

        [Rpc(SendTo.Authority)]
        private void RequestCastRpc(PotionType type, ulong casterId, ulong targetId)
        {
            if (!HasAuthority || !gameActive.Value)
            {
                return;
            }

            // ulong.MaxValue means "target the next player" (used by the play zone).
            if (targetId == ulong.MaxValue)
            {
                targetId = NextPlayerId(casterId);
            }

            // Curse defence takes priority even if it interrupts turn order.
            if (cursedPlayers.Contains(casterId))
            {
                if (type == PotionType.Counterspell)
                {
                    cursedPlayers.Remove(casterId);
                    deck.Add(PotionType.Curse);
                    ShuffleDeck();
                    SetAnnouncement($"{PlayerLabel(casterId)} countered the curse! It is back in the cauldron.");
                    AdvanceTurn();
                }
                else
                {
                    SetAnnouncement($"{PlayerLabel(casterId)} exploded! Wrong potion against a curse.");
                    EliminatePlayer(casterId);
                }
                return;
            }

            if (CurrentTurnClientId != casterId)
            {
                // Dispel / Reflection out-of-turn interrupts are a later milestone.
                return;
            }

            ApplyEffect(type, casterId, targetId);
        }

        private void ApplyEffect(PotionType type, ulong casterId, ulong targetId)
        {
            switch (type)
            {
                case PotionType.Curse:
                    cursedPlayers.Add(casterId);
                    SetAnnouncement($"{PlayerLabel(casterId)} unleashed a Curse — counter it!");
                    return; // stays their turn until resolved

                case PotionType.Hex:
                    pendingExtraTurns.Value += 1;
                    SetAnnouncement($"{PlayerLabel(casterId)} cast Hex on {PlayerLabel(NextPlayerId(casterId))}!");
                    AdvanceTurn();
                    return;

                case PotionType.Warp:
                    ShuffleDeck();
                    SetAnnouncement($"{PlayerLabel(casterId)} shuffled the cauldron.");
                    AdvanceTurn();
                    return;

                case PotionType.Phase:
                    SetAnnouncement($"{PlayerLabel(casterId)} phased out — turn ended without drawing.");
                    AdvanceTurn();
                    return;

                case PotionType.Tribute:
                    SetAnnouncement($"{PlayerLabel(casterId)} demands a Tribute from {PlayerLabel(targetId)}.");
                    AdvanceTurn();
                    return;

                case PotionType.Foresight:
                    SetAnnouncement($"{PlayerLabel(casterId)} peeked at the top of the cauldron.");
                    return; // stays their turn; they still draw or play

                case PotionType.Dispel:
                case PotionType.Reflection:
                case PotionType.Counterspell:
                    SetAnnouncement($"{PlayerLabel(casterId)} played {type}.");
                    AdvanceTurn();
                    return;
            }
        }

        // ===================== Turn + elimination =====================

        private void AdvanceTurn()
        {
            if (turnOrder.Count == 0)
            {
                return;
            }

            // Hex: instead of passing on, the next player is forced to take an extra turn.
            if (pendingExtraTurns.Value > 0)
            {
                pendingExtraTurns.Value -= 1;
            }

            // Fresh cauldron for the next player.
            stirring.Value = false;
            brewing = false;

            currentTurnIndex.Value = (currentTurnIndex.Value + 1) % turnOrder.Count;
            AnnounceCurrentTurn();
        }

        private void EliminatePlayer(ulong playerId)
        {
            cursedPlayers.Remove(playerId);
            RemovePlayer(playerId);

            if (turnOrder.Count <= 1)
            {
                gameActive.Value = false;
                string winner = turnOrder.Count == 1 ? PlayerLabel(turnOrder[0]) : "Nobody";
                SetAnnouncement($"{winner} wins! Last mage standing.");
                return;
            }

            AnnounceCurrentTurn();
        }

        private void RemovePlayer(ulong playerId)
        {
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (turnOrder[i] == playerId)
                {
                    turnOrder.RemoveAt(i);
                    if (turnOrder.Count > 0 && currentTurnIndex.Value >= turnOrder.Count)
                    {
                        currentTurnIndex.Value %= turnOrder.Count;
                    }
                    return;
                }
            }
        }

        private ulong NextPlayerId(ulong fromId)
        {
            if (turnOrder.Count == 0)
            {
                return ulong.MaxValue;
            }

            int index = 0;
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (turnOrder[i] == fromId)
                {
                    index = i;
                    break;
                }
            }
            return turnOrder[(index + 1) % turnOrder.Count];
        }

        private void AnnounceCurrentTurn()
        {
            if (!gameActive.Value)
            {
                return;
            }
            SetAnnouncement($"{PlayerLabel(CurrentTurnClientId)}'s turn — dip your hand in the cauldron.");
        }

        private void SetAnnouncement(string text)
        {
            announcement.Value = new FixedString512Bytes(text);
        }

        // Resolve a player's display name from their networked profile, falling back
        // to a generic label before the name has replicated.
        private string PlayerLabel(ulong id)
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
