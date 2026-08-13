using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Builds the mirror's settings panel at runtime and hands it to <c>MagicMirrorMenu</c>.
    ///
    /// The menu already knows how to show a settings panel — <c>OnSettings</c> swaps it with the
    /// main panel — it just has nothing to show, because the only thing that ever built one was
    /// the editor-time <c>BuildMenu()</c>, and re-running that also deletes Host and Join.
    /// This component closes that gap without touching either: <c>MagicMirrorMenu</c> is a
    /// teammate's file, so this ATTACHES rather than edits, which is the project's standing rule
    /// for someone else's code.
    ///
    /// Built from code on purpose. Serialized panel references are exactly what left this menu
    /// with a column of nulls in the scene YAML in the first place.
    /// </summary>
    [DefaultExecutionOrder(100)] // after MagicMirrorMenu.Awake has wired its buttons
    public class LobbySettingsPanel : MonoBehaviour
    {
        private const string LobbySceneName = "LobbyMirrorScene";

        // Installed from a runtime hook rather than placed in the scene. Saving LobbyMirrorScene
        // to add one component would rewrite a 1.4 MB YAML file that a teammate also edits, and
        // that file cannot be merged by hand. XRUIInputModuleGuard installs itself the same way.
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

        // Deliberately NOT DontDestroyOnLoad: the panel belongs to the lobby, and one builder per
        // visit is what makes re-entering the lobby rebuild it rather than leave a dead reference.
        private static void Spawn(string sceneName)
        {
            if (sceneName != LobbySceneName)
            {
                return;
            }

            if (FindAnyObjectByType<LobbySettingsPanel>() != null)
            {
                return;
            }

            new GameObject("LobbySettingsPanel").AddComponent<LobbySettingsPanel>();
        }

        // Lifted from MagicMirrorMenu's editor builder so the runtime panel and an editor-built
        // one are the same menu, not two that merely coexist.
        private static readonly Color Accent = new(0.72f, 0.45f, 1f, 1f);
        private static readonly Color BtnNormal = new(0.24f, 0.11f, 0.44f, 0.92f);
        private static readonly Color BtnHi = new(0.52f, 0.30f, 0.88f, 1f);
        private static readonly Color BtnPress = new(0.14f, 0.05f, 0.28f, 1f);
        private static readonly Color TextCol = new(0.95f, 0.92f, 1f, 1f);
        private static readonly Color TrackCol = new(0.10f, 0.05f, 0.20f, 0.95f);

        private TMP_FontAsset menuFont;

        private void Start()
        {
            MagicMirrorMenu menu = FindAnyObjectByType<MagicMirrorMenu>();
            if (menu == null)
            {
                return; // not the lobby — this component is harmless anywhere else
            }

            if (menu.settingsPanel != null)
            {
                return; // an editor-built panel already exists; do not stack a second one
            }

            var root = menu.GetComponent<RectTransform>();
            if (root == null)
            {
                Debug.LogWarning("[LobbySettings] MagicMirrorMenu is not on a RectTransform — " +
                                 "cannot build a panel to match it.");
                return;
            }

            // The menu's own labels carry the Gabriola SDF asset. Borrowing it from one of them is
            // the only way to match at runtime: the font lives outside Resources, so there is
            // nothing to Load, and hardcoding a fallback would make this panel the odd one out.
            TMP_Text sample = menu.GetComponentInChildren<TMP_Text>(true);
            menuFont = sample != null ? sample.font : null;

            // MainPanel has to be found by name: in the shipping scene the field is unassigned,
            // and without it OnSettings would show the settings panel ON TOP of the main menu
            // instead of instead of it. MirrorMenuRouter locates it the same way.
            if (menu.mainPanel == null)
            {
                Transform main = menu.transform.Find("MainPanel");
                if (main != null)
                {
                    menu.mainPanel = main.gameObject;
                }
                else
                {
                    Debug.LogWarning("[LobbySettings] No MainPanel found — the settings panel will " +
                                     "open over the main menu rather than replacing it.");
                }
            }

            menu.settingsPanel = BuildPanel(root, menu);
            menu.settingsPanel.SetActive(false);

            Debug.Log("[LobbySettings] Settings panel built and handed to the mirror menu.");
        }

        private GameObject BuildPanel(RectTransform root, MagicMirrorMenu menu)
        {
            // No backdrop: the mirror's glass is the background. raycastTarget goes off with it —
            // a fully transparent Image still swallows UI raycasts, so an invisible slab across the
            // whole menu would eat clicks meant for what is behind it.
            GameObject panel = Panel("SettingsPanel", root, Color.clear);
            panel.GetComponent<Image>().raycastTarget = false;
            Stretch((RectTransform)panel.transform);

            Label("SettingsTitle", panel.transform, "SETTINGS", 72f, Accent,
                  new Vector2(0f, 720f), new Vector2(940f, 140f), FontStyles.Bold);

            // Same three levels, same keys, same defaults as BootScene's panel — they are all
            // read back out of GameAudioSettings, so the two screens cannot drift apart.
            VolumeRow("Master", panel.transform, 470f,
                      GameAudioSettings.Master, GameAudioSettings.SetMaster);
            VolumeRow("Music", panel.transform, 250f,
                      GameAudioSettings.Music, GameAudioSettings.SetMusic);
            VolumeRow("Sound Effects", panel.transform, 30f,
                      GameAudioSettings.Sfx, GameAudioSettings.SetSfx);

            // Wired here rather than through menu.backButton: that field is read in Awake, which
            // has already run by the time this builds, so assigning it now would do nothing.
            Button back = MakeButton("Back", panel.transform, new Vector2(0f, -280f), new Vector2(500f, 130f));
            back.onClick.AddListener(() =>
            {
                panel.SetActive(false);
                if (menu.mainPanel != null)
                {
                    menu.mainPanel.SetActive(true);
                }
            });

            return panel;
        }

        /// <summary>A caption with a volume slider under it, as one settings row.</summary>
        private void VolumeRow(string caption, Transform parent, float y, float value,
                               UnityEngine.Events.UnityAction<float> onChanged)
        {
            Label(caption + "Label", parent, caption, 40f, TextCol,
                  new Vector2(0f, y), new Vector2(760f, 60f), FontStyles.Normal);

            var readout = Label(caption + "Readout", parent, Percent(value), 34f, Accent,
                                new Vector2(330f, y), new Vector2(200f, 60f), FontStyles.Bold);

            Slider slider = MakeSlider(caption, parent, new Vector2(0f, y - 80f),
                                       new Vector2(760f, 56f), value);

            slider.onValueChanged.AddListener(v =>
            {
                onChanged(v);
                readout.text = Percent(v);
            });
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        // ---- builders, matching MagicMirrorMenu's editor versions ----

        private static GameObject Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI Label(string name, Transform parent, string text, float size,
                                      Color color, Vector2 pos, Vector2 sizeD, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeD;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (menuFont != null)
            {
                tmp.font = menuFont;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        private Button MakeButton(string label, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = Color.white; // tinted by the Button colour block

            var btn = go.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = BtnNormal;
            cb.highlightedColor = BtnHi;
            cb.pressedColor = BtnPress;
            cb.selectedColor = BtnHi;
            cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            TextMeshProUGUI txt = Label("Text", go.transform, label, 46f, TextCol,
                                        Vector2.zero, size, FontStyles.Bold);
            Stretch(txt.rectTransform);
            return btn;
        }

        private static Slider MakeSlider(string name, Transform parent, Vector2 pos, Vector2 size, float value)
        {
            var go = new GameObject(name + "Slider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            GameObject bg = Panel("Background", go.transform, TrackCol);
            Stretch((RectTransform)bg.transform);

            // Fill and handle are driven by Slider itself, which rewrites their anchors from the
            // value — so unlike a plain Image these do not need a sprite to show a level.
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)fillArea.transform);
            GameObject fill = Panel("Fill", fillArea.transform, Accent);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)handleArea.transform);
            GameObject handle = Panel("Handle", handleArea.transform, BtnHi);
            var handleRt = (RectTransform)handle.transform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = new Vector2(0f, 1f);

            // A deliberately fat handle. At arm's length a ray pointer is far better at hitting a
            // big target than at dragging a small one — and Unity's Slider also jumps to a click
            // anywhere on the track, so the whole 760px row is a valid target, not just this.
            handleRt.sizeDelta = new Vector2(56f, 0f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value); // showing the saved value must not re-save it

            ColorBlock cb = slider.colors;
            cb.normalColor = BtnHi;
            cb.highlightedColor = Color.white;
            cb.pressedColor = BtnPress;
            slider.colors = cb;
            return slider;
        }
    }
}
