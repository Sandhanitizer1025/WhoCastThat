using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// The Foresight reveal: the top potions of the deck rise out of the cauldron, in draw order,
    /// for a few seconds.
    ///
    /// This is purely LOCAL presentation and that is the whole point. Nothing here is networked:
    /// each client builds its own view from whichever message it received, and only the caster's
    /// client is ever sent the actual potion types. Everyone else is told a count and nothing
    /// more, so they can only build anonymous tubes. Broadcasting the real types and hiding them
    /// in the UI would put the secret on every machine in the session, where a curious player
    /// could read it straight out of the scene.
    ///
    /// The tubes are built by cloning the potion prefab's <c>Visual</c> child rather than the
    /// prefab itself, which keeps the NetworkObject and NetworkedPotion behaviours out of a
    /// throwaway decoration — an unspawned NetworkObject is not something to leave lying around.
    /// </summary>
    public class ForesightDisplay : MonoBehaviour
    {
        private const float RiseSeconds = 0.55f;
        private const float Spacing = 0.105f;
        private const float HeightAboveCauldron = 0.32f;
        private const float LabelHeight = 0.115f;

        // What the other players see: one anonymous colour for every tube, so the reveal reads as
        // "three potions came out" and nothing else.
        private static readonly Color UnknownColour = new(0.58f, 0.56f, 0.68f);

        private static readonly int SideColourId = Shader.PropertyToID("_SideColour");
        private static readonly int TopColourId = Shader.PropertyToID("_TopColour");

        private readonly List<GameObject> shown = new();
        private Coroutine routine;

        /// <summary>
        /// Show <paramref name="count"/> potions rising from the cauldron. Pass the real
        /// <paramref name="types"/> to reveal them with captions (the caster), or null to show
        /// anonymous tubes (everybody else).
        /// </summary>
        public void Reveal(PotionType[] types, int count, GameObject potionPrefab, Transform cauldron, float seconds)
        {
            Clear();

            if (potionPrefab == null || cauldron == null || count <= 0)
            {
                return;
            }

            Transform visualSource = potionPrefab.transform.Find("Visual");
            if (visualSource == null)
            {
                return; // prefab restructured — better to show nothing than a broken reveal
            }

            // Lay the tubes out across the player's view rather than along a world axis, so the
            // row reads left-to-right in draw order from wherever this player is sitting.
            Camera view = Camera.main;
            Vector3 right = view != null ? view.transform.right : Vector3.right;
            right.y = 0f;
            right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;

            Vector3 centre = cauldron.position + (Vector3.up * HeightAboveCauldron);
            float start = -(count - 1) * 0.5f * Spacing;

            for (int i = 0; i < count; i++)
            {
                Vector3 target = centre + (right * (start + (i * Spacing)));
                GameObject tube = BuildTube(visualSource, target, types, i);
                shown.Add(tube);
            }

            // ONE caption for the whole row rather than one per tube. A PotionLabel panel is
            // ~0.27 m wide, so three of them over tubes 0.105 m apart overlap almost completely
            // and collapse into an unreadable bar. Numbering the list keeps the mapping obvious
            // without dragging the tubes 0.6 m apart to make room for separate captions.
            if (types != null)
            {
                shown.Add(BuildCaption(centre, types, count));
            }

            routine = StartCoroutine(RiseThenClear(cauldron.position, seconds));
        }

        private GameObject BuildTube(Transform visualSource, Vector3 target, PotionType[] types, int index)
        {
            // A plain container standing in for the potion root, so the cloned Visual keeps the
            // same local offset and scale it has on the prefab and ends up centred on the origin.
            var tube = new GameObject($"ForesightPreview_{index}");
            tube.transform.SetParent(transform, false);
            tube.transform.position = target;
            tube.transform.rotation = Quaternion.identity;

            Instantiate(visualSource.gameObject, tube.transform, false);

            bool revealed = types != null && index < types.Length;
            Color colour = revealed ? NetworkedPotion.ColorFor(types[index]) : UnknownColour;

            foreach (Renderer r in tube.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (!r.gameObject.name.ToLower().Contains("liquid"))
                {
                    continue;
                }

                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor(SideColourId, new Color(colour.r * 0.75f, colour.g * 0.75f, colour.b * 0.75f, 1f));
                block.SetColor(TopColourId, colour);
                r.SetPropertyBlock(block);
            }

            return tube;
        }

        // The ordered list of what is coming, top of the deck first.
        private GameObject BuildCaption(Vector3 centre, PotionType[] types, int count)
        {
            var anchor = new GameObject("ForesightCaption");
            anchor.transform.SetParent(transform, false);
            anchor.transform.position = centre;
            anchor.transform.rotation = Quaternion.identity;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count && i < types.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                // Numbered, because Foresight is only useful if you know which one comes first.
                sb.Append(i + 1).Append(". ").Append(PotionInfo.DisplayName(types[i]));
            }

            PotionLabel label = PotionLabel.Create(anchor.transform, LabelHeight);
            label.Show(sb.ToString());
            return anchor;
        }

        // Float up out of the pot, hold, then vanish. Purely cosmetic: the reveal has already
        // happened as far as the game is concerned, so nothing waits on this.
        private IEnumerator RiseThenClear(Vector3 from, float seconds)
        {
            var targets = new Vector3[shown.Count];
            for (int i = 0; i < shown.Count; i++)
            {
                targets[i] = shown[i].transform.position;
                shown[i].transform.position = from;
            }

            float t = 0f;
            while (t < RiseSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / RiseSeconds));
                for (int i = 0; i < shown.Count; i++)
                {
                    if (shown[i] != null)
                    {
                        shown[i].transform.position = Vector3.Lerp(from, targets[i], k);
                    }
                }
                yield return null;
            }

            yield return new WaitForSeconds(Mathf.Max(0.5f, seconds));
            routine = null;
            Clear();
        }

        private void Clear()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i] != null)
                {
                    Destroy(shown[i]);
                }
            }
            shown.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}
