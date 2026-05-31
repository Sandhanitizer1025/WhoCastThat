// ============================================================
//  TablePlayZone.cs
//
//  Attach to the centre-of-table trigger zone (a flat trigger
//  collider, e.g. a thin box set to Is Trigger).
//
//  When a tube is released/placed inside this zone:
//    1. The ability fires — affecting only the local player
//    2. The tube is moved to the discard pile
//    3. PotInteraction is notified to allow the next draw
//
//  All 9 abilities are self-affecting (no target selection yet).
//
//  Inspector setup:
//    • potInteraction   — drag your Pot GameObject here
//    • foresightDisplay — drag your ForesightDisplay GameObject here
//    • gameLog          — optional TextMeshPro for event log
// ============================================================
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;


namespace WhocastThat
{
    [RequireComponent(typeof(Collider))]
    public class TablePlayZone : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PotInteraction   potInteraction;
        [SerializeField] private ForesightDisplay foresightDisplay;
        [SerializeField] private TextMeshPro      gameLog;

        // ── Player state ──────────────────────────────────────
        private PlayerHand player     = new PlayerHand();
        private TubeData   pendingCurse = null; // Curse waiting for Counterspell
        private bool       processing   = false;

        // ═════════════════════════════════════════════════════
        private void Awake() => GetComponent<Collider>().isTrigger = true;

        // ═════════════════════════════════════════════════════
        //  TRIGGER  — detect tube placed / dropped in zone
        // ═════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other) => TryActivate(other);
        private void OnTriggerStay(Collider other)  => TryActivate(other);

        private void TryActivate(Collider other)
        {
            if (processing) return;

            var tubeObj = other.GetComponentInParent<TubeObject>();
            if (tubeObj == null || tubeObj.Data == null) return;

            // Only fire once the player has released the tube
            var grab = other.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null && grab.isSelected) return;

