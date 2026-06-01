// ============================================================
//  DeckManager.cs
//  Owns the draw pile and discard pile.
//  PotInteraction calls DrawRandom() to pull a tube.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhocastThat
{
    public class DeckManager : MonoBehaviour
    {
        public static DeckManager Instance { get; private set; }

        // ── Tube counts ───────────────────────────────────────
        private static readonly (TubeType type, int count)[] BaseDeck =
        {
            (TubeType.Hex,          5),
            (TubeType.Tribute,      4),
            (TubeType.Dispel,       4),
            (TubeType.Foresight,    5),
            (TubeType.Warp,         4),
            (TubeType.Phase,        4),
            (TubeType.Reflection,   4),
            (TubeType.Counterspell, 6),
            (TubeType.Curse,        4),
        };

        // ── State ─────────────────────────────────────────────
        private List<TubeData> drawPile    = new();
        private List<TubeData> discardPile = new();

        // For Reflection — what was the last tube played?
        public TubeData LastPlayed { get; private set; }

        public int DrawCount    => drawPile.Count;
        public int DiscardCount => discardPile.Count;

        // ── Events ────────────────────────────────────────────
        public event Action OnDeckChanged;

        // ─────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start() => BuildDeck();

        // ═════════════════════════════════════════════════════
        //  BUILD
        // ═════════════════════════════════════════════════════

        public void BuildDeck()
        {
            drawPile.Clear();
            discardPile.Clear();
            LastPlayed = null;

            int id = 0;
            foreach (var (type, count) in BaseDeck)
                for (int i = 0; i < count; i++)
                    drawPile.Add(new TubeData(id++, type));

            Shuffle(drawPile);
            OnDeckChanged?.Invoke();
            Debug.Log($"[Deck] Built and shuffled. {DrawCount} tubes ready.");
        }

        // ═════════════════════════════════════════════════════
        //  DRAW — called by PotInteraction when hand enters pot
        // ═════════════════════════════════════════════════════

        /// <summary>
        /// Pulls a random tube from the draw pile.
        /// Returns null if the deck is empty (rebuilds automatically).
        /// </summary>
        public TubeData DrawRandom()
        {
            if (drawPile.Count == 0)
            {
                Debug.Log("[Deck] Deck empty — rebuilding.");
                BuildDeck();
            }

            int idx  = UnityEngine.Random.Range(0, drawPile.Count);
            var tube = drawPile[idx];
            drawPile.RemoveAt(idx);
            OnDeckChanged?.Invoke();
            Debug.Log($"[Deck] Drew: {tube}");
            return tube;
        }

        // ═════════════════════════════════════════════════════
        //  DISCARD — called after a tube is played or resolved
        // ═════════════════════════════════════════════════════

        public void Discard(TubeData tube)
        {
            if (tube == null) return;
            discardPile.Add(tube);
            LastPlayed = tube;
            OnDeckChanged?.Invoke();
        }

        // ═════════════════════════════════════════════════════
        //  SPECIAL DECK OPS used by abilities
        // ═════════════════════════════════════════════════════

        /// <summary>Warp: shuffle the draw pile.</summary>
        public void Shuffle()
        {
            Shuffle(drawPile);
            OnDeckChanged?.Invoke();
            Debug.Log("[Deck] Warped — draw pile shuffled.");
        }

        /// <summary>
        /// Foresight: peek at top N without removing them.
        /// Returns whatever is currently at the front of the pile.
        /// </summary>
        public List<TubeData> PeekTop(int count = 3)
        {
            var result = new List<TubeData>();
            for (int i = 0; i < Mathf.Min(count, drawPile.Count); i++)
                result.Add(drawPile[i]);
            return result;
        }

        /// <summary>
        /// Counterspell: secretly insert a Curse at any position.
        /// 0 = top, DrawCount = bottom.
        /// </summary>
        public void InsertAt(TubeData tube, int position)
        {
            position = Mathf.Clamp(position, 0, drawPile.Count);
            drawPile.Insert(position, tube);
            OnDeckChanged?.Invoke();
            Debug.Log($"[Deck] Inserted {tube.Type} at position {position}.");
        }

        /// <summary>
        /// Draw from the front (index 0) without randomising.
        /// Used internally by ability effects that force a draw.
        /// </summary>
        public TubeData DrawTop()
        {
            if (drawPile.Count == 0) BuildDeck();
            var tube = drawPile[0];
            drawPile.RemoveAt(0);
            OnDeckChanged?.Invoke();
            return tube;
        }

        // ─────────────────────────────────────────────────────
        private static void Shuffle(List<TubeData> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}