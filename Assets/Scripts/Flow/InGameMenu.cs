using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// The in-game pause menu: volume, leave the match, quit.
    ///
    /// Summoned with the controller's MENU button, and the panel is then WORLD-LOCKED where it
    /// appeared rather than following the head. That is the convention every comfortable VR game
    /// uses, and the reason is not stylistic: a panel welded to your face cannot be looked away
    /// from, permanently occludes the table, and is a known source of discomfort. World-locking
    /// lets the player glance at the game, step back from the menu, and point at it with the same
    /// ray they already use for everything else.
    ///
    /// It deliberately does NOT pause anything. This is a networked match — the other players keep
    /// going, so a "pause" would only lie to the person who opened it.
    ///
    /// Built entirely from code and self-installing, so it needs no scene wiring and cannot end up
    /// half-materialised in a scene YAML the way the lobby's settings panel did.
    /// </summary>
    public class InGameMenu : MonoBehaviour
    {
        private const float Distance = 1.05f;   // metres in front of the player
        private const float EyeDrop = 0.12f;    // just below the sight line
        private const float VolumeStep = 0.1f;

        private static readonly Color PanelColour = new(0.06f, 0.05f, 0.11f, 0.94f);
        private static readonly Color ButtonColour = new(0.16f, 0.14f, 0.26f, 1f);
        private static readonly Color DangerColour = new(0.42f, 0.13f, 0.15f, 1f);
        private static readonly Color BarColour = new(0.45f, 0.86f, 0.62f, 1f);
        private static readonly Color BarBackColour = new(0.18f, 0.17f, 0.26f, 1f);

        private GameObject panel;
        private bool menuHeldLastFrame;
        private readonly List<InputDevice> devices = new();

        // Rebuilt whenever a level changes, so the readouts never drift from PlayerPrefs.
        private readonly Dictionary<string, Image> bars = new();
        private readonly Dictionary<string, TMP_Text> readouts = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // BootScene is a login screen, not gameplay; a pause menu there would be noise.
            if (SceneManager.GetActiveScene().name == "BootScene")
            {
                return;
            }

            var go = new GameObject("InGameMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<InGameMenu>();
        }

        private void Update()
        {
            if (MenuPressedThisFrame())
            {
                Toggle();
            }
        }

        // Read the menu button straight off the XR devices rather than through an action asset.
        // The action maps are the template's and differ between the hands/controllers rigs, so
        // binding by hand here keeps this working without touching that wiring. Escape is the
        // editor fallback, where there is no controller at all.
        private bool MenuPressedThisFrame()
        {
            bool held = false;

            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeldInHand, devices);
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].TryGetFeatureValue(CommonUsages.menuButton, out bool pressed) && pressed)
                {
                    held = true;
                    break;
                }
            }

            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                held = true;
                menuHeldLastFrame = false; // treat as a clean edge
            }

            bool edge = held && !menuHeldLastFrame;
            menuHeldLastFrame = held;
            return edge;
        }

        private void Toggle()
        {
            if (panel != null)
            {
                Destroy(panel);
                panel = null;
                bars.Clear();
                readouts.Clear();
                return;
            }

            Build();
        }

        private void Build()
        {
            Camera view = Camera.main;
            if (view == null)
            {
                return;
            }

            Vector3 forward = view.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;

            panel = new GameObject("InGameMenuPanel");
            panel.transform.position = view.transform.position + (forward * Distance) - (Vector3.up * EyeDrop);
            // Face the player once, here. After this the panel stays put — see the class summary.
            panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(620f, 560f);
            panel.transform.localScale = Vector3.one * 0.0011f; // ~0.68m wide at arm's length

            panel.AddComponent<CanvasScaler>();
            panel.AddComponent<GraphicRaycaster>();
            AddTrackedDeviceRaycaster(panel);

            var bg = NewImage(panel.transform, PanelColour);
            Stretch(bg.rectTransform);

            var column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(panel.transform, false);
            var col = (RectTransform)column.transform;
            Stretch(col);
            col.offsetMin = new Vector2(34f, 30f);
            col.offsetMax = new Vector2(-34f, -30f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            NewLabel(col, "PAUSED", 46f, FontStyles.Bold, TextAlignmentOptions.Center, 62f);
            NewLabel(col, "The match keeps running while this is open.", 22f,
                     FontStyles.Italic, TextAlignmentOptions.Center, 32f)
                .color = new Color(0.72f, 0.68f, 0.85f);

            VolumeRow(col, "Master", GameAudioSettings.MasterVolumeKey);
            VolumeRow(col, "Music", GameAudioSettings.MusicVolumeKey);
            VolumeRow(col, "Effects", GameAudioSettings.UISfxVolumeKey);

            NewButton(col, "Resume", ButtonColour, 62f, Toggle);
            NewButton(col, "Leave Match", ButtonColour, 62f, LeaveMatch);
            NewButton(col, "Quit Game", DangerColour, 62f, QuitGame);

            RefreshAll();
        }

        // The world-space raycaster lives in an XRI assembly this script does not reference, so it
        // is added by name — the same approach MagicMirrorMenu already uses for it.
        private static void AddTrackedDeviceRaycaster(GameObject target)
        {
            System.Type type = System.Type.GetType(
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");

            if (type != null && target.GetComponent(type) == null)
            {
                target.AddComponent(type);
            }
        }

        private void VolumeRow(RectTransform parent, string label, string key)
        {
            var row = new GameObject(label + "Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rt = (RectTransform)row.transform;
            rt.sizeDelta = new Vector2(0f, 56f);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            // Widths are budgeted to fit the column: 130 + 170 + 70 + 56 + 56 plus four 10px
            // gaps is 522 inside 552 of usable width. The first version totalled 608 and the
            // +/- buttons were clipped off the right edge of the panel.
            NewLabel(rt, label, 26f, FontStyles.Normal, TextAlignmentOptions.Left, 56f, 130f);

            // A filled bar rather than a drag handle: a ray pointer at arm's length is far more
            // reliable at hitting a big button than at dragging a small handle along a track.
            var track = NewImage(rt, BarBackColour);
            var trackRt = track.rectTransform;
            trackRt.sizeDelta = new Vector2(170f, 26f);
            var le = track.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 170f;
            le.preferredHeight = 26f;

            // The bar is driven by its ANCHOR, not Image.fillAmount. A sprite-less Image ignores
            // fillAmount completely — the first version set 1.00/0.28/0.79 correctly and drew
            // three identical full bars, because Filled needs a sprite to fill and these are
            // plain colour quads.
            var fill = NewImage(trackRt, BarColour);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(1f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            bars[key] = fill;

            readouts[key] = NewLabel(rt, "100%", 24f, FontStyles.Normal, TextAlignmentOptions.Center, 56f, 70f);

            NewButton(rt, "-", ButtonColour, 56f, () => Nudge(key, -VolumeStep), 56f);
            NewButton(rt, "+", ButtonColour, 56f, () => Nudge(key, VolumeStep), 56f);
        }

        private void Nudge(string key, float delta)
        {
            float current = Read(key);
            float next = Mathf.Clamp01(Mathf.Round((current + delta) * 100f) / 100f);
            Write(key, next);
            RefreshAll();
        }

        // Routed through GameAudioSettings so this menu and the mirror's settings panel cannot
        // disagree about keys, defaults, or what actually applies the change.
        private static float Read(string key)
        {
            if (key == GameAudioSettings.MasterVolumeKey) return GameAudioSettings.Master;
            if (key == GameAudioSettings.MusicVolumeKey) return GameAudioSettings.Music;
            return GameAudioSettings.Sfx;
        }

        private static void Write(string key, float value)
        {
            if (key == GameAudioSettings.MasterVolumeKey) GameAudioSettings.SetMaster(value);
            else if (key == GameAudioSettings.MusicVolumeKey) GameAudioSettings.SetMusic(value);
            else GameAudioSettings.SetSfx(value);
        }

        private void RefreshAll()
        {
            foreach (KeyValuePair<string, Image> pair in bars)
            {
                float v = Read(pair.Key);
                if (pair.Value != null)
                {
                    RectTransform rt = pair.Value.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                if (readouts.TryGetValue(pair.Key, out TMP_Text text) && text != null)
                {
                    text.text = Mathf.RoundToInt(v * 100f) + "%";
                }
            }
        }

        // Leaving is a scene load, not a manual disconnect: XRINetworkGameManager has no
        // DontDestroyOnLoad and its OnDestroy shuts the session down, which is the project's
        // established way out of a match.
        private void LeaveMatch()
        {
            Toggle();
            LobbyIntent.Clear();
            SessionIntentConnector.ResetHandled();
            SceneManager.LoadScene("LobbyMirrorScene");
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- small UI builders ----

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image NewImage(Transform parent, Color colour)
        {
            var go = new GameObject("Image", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = colour;
            return image;
        }

        private static TMP_Text NewLabel(RectTransform parent, string text, float size, FontStyles style,
                                         TextAlignmentOptions align, float height, float width = 0f)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = align;
            label.color = Color.white;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, height);
            if (width > 0f)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.preferredHeight = height;
            }
            return label;
        }

        private static void NewButton(RectTransform parent, string text, Color colour, float height,
                                      UnityEngine.Events.UnityAction onClick, float width = 0f)
        {
            var go = new GameObject(text + "Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = colour;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var colors = button.colors;
            colors.highlightedColor = colour * 1.5f;
            colors.pressedColor = colour * 0.7f;
            button.colors = colors;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, height);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            if (width > 0f)
            {
                le.preferredWidth = width;
            }

            var label = NewLabel(rt, text, 28f, FontStyles.Bold, TextAlignmentOptions.Center, height);
            Stretch(label.rectTransform);
        }
    }
}
