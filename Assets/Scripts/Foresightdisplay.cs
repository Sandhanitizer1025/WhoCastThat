// ============================================================
//  ForesightDisplay.cs
//
//  Shows the top 3 peeked tubes as floating world-space labels
//  above the pot for a few seconds, then hides them.
//
//  Attach to an empty GameObject. Assign 3 child GameObjects
//  as the tube label slots (each with a TextMeshPro component).
//
//  For true "private view" in VR multiplayer later, put these
//  objects on a camera layer only the local player's camera sees.
// ============================================================
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WhocastThat
{
    public class ForesightDisplay : MonoBehaviour
    {
        [Header("3 label slots (assign in Inspector)")]
        [SerializeField] private TextMeshPro[] labelSlots = new TextMeshPro[3];

        [Header("Colors matching your tube colors")]
        [SerializeField] private Color[] tubeColors = new Color[9]
        {
            new Color(0.55f, 0.35f, 0.18f), // Hex          — brown
            new Color(0.95f, 0.80f, 0.10f), // Tribute      — yellow
            new Color(0.85f, 0.12f, 0.12f), // Dispel       — red
            new Color(0.95f, 0.50f, 0.70f), // Foresight    — pink
            new Color(0.55f, 0.55f, 0.55f), // Warp         — grey
            new Color(0.18f, 0.72f, 0.22f), // Phase        — green
            new Color(0.15f, 0.45f, 0.90f), // Reflection   — blue
            new Color(0.10f, 0.80f, 0.85f), // Counterspell — cyan
            new Color(0.25f, 0.05f, 0.35f), // Curse        — dark purple
        };

        [SerializeField] private float displaySeconds = 4f;

        private Coroutine hideCoroutine;

        // ─────────────────────────────────────────────────────
        private void Awake() => gameObject.SetActive(false);

        // ═════════════════════════════════════════════════════
        //  Called by TablePlayZone after Foresight resolves
        // ═════════════════════════════════════════════════════

        public void Show(List<TubeData> tubes)
        {
            gameObject.SetActive(true);

            for (int i = 0; i < labelSlots.Length; i++)
            {
                if (labelSlots[i] == null) continue;

                if (i < tubes.Count)
                {
                    var tube = tubes[i];
                    labelSlots[i].text  = tube.Type.ToString();
                    labelSlots[i].color = (int)tube.Type < tubeColors.Length
                                        ? tubeColors[(int)tube.Type]
                                        : Color.white;
                    labelSlots[i].gameObject.SetActive(true);
                }
                else
                {
                    labelSlots[i].gameObject.SetActive(false);
                }
            }

            if (hideCoroutine != null) StopCoroutine(hideCoroutine);
            hideCoroutine = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(displaySeconds);
            gameObject.SetActive(false);
        }
    }
}