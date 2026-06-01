// ============================================================
//  StartingHand.cs
//
//  Spawns 7 random tubes in a row at game start.
//  Attach to your GameManager object alongside DeckManager.
//
//  Inspector:
//    tubePrefabs[0..8]  — same array as PotInteraction,
//                         same order (Hex=0 ... Curse=8)
//    handAnchor         — the transform in front of the player
//                         where tubes appear (e.g. a point
//                         just above the table on your side)
//    tubeSpacing        — gap between each tube (default 0.08m)
//    startingCount      — how many tubes to deal (default 7)
// ============================================================
using UnityEngine;

namespace WhocastThat
{
    public class StartingHand : MonoBehaviour
    {
        [Header("Tube Prefabs (index = TubeType, same order as PotInteraction)")]
        [SerializeField] private GameObject[] tubePrefabs = new GameObject[9];

        [Header("Layout")]
        [SerializeField] private Transform handAnchor;
        [SerializeField] private float     tubeSpacing    = 0.08f;
        [SerializeField] private int       startingCount  = 7;

        // ─────────────────────────────────────────────────────
        private void Start()
        {
            // Wait one frame so DeckManager.Start() runs first
            Invoke(nameof(DealStartingHand), 0.05f);
        }

        private void DealStartingHand()
        {
            if (handAnchor == null)
            {
                Debug.LogError("[StartingHand] Hand Anchor not assigned in Inspector.");
                return;
            }

            for (int i = 0; i < startingCount; i++)
            {
                var tubeData = DeckManager.Instance.DrawRandom();
                if (tubeData == null) break;

                int typeIndex = (int)tubeData.Type;
                if (typeIndex >= tubePrefabs.Length || tubePrefabs[typeIndex] == null)
                {
                    Debug.LogError($"[StartingHand] No prefab for TubeType {tubeData.Type}.");
                    continue;
                }

                // Space tubes out in a row along handAnchor's local X axis
                float offset = (i - (startingCount - 1) * 0.5f) * tubeSpacing;
                Vector3 pos  = handAnchor.position
                             + handAnchor.right   * offset
                             + handAnchor.up      * 0f
                             + handAnchor.forward * 0f;

                var go  = Instantiate(tubePrefabs[typeIndex], pos, handAnchor.rotation);
                go.name = $"StartTube_{tubeData.Type}_{i}";

                // Tag with TubeObject so TablePlayZone can read it
                var tag  = go.GetComponent<TubeObject>() ?? go.AddComponent<TubeObject>();
                tag.Data = tubeData;

                Debug.Log($"[StartingHand] Dealt {tubeData.Type} at slot {i}.");
            }

            Debug.Log($"[StartingHand] Dealt {startingCount} starting tubes.");
        }
    }
}