using System.Collections;
using TMPro;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// A small world-space caption that floats above a potion — used both for the hover
    /// tooltip (what this potion does) and for the draw reveal (what you just brewed).
    ///
    /// It builds itself from code rather than from a prefab so a potion needs no extra
    /// wiring: <see cref="NetworkedPotion"/> just calls <see cref="Create"/>. Swap this for a
    /// designed prefab later by replacing the body of Create — nothing else needs to change.
    ///
    /// The label is purely local/presentational. It is never networked: each client decides
    /// what its own player can see, which is what keeps Foresight-style hidden information
    /// honest.
    /// </summary>
    public class PotionLabel : MonoBehaviour
    {
        // Everything is authored at 100x and scaled down, because TextMeshPro's font sizing
        // behaves badly at very small world scales (hinting and rich-text metrics degrade).
        // Authored units x RootScale = metres, so PanelWidth 20 is a 0.20 m caption.
        private const float RootScale = 0.01f;
        private const float PanelWidth = 24f;

        // Authored units, NOT centimetres. With LiberationSans SDF one line of text measures
        // roughly 0.113 x FontSize authored units, so 18 gives a ~20 mm line height in world
        // space — about 1.5 degrees at the ~0.75 m a seated player views their own rack from.
        // (The original 2.6 here was written as if it were centimetres, which rendered the text
        // ~9x too small inside a fixed slab. Panel height is now measured, never assumed.)
        private const float FontSize = 18f;

        // Breathing room between the text block and the panel edge, in authored units.
        private const float PaddingX = 1.5f;
        private const float PaddingY = 1.2f;

        private TextMeshPro text;
        private Transform panel;
        private Renderer panelRenderer;
        private Coroutine timedRoutine;
        private Camera billboardTarget;

        /// <summary>
        /// Recolour the backing panel. Used by the target picker to show which box is selected;
        /// tooltips and draw banners keep the default.
        /// </summary>
        public void SetPanelColor(Color colour)
        {
            if (panelRenderer != null && panelRenderer.sharedMaterial != null)
            {
                panelRenderer.sharedMaterial.color = colour;
            }
        }

        /// <summary>Build a label parented to a potion, sitting <paramref name="heightOffset"/> above it.</summary>
        public static PotionLabel Create(Transform parent, float heightOffset)
        {
            var root = new GameObject("PotionLabel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, heightOffset, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * RootScale;

            PotionLabel label = root.AddComponent<PotionLabel>();
            label.Build();
            root.SetActive(false);
            return label;
        }

        private void Build()
        {
            // Backing panel so the caption stays readable against the busy room. Built from a
            // primitive quad with its collider stripped — the label must never block the XR ray.
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider quadCollider = quad.GetComponent<Collider>();
            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            quad.name = "Panel";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just behind the text
            quad.transform.localScale = new Vector3(PanelWidth, PanelWidth, 1f); // resized to fit in Apply
            panel = quad.transform;

            panelRenderer = quad.GetComponent<Renderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                unlit = Shader.Find("Unlit/Color");
            }

            if (unlit != null)
            {
                var mat = new Material(unlit);
                SetTransparent(mat);
                mat.color = new Color(0.05f, 0.03f, 0.10f, 0.82f);
                panelRenderer.sharedMaterial = mat;
            }
            else
            {
                // No shader we recognise — drop the panel rather than render an opaque white slab.
                panelRenderer.enabled = false;
            }

            panelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            panelRenderer.receiveShadows = false;

            // RectTransform is created up front: TextMeshPro reads its rect for layout, and a
            // GameObject made with `new GameObject()` starts with a plain Transform.
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = Vector3.zero;
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;

            text = textGo.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(PanelWidth, PanelWidth);
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = true;
            text.color = Color.white;

            Renderer textRenderer = textGo.GetComponent<Renderer>();
            if (textRenderer != null)
            {
                textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
            }
        }

        // URP/Unlit defaults to opaque; flip the surface keywords so alpha actually applies.
        private static void SetTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f); // 0 = opaque, 1 = transparent
            mat.SetFloat("_Blend", 0f);   // alpha blend
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        /// <summary>Show the label until something hides it.</summary>
        public void Show(string content)
        {
            StopTimed();
            Apply(content);
        }

        /// <summary>Show the label for a fixed time, then hide it again.</summary>
        public void ShowFor(string content, float seconds)
        {
            StopTimed();
            Apply(content);
            timedRoutine = StartCoroutine(HideAfter(seconds));
        }

        public void Hide()
        {
            StopTimed();
            if (gameObject != null)
            {
                gameObject.SetActive(false);
            }
        }

        private void Apply(string content)
        {
            // Active first: TextMeshPro will not lay out on a disabled GameObject, and the
            // panel is sized from that layout.
            gameObject.SetActive(true);

            if (text != null)
            {
                text.text = content;
                FitPanelToText();
            }
        }

        /// <summary>
        /// Grow the backing panel to whatever the text actually needs. The tooltip runs to five
        /// lines for the wordier potions while the draw banner is two, so a single fixed height
        /// either clips one or leaves the other floating in a slab of empty purple.
        /// </summary>
        private void FitPanelToText()
        {
            Vector2 needed = text.GetPreferredValues(text.text, PanelWidth, 0f);
            float height = needed.y + (PaddingY * 2f);

            text.rectTransform.sizeDelta = new Vector2(PanelWidth, height);

            if (panel != null)
            {
                panel.localScale = new Vector3(PanelWidth + (PaddingX * 2f), height, 1f);
            }
        }

        private void StopTimed()
        {
            if (timedRoutine != null)
            {
                StopCoroutine(timedRoutine);
                timedRoutine = null;
            }
        }

        private IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            timedRoutine = null;
            gameObject.SetActive(false);
        }

        // Face the local player. The label is parented to a potion that can be rotated in a
        // hand, so this has to run every frame in LateUpdate, after the grab has moved it.
        private void LateUpdate()
        {
            if (billboardTarget == null)
            {
                billboardTarget = Camera.main;
                if (billboardTarget == null)
                {
                    return;
                }
            }

            Vector3 toCamera = transform.position - billboardTarget.transform.position;
            if (toCamera.sqrMagnitude < 1e-6f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }
    }
}