            StartCoroutine(ActivateTube(tubeObj, other.gameObject));
        }

        // ═════════════════════════════════════════════════════
        //  ACTIVATE SEQUENCE
        // ═════════════════════════════════════════════════════

        private IEnumerator ActivateTube(TubeObject tubeObj, GameObject physicalTube)
        {
            processing = true;

            var data = tubeObj.Data;
            Log($"Playing: {data.Type}");

            // Track it in the logical hand during resolution
            player.Add(data);
            FireAbility(data);

            yield return new WaitForSeconds(0.4f);

            // Clean up
            player.Remove(data);
            DeckManager.Instance.Discard(data);
            Destroy(physicalTube);
            potInteraction?.ClearHeldTube();

            processing = false;
        }

        // ═════════════════════════════════════════════════════
        //  ABILITY DISPATCH
        // ═════════════════════════════════════════════════════

        private void FireAbility(TubeData tube)
        {
            switch (tube.Type)
            {
                case TubeType.Hex:          AbilityHex();          break;
                case TubeType.Tribute:      AbilityTribute();      break;
                case TubeType.Dispel:       AbilityDispel();       break;
                case TubeType.Foresight:    AbilityForesight();    break;
                case TubeType.Warp:         AbilityWarp();         break;
                case TubeType.Phase:        AbilityPhase();        break;
                case TubeType.Reflection:   AbilityReflection();   break;
                case TubeType.Counterspell: AbilityCounterspell(); break;
                case TubeType.Curse:        AbilityCurse(tube);    break;
            }
        }

        // ═════════════════════════════════════════════════════
        //  ABILITIES  —  all self-affecting
        // ═════════════════════════════════════════════════════

        // ── HEX ──────────────────────────────────────────────
        // Normal: force next player to take 2 turns.
        // Self:   you draw 2 cards right now.
        private void AbilityHex()
        {
            Log("Hex — you draw 2 cards.");
            DrawCards(2);
        }

        // ── TRIBUTE ──────────────────────────────────────────
        // Normal: steal 1 card from target, give 1 back.
        // Self:   draw 1 card, then lose 1 random card.
        private void AbilityTribute()
        {
            Log("Tribute — draw 1 card, then lose 1 random card.");
            DrawCards(1);
            DiscardRandom();
        }

        // ── DISPEL ───────────────────────────────────────────
        // Normal: cancel another player's action.
        // Self:   lose 1 random card as the casting cost.
        private void AbilityDispel()
        {
            Log("Dispel — you lose 1 random card (casting cost).");
            DiscardRandom();
        }

        // ── FORESIGHT ────────────────────────────────────────
        // Normal: peek top 3 cards privately.
        // Self:   those 3 cards move to the bottom of the deck.
        private void AbilityForesight()
        {
            var top3 = DeckManager.Instance.PeekTop(3);
            Log($"Foresight — you peek at {top3.Count} card(s). They move to the bottom.");
            foresightDisplay?.Show(top3);

            // Move the peeked cards to the bottom without reordering
            for (int i = 0; i < top3.Count; i++)
            {
                var t = DeckManager.Instance.DrawTop();
                DeckManager.Instance.InsertAt(t, DeckManager.Instance.DrawCount);
            }
        }

        // ── WARP ─────────────────────────────────────────────
        // Normal: shuffle the deck.
        // Self:   your logical hand is also shuffled.
        private void AbilityWarp()
        {
            Log("Warp — deck shuffled. Your hand shuffled too.");
            DeckManager.Instance.Shuffle();
            ShuffleHand();
        }

        // ── PHASE ────────────────────────────────────────────
        // Normal: target skips their next draw.
        // Self:   you also skip your next draw from the pot.
        private void AbilityPhase()
        {
            Log("Phase — your next draw from the pot is skipped.");
            player.SkipNextDraw = true;
            // PotInteraction.OnTriggerEnter checks player.SkipNextDraw via GameState
        }

        // ── REFLECTION ───────────────────────────────────────
        // Copies the last played ability — applied twice.
        // Cannot copy Dispel.
        private void AbilityReflection()
        {
            var last = DeckManager.Instance.LastPlayed;

            if (last == null)
            {
                Log("Reflection — nothing has been played yet.");
                return;
            }
            if (last.Type == TubeType.Dispel)
            {
                Log("Reflection — cannot copy Dispel.");
                return;
            }
            if (last.Type == TubeType.Reflection)
            {
                Log("Reflection — cannot copy itself.");
                return;
            }

            Log($"Reflection — copying {last.Type} twice.");
            FireAbility(last); // target effect
            FireAbility(last); // self effect
        }

        // ── COUNTERSPELL ─────────────────────────────────────
        // Normal: survive a drawn Curse; secretly reinsert it.
        // Self:   draw 1 card as the casting cost.
        // If no Curse is pending: still costs 1 draw.
        private void AbilityCounterspell()
        {
            if (pendingCurse != null)
            {
                Log("Counterspell — Curse survived! Reinserting at a random position.");
                int pos = Random.Range(0, DeckManager.Instance.DrawCount + 1);
                DeckManager.Instance.InsertAt(pendingCurse, pos);
                pendingCurse = null;
                Log("Counterspell self-cost — draw 1 card.");
                DrawCards(1);
            }
            else
            {
                Log("Counterspell with no active Curse — draw 1 card as wasted cost.");
                DrawCards(1);
            }
        }

        // ── CURSE ────────────────────────────────────────────
        // Drawn from the deck (or triggered mid-draw).
        // Check hand for Counterspell — auto-use if found.
        // Otherwise: game over.
        private void AbilityCurse(TubeData curseTube)
        {
            Log("CURSE! Checking for Counterspell...");
            pendingCurse = curseTube;

            var cs = player.FirstOfType(TubeType.Counterspell);
            if (cs != null)
            {
                Log("Counterspell found — auto-activating.");
                player.Remove(cs);
                DeckManager.Instance.Discard(cs);
                // Reinsert the curse at a random spot
                int pos = Random.Range(0, DeckManager.Instance.DrawCount + 1);
                DeckManager.Instance.InsertAt(curseTube, pos);
                pendingCurse = null;
                Log("Counterspell self-cost — draw 1 card.");
                DrawCards(1);
            }
            else
            {
                Log("No Counterspell — YOU EXPLODE! Game over.");
                player.IsAlive = false;
                OnGameOver();
            }
        }

        // ═════════════════════════════════════════════════════
        //  DRAW HELPER
        //  Pulls [count] random tubes. Curses trigger immediately.
        //  Respects Phase skip flag.
        // ═════════════════════════════════════════════════════

        private void DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (player.SkipNextDraw)
                {
                    player.SkipNextDraw = false;
                    Log("Draw skipped (Phase effect).");
                    continue;
                }

                var tube = DeckManager.Instance.DrawRandom();
                if (tube == null) break;

                Log($"Drew: {tube.Type}");
                player.Add(tube);

                if (tube.Type == TubeType.Curse)
                    AbilityCurse(tube);
            }
        }

        // ── Discard a random card from the logical hand ───────
        private void DiscardRandom()
        {
            // Exclude the tube currently being played (last in list)
            var candidates = new List<TubeData>(player.Tubes);
            if (candidates.Count <= 1) { Log("Hand too small to discard."); return; }
            candidates.RemoveAt(candidates.Count - 1); // don't discard the active tube

            int idx  = Random.Range(0, candidates.Count);
            var lost = candidates[idx];
            player.Remove(lost);
            DeckManager.Instance.Discard(lost);
            Log($"Lost from hand: {lost.Type}");
        }

        // ── Shuffle logical hand order ─────────────────────────
        private void ShuffleHand()
        {
            var h = player.Tubes;
            for (int i = h.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (h[i], h[j]) = (h[j], h[i]);
            }
        }

        // ── Game over handler ──────────────────────────────────
        private void OnGameOver()
        {
            // Hook up your VR game-over screen here
            // GameOverUI.Instance?.Show();
        }

        // ── Log ───────────────────────────────────────────────
        private void Log(string msg)
        {
            Debug.Log($"[Table] {msg}");
            if (gameLog != null) gameLog.text = msg;
        }
    }
}