using System;
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

        /// <summary>Call from the local player (e.g. hand enters the cauldron).</summary>
        public void RequestDraw()
        {
            RequestDrawRpc(NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Authority)]
        private void RequestDrawRpc(ulong playerId)
        {
            if (!HasAuthority || !gameActive.Value)
            {
                return;
            }
            if (CurrentTurnClientId != playerId)
            {
                return; // not this player's turn
            }
            if (cursedPlayers.Contains(playerId))
            {
                SetAnnouncement($"{PlayerLabel(playerId)} must play a Counterspell before drawing!");
                return;
            }
            if (deck.Count == 0)
            {
                SetAnnouncement("The cauldron is empty!");
                return;
            }

            PotionType drawn = deck[0];
            deck.RemoveAt(0);

            // TODO(scene wiring): spawn a networked potion of `drawn` owned by playerId
            // into that player's rack. For now the draw outcome resolves directly.
            if (drawn == PotionType.Curse)
            {
                cursedPlayers.Add(playerId);
                SetAnnouncement($"{PlayerLabel(playerId)} drew a CURSE! Play a Counterspell to survive.");
            }
            else
            {
                SetAnnouncement($"{PlayerLabel(playerId)} drew a card.");
                AdvanceTurn();
            }
        }

        // ===================== Cast (client -> authority) =====================

        /// <summary>Call from the local player when a potion is placed in the play zone.</summary>
        public void RequestCast(PotionType type, ulong targetId)
        {
            RequestCastRpc(type, NetworkManager.LocalClientId, targetId);
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
            SetAnnouncement($"{PlayerLabel(CurrentTurnClientId)}'s turn.");
        }

        private void SetAnnouncement(string text)
        {
            announcement.Value = new FixedString512Bytes(text);
        }

        // Placeholder label until wired to XRINetworkPlayer names/colours.
        private string PlayerLabel(ulong id)
        {
            return $"Player {id}";
        }
    }
}
