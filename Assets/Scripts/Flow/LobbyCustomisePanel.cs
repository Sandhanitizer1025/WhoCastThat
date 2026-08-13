using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoCastThat.Visuals;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// A Customise button on the mirror menu, opening a panel with a live preview of the hat.
    ///
    /// The preview is a MANNEQUIN, not a reflection of the room. That is the important design
    /// choice here: the local player's avatar head sits at their own eye position, so reflecting
    /// it means either rendering a head across their whole view or splitting layers between two
    /// cameras. A preview head parked far below the world, filmed by its own camera, needs
    /// neither — it is isolated by distance instead of by layer, so no project-wide layer setup
    /// changes and nothing else can ever wander into shot.
    ///
    /// Replaces an earlier attempt that floated swatches beside the mirror, where the mirror's
    /// own frame geometry cut straight through them.
    /// </summary>
    [DefaultExecutionOrder(110)] // after LobbySettingsPanel has claimed its own panel
    public class LobbyCustomisePanel : MonoBehaviour
    {
        private const string LobbySceneName = "LobbyMirrorScene";

        private static readonly Color PanelBg = new(0.05f, 0.02f, 0.12f, 0.88f);
        private static readonly Color Accent = new(0.72f, 0.45f, 1f, 1f);
        private static readonly Color TextCol = new(0.95f, 0.92f, 1f, 1f);
        private static readonly Color BtnNormal = new(0.24f, 0.11f, 0.44f, 0.92f);
        private static readonly Color BtnHi = new(0.52f, 0.30f, 0.88f, 1f);
        private static readonly Color BtnPress = new(0.14f, 0.05f, 0.28f, 1f);

        // Far enough under the world that nothing else is ever in frame.
        private static readonly Vector3 StageOrigin = new(0f, -500f, 0f);

        private const float PreviewSpinDegreesPerSecond = 24f;

        private PlayerHatLibrary library;
        private TMP_FontAsset menuFont;
        private MagicMirrorMenu menu;

        private GameObject panel;
        private Transform stage;        // where the preview head lives
        private Transform hatMount;
        private GameObject wornPreview;
        private Camera previewCamera;
        private RenderTexture previewTexture;

        private readonly System.Collections.Generic.List<Outline> marks = new();
        private readonly System.Collections.Generic.List<Color> colours = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Additive)
            {
                Spawn(scene.name);
            }
        }

        private static void Spawn(string sceneName)
        {
            if (sceneName != LobbySceneName || FindAnyObjectByType<LobbyCustomisePanel>() != null)
            {
                return;
            }

            new GameObject("LobbyCustomisePanel").AddComponent<LobbyCustomisePanel>();
        }

        private void Start()
        {
            library = Resources.Load<PlayerHatLibrary>("PlayerHatLibrary");
            menu = FindAnyObjectByType<MagicMirrorMenu>();

            if (library == null || library.Hats.Length == 0 || menu == null)
            {
                return;
            }

            var menuRect = menu.GetComponent<RectTransform>();
            Transform main = menu.transform.Find("MainPanel");
            if (menuRect == null || main == null)
            {
                Debug.LogWarning("[LobbyFlow] No MainPanel — cannot add a Customise button.");
                return;
            }

            TMP_Text sample = menu.GetComponentInChildren<TMP_Text>(true);
            menuFont = sample != null ? sample.font : null;

            BuildStage();
            BuildPanel(menuRect);
            AddCustomiseButton((RectTransform)main);

            panel.SetActive(false);
            SetStageActive(false);

            HatPreference.Reapply();
            Refresh(XRMultiplayer.XRINetworkGameManager.LocalPlayerColor.Value);
        }

        private void OnDestroy()
        {
            if (previewTexture != null)
            {
                previewTexture.Release();
                Destroy(previewTexture);
            }
        }

        private void Update()
        {
            // Only spin while the player is actually looking at it.
            if (stage != null && panel != null && panel.activeSelf)
            {
                stage.Rotate(Vector3.up, PreviewSpinDegreesPerSecond * Time.deltaTime, Space.World);
            }
        }

        // ---- the mannequin ----

        private void BuildStage()
        {
            var stageGo = new GameObject("HatPreviewStage");
            stageGo.transform.position = StageOrigin;
            stage = stageGo.transform;

            // Borrow the head from the rig's offline avatar so the preview is the same head the
            // player wears in a match, rather than a stand-in that flatters the fit.
            Transform head = FindOfflineAvatarHead();
            if (head != null)
            {
                GameObject copy = Instantiate(head.gameObject, stage);
                copy.name = "PreviewHead";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                StripBehaviours(copy);
                SetActiveDeep(copy);
            }

            hatMount = new GameObject("HatMount").transform;
            hatMount.SetParent(stage, false);

            var light = new GameObject("PreviewLight").AddComponent<Light>();
            light.transform.SetParent(stage, false);
            light.transform.localPosition = new Vector3(0.4f, 0.6f, -0.6f);
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 3f;

            // Deliberately NOT parented to the stage: the stage rotates, and a camera riding it
            // would turn with the head and show a motionless portrait.
            var camGo = new GameObject("HatPreviewCamera");
            camGo.transform.position = StageOrigin + new Vector3(0f, 0.12f, -0.85f);
            camGo.transform.LookAt(StageOrigin);

            previewTexture = new RenderTexture(512, 512, 16) { name = "HatPreviewTexture" };

            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.targetTexture = previewTexture;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.04f, 0.02f, 0.09f, 1f);
            previewCamera.fieldOfView = 40f;
            previewCamera.nearClipPlane = 0.05f;

            // Nothing else is 500m down, so the far plane doubles as the isolation: the camera
            // physically cannot see the lobby, which is why no culling layer is needed.
            previewCamera.farClipPlane = 5f;
            previewCamera.enabled = false; // only renders while the panel is open
        }

        private static Transform FindOfflineAvatarHead()
        {
            Unity.XR.CoreUtils.XROrigin origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null)
            {
                return null;
            }

            // Inactive in the rig by default — the local player is not meant to see their own
            // head — so this has to search inactive objects too.
            Transform[] all = origin.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "Avatar_Head")
                {
                    return all[i];
                }
            }
            return null;
        }

        private static void SetActiveDeep(GameObject go)
        {
            go.SetActive(true);
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.SetActive(true);
            }
        }

        private static void StripBehaviours(GameObject go)
        {
            // The avatar head carries IK and networking helpers that have no meaning on a
            // mannequin and would log their way through every frame the panel is open.
            MonoBehaviour[] mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < mbs.Length; i++)
            {
                if (mbs[i] != null)
                {
                    Destroy(mbs[i]);
                }
            }
        }

        private void SetStageActive(bool on)
        {
            if (stage != null)
            {
                stage.gameObject.SetActive(on);
            }
            if (previewCamera != null)
            {
                // A camera rendering to a texture nobody is looking at is pure cost on Quest.
                previewCamera.enabled = on;
            }
        }

        // ---- the panel ----

        private void AddCustomiseButton(RectTransform main)
        {
            // Quit sits at y=-420 and the panel runs to -900, so there is room for one more row
            // at the established 170 spacing without touching the existing layout.
            Button button = MakeButton(main, "Customise", new Vector2(0f, -590f), new Vector2(700f, 140f));
            button.onClick.AddListener(Open);
        }

        private void BuildPanel(RectTransform menuRect)
        {
            var go = new GameObject("CustomisePanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(menuRect, false);
            go.GetComponent<Image>().color = PanelBg;
            panel = go;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Label(rt, "CUSTOMISE", 72f, Accent, new Vector2(0f, 720f), new Vector2(940f, 140f), FontStyles.Bold);

            // The mirror.
            var view = new GameObject("Mirror", typeof(RectTransform));
            view.transform.SetParent(rt, false);
            var vrt = (RectTransform)view.transform;
            vrt.anchoredPosition = new Vector2(0f, 340f);
            vrt.sizeDelta = new Vector2(520f, 520f);
            var raw = view.AddComponent<RawImage>();
            raw.texture = previewTexture;

            // Flipped horizontally so it behaves like a mirror rather than a video call.
            raw.uvRect = new Rect(1f, 0f, -1f, 1f);
            raw.raycastTarget = false;

            Label(rt, "Choose your hat", 34f, TextCol, new Vector2(0f, 40f),
                  new Vector2(940f, 60f), FontStyles.Italic);

            // Swatches in a row under the mirror, so the panel stays one screenful.
            int count = library.Hats.Length;
            float width = 150f;
            float gap = 18f;
            float total = count * width + (count - 1) * gap;
            float x = -total * 0.5f + width * 0.5f;

            for (int i = 0; i < count; i++)
            {
                PlayerHatLibrary.Hat hat = library.Hats[i];
                if (hat.prefab == null)
                {
                    continue;
                }

                colours.Add(hat.colour);
                string label = string.IsNullOrEmpty(hat.label) ? hat.prefab.name.Replace("hat_", "") : hat.label;
                MakeSwatch(rt, label, hat.colour, new Vector2(x + i * (width + gap), -110f), width);
            }

            Button back = MakeButton(rt, "Back", new Vector2(0f, -420f), new Vector2(500f, 130f));
            back.onClick.AddListener(Close);
        }

        private void Open()
        {
            panel.SetActive(true);
            SetStageActive(true);
            if (menu.mainPanel != null)
            {
                menu.mainPanel.SetActive(false);
            }
            Refresh(XRMultiplayer.XRINetworkGameManager.LocalPlayerColor.Value);
        }

        private void Close()
        {
            panel.SetActive(false);
            SetStageActive(false);
            if (menu.mainPanel != null)
            {
                menu.mainPanel.SetActive(true);
            }
        }

        private void Choose(Color colour)
        {
            // Saved as well as applied, so it survives into the tutorial and the match.
            HatPreference.Set(colour);
            Refresh(colour);
        }

        private void Refresh(Color colour)
        {
            GameObject source = library.Nearest(colour);

            if (source != null && hatMount != null)
            {
                if (wornPreview != null)
                {
                    Destroy(wornPreview);
                }

                wornPreview = Instantiate(source, hatMount);
                StripBehaviours(wornPreview);
                FitPreviewHat(wornPreview);
            }

            // After the hat, because the hat is what changes the subject's height.
            FrameCamera();

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < colours.Count; i++)
            {
                float dr = colours[i].r - colour.r;
                float dg = colours[i].g - colour.g;
                float db = colours[i].b - colour.b;
                float d = dr * dr + dg * dg + db * db;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }

            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i] != null)
                {
                    marks[i].enabled = i == best;
                }
            }
        }

        /// <summary>
        /// Point the camera at what is actually on the stage, and back off far enough to hold all
        /// of it.
        ///
        /// The first version aimed at a hand-picked point 10cm above the stage origin and sat at a
        /// fixed 0.85m. The subject fitted by size but was framed wrong — its top edge landed at
        /// viewport 1.16, i.e. the top of the hat was cut off — because the avatar head is not
        /// centred on the stage origin and the hat then adds height above it. Measuring is the
        /// only way this stays correct when a taller hat is added later.
        /// </summary>
        private void FrameCamera()
        {
            if (previewCamera == null || stage == null)
            {
                return;
            }

            Renderer[] rs = stage.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0)
            {
                return;
            }

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
            {
                b.Encapsulate(rs[i].bounds);
            }

            // Radius rather than height: the stage spins, so the widest silhouette it will ever
            // present is what has to fit, not the pose it happens to be in right now.
            float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            float halfFov = previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = (radius / Mathf.Tan(halfFov)) * 1.35f; // margin so it never touches the edge

            previewCamera.transform.position = b.center + new Vector3(0f, 0f, -distance);
            previewCamera.transform.LookAt(b.center);
            previewCamera.farClipPlane = distance + radius * 4f;
        }

        /// <summary>
        /// Size and seat the preview hat exactly as <see cref="PlayerHat"/> does in the match, so
        /// what the mirror shows is what the player actually gets.
        /// </summary>
        private void FitPreviewHat(GameObject hat)
        {
            hat.transform.localRotation = Quaternion.identity;
            hat.transform.localScale = Vector3.one;

            Renderer[] rs = hat.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0)
            {
                return;
            }

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) { b.Encapsulate(rs[i].bounds); }

            float wide = Mathf.Max(b.size.x, b.size.z);
            if (wide > 0.0001f)
            {
                hat.transform.localScale = Vector3.one * (library.WidthMetres / wide);
            }

            hat.transform.localPosition = Vector3.zero;
            rs = hat.GetComponentsInChildren<Renderer>(true);
            b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) { b.Encapsulate(rs[i].bounds); }

            float baseBelowPivot = hat.transform.position.y - b.min.y;
            hat.transform.localPosition = new Vector3(0f, library.HeightOffset + baseBelowPivot, 0f);
        }

        // ---- widgets ----

        private void MakeSwatch(RectTransform parent, string label, Color colour, Vector2 pos, float width)
        {
            var go = new GameObject(label + "Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 120f);

            var image = go.GetComponent<Image>();
            image.color = colour;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(6f, 6f);
            outline.enabled = false;
            marks.Add(outline);

            var button = go.GetComponent<Button>();
            ColorBlock cb = button.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            button.colors = cb;
            button.targetGraphic = image;

            Color captured = colour;
            button.onClick.AddListener(() => Choose(captured));

            float luminance = colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
            Color textColour = luminance > 0.6f ? new Color(0.08f, 0.05f, 0.12f) : TextCol;
            TextMeshProUGUI text = Label(rt, label, 26f, textColour, Vector2.zero, new Vector2(width, 120f), FontStyles.Bold);
            text.raycastTarget = false;
        }

        private Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = Color.white;

            var btn = go.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = BtnNormal;
            cb.highlightedColor = BtnHi;
            cb.pressedColor = BtnPress;
            cb.selectedColor = BtnHi;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            TextMeshProUGUI txt = Label(rt, label, 46f, TextCol, Vector2.zero, size, FontStyles.Bold);
            txt.raycastTarget = false;
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        private TextMeshProUGUI Label(RectTransform parent, string text, float size, Color colour,
                                      Vector2 pos, Vector2 sizeD, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeD;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (menuFont != null) { tmp.font = menuFont; }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = colour;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            return tmp;
        }
    }
}
