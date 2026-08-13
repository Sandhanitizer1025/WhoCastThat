using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Main-menu logic for the magic-mirror UI in the "Who Cast That?" VR game.
/// Button click listeners are wired at runtime (Awake) from the references
/// assigned by the editor builder below, so nothing depends on serialized
/// UnityEvents. The BuildMenu() editor method constructs the whole world-space
/// canvas on the "magik mirror" and hooks up these references.
/// </summary>
public class MagicMirrorMenu : MonoBehaviour
{
    [Header("Scenes")]
    public string gameSceneName = "InteractionTestScene";
    public string tutorialSceneName = "TutorialScene";
    public string bootSceneName = "BootScene";

    [Header("Buttons")]
    public Button playButton;
    // Host and Join are no longer built into the menu, but these fields must stay: MirrorMenuRouter
    // reaches the buttons through them, so deleting the fields would stop that file compiling.
    // They are simply left unassigned, and every use of them is null-guarded.
    public Button hostButton;
    public Button joinButton;
    public Button howToButton;
    public Button settingsButton;
    public Button quitButton;
    public Button backButton;

    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject settingsPanel;
    public GameObject howToPanel;

    [Header("Settings sliders")]
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;

    // ---- Main-menu row layout -------------------------------------------------------------
    // Public, and deliberately OUTSIDE the editor-only region below, because components that
    // attach to this menu at runtime add rows to the same column. LobbyCustomisePanel used to
    // carry its own copy of these numbers ("Quit sits at y=-420"), which stopped being true the
    // moment this layout changed and left its button floating in a gap.
    public const float RowFirstY = 470f;
    public const float RowStep = 150f;
    public static readonly Vector2 RowSize = new Vector2(700f, 120f);

    /// <summary>Rows this menu lays down itself. Anything attaching later starts at this index.</summary>
    public const int BuiltInRowCount = 6;

    /// <summary>Anchored position of main-menu row <paramref name="index"/>, counting from zero.</summary>
    public static Vector2 RowPosition(int index)
    {
        return new Vector2(0f, RowFirstY - index * RowStep);
    }

    void Awake()
    {
        Wire(playButton, OnPlay);
        Wire(hostButton, OnHost);
        Wire(joinButton, OnJoin);
        Wire(howToButton, OnHowToPlay);
        Wire(settingsButton, OnSettings);
        Wire(quitButton, OnQuit);
        Wire(backButton, OnBack);

        WireSlider(masterVolumeSlider, GameAudioSettings.Master, GameAudioSettings.SetMaster);
        WireSlider(musicVolumeSlider, GameAudioSettings.Music, GameAudioSettings.SetMusic);
        WireSlider(sfxVolumeSlider, GameAudioSettings.Sfx, GameAudioSettings.SetSfx);

        ShowSettings(false);
        if (howToPanel != null) howToPanel.SetActive(false);
    }

    static void Wire(Button b, UnityEngine.Events.UnityAction action)
    {
        if (b == null) return;
        b.onClick.RemoveListener(action);
        b.onClick.AddListener(action);
    }

    static void WireSlider(Slider s, float saved, UnityEngine.Events.UnityAction<float> action)
    {
        if (s == null) return;
        s.SetValueWithoutNotify(saved);   // showing the saved value must not re-save it
        s.onValueChanged.RemoveListener(action);
        s.onValueChanged.AddListener(action);
    }

    void ShowSettings(bool visible)
    {
        if (settingsPanel != null) settingsPanel.SetActive(visible);
        if (mainPanel != null) mainPanel.SetActive(!visible);
    }

    static void Load(string sceneName)
    {
        Debug.Log("[MirrorMenu] Loading " + sceneName + "...");
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    // --- Button handlers ---------------------------------------------------

    public void OnPlay()      { Load(gameSceneName); }
    public void OnHowToPlay() { Load(tutorialSceneName); }
    public void OnSettings()  { ShowSettings(true); }

    // Quit returns to the boot screen rather than closing the app: on a headset Application.Quit()
    // drops the player straight out to the system menu, which reads as a crash.
    public void OnQuit()      { Load(bootSceneName); }

    // Host and Join are no longer offered by this menu. The handlers remain because the public
    // button fields do; they are unreachable unless something assigns those buttons again.
    public void OnHost()      { Debug.Log("[MirrorMenu] Host pressed, but hosting is not offered by this menu."); }
    public void OnJoin()      { Debug.Log("[MirrorMenu] Join pressed, but joining is not offered by this menu."); }

    public void OnBack()
    {
        ShowSettings(false);
        if (howToPanel != null) howToPanel.SetActive(false);
    }

#if UNITY_EDITOR
    // =======================================================================
    //  EDITOR BUILDER  -  constructs the mirror menu UI and wires references.
    // =======================================================================
    // Font used for every label BuildMenu creates. Loaded once per build so the mirror matches
    // the tutorial; falls back to TMP's default if the asset is missing.
    const string MenuFontPath = "Assets/Fonts/Gabriola SDF.asset";
    static TMP_FontAsset s_MenuFont;

    // Canvas resolution in UI units. Kept at 1000x1800 deliberately: LobbySettingsPanel and
    // LobbyCustomisePanel both position their contents with literal coordinates in this space
    // (the Customise row sits at y=-590), so changing these numbers silently moves their layouts.
    // The menu is fitted to the mirror by SCALE, below, not by resizing the canvas.
    const float CanvasWidth  = 1000f;
    const float CanvasHeight = 1800f;

    // Fraction of the mirror's face the menu is allowed to cover. The mirror's bounds include its
    // carved frame, so filling them edge to edge puts the outermost buttons on the woodwork.
    const float FrameInset = 0.86f;

    static readonly Color Accent     = new Color(0.72f, 0.45f, 1f, 1f);
    static readonly Color BtnNormal  = new Color(0.24f, 0.11f, 0.44f, 0.92f);
    static readonly Color BtnHi      = new Color(0.52f, 0.30f, 0.88f, 1f);
    static readonly Color BtnPress   = new Color(0.14f, 0.05f, 0.28f, 1f);
    static readonly Color TextCol    = new Color(0.95f, 0.92f, 1f, 1f);

    [MenuItem("WhoCastThat/Build Mirror Menu")]
    public static void BuildMenu()
    {
        s_MenuFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MenuFontPath);
        if (s_MenuFont == null)
            Debug.LogWarning("[MirrorMenu] " + MenuFontPath + " not found; using TMP's default font.");

        var mirror = GameObject.Find("magik mirror");
        if (mirror == null) { Debug.LogError("[MirrorMenu] 'magik mirror' not found."); return; }
        var mr = mirror.GetComponent<MeshRenderer>();
        Vector3 faceCenter = mr != null ? mr.bounds.center : mirror.transform.position;

        // Player camera is on the -X side, so the readable face points toward -X.
        Vector3 viewDir = new Vector3(1f, 0f, 0f);              // camera looks +X
        Vector3 pos = faceCenter - viewDir * 0.05f;            // 5cm in front of the glass
        Quaternion rot = Quaternion.LookRotation(viewDir, Vector3.up);

        // Fit the menu to the glass by MEASURING it. The mirror faces along X, so its face is the
        // Y-Z plane: height is bounds.size.y and width is bounds.size.z. Hard-coding the scale is
        // what let the canvas grow to 1.17m x 2.11m against a 1.10m x 1.88m mirror -- every edge
        // of the menu hung off the frame into the room behind it.
        float faceWidth  = 1.0f;
        float faceHeight = 1.8f;
        if (mr != null)
        {
            faceWidth  = mr.bounds.size.z * FrameInset;
            faceHeight = mr.bounds.size.y * FrameInset;
        }

        // One uniform scale, taken from whichever axis runs out first, so the menu keeps its
        // proportions instead of stretching to the mirror's.
        float fitScale = Mathf.Min(faceWidth / CanvasWidth, faceHeight / CanvasHeight);

        var old = GameObject.Find("MirrorMenu");
        if (old != null) Undo.DestroyObjectImmediate(old);

        // Root canvas -------------------------------------------------------
        var root = new GameObject("MirrorMenu", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Build Mirror Menu");
        root.transform.SetPositionAndRotation(pos, rot);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        // XR-only raycaster. A plain GraphicRaycaster alongside the tracked-device one can fight
        // over UI input in VR (it wants a screen/event camera), so the menu uses only the XR raycaster.
        AddXRRaycaster(root);
        var rrt = root.GetComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        root.transform.localScale = Vector3.one * fitScale;

        var menu = root.AddComponent<MagicMirrorMenu>();

        // Main panel --------------------------------------------------------
        // No background image: the mirror's own glass is the backdrop. A filled panel over it read
        // as a purple slab hung in front of the mirror rather than as something IN it.
        var main = Container("MainPanel", rrt);
        Stretch(main.GetComponent<RectTransform>());

        Label("Title", main.transform, "WHO CAST THAT?", 82, Accent, new Vector2(0, 720), new Vector2(940, 160), FontStyles.Bold);
        Label("Subtitle", main.transform, "A Magical Duel of Potions", 34, TextCol, new Vector2(0, 620), new Vector2(940, 70), FontStyles.Italic);

        // Play, Host and Join all reach the real multiplayer flow through MirrorMenuRouter, which
        // replaces the stub handlers below with its own in Start(). Host and Join were previously
        // omitted and their fields left null, which made the router's OnHost/OnJoin -- and the
        // whole RoomCodePad behind Join -- unreachable: the code existed with no way to press it.
        string[] labels = { "Play", "Host", "Join", "How to Play", "Settings", "Quit" };
        var buttons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
            buttons[i] = MakeButton(labels[i], main.transform, RowPosition(i), RowSize);

        menu.mainPanel      = main;
        menu.playButton     = buttons[0];
        menu.hostButton     = buttons[1];
        menu.joinButton     = buttons[2];
        menu.howToButton    = buttons[3];
        menu.settingsButton = buttons[4];
        menu.quitButton     = buttons[5];

        // Settings panel ----------------------------------------------------
        // A sibling of MainPanel that swaps places with it, so the mirror never shows both.
        var settings = Container("SettingsPanel", rrt);
        Stretch(settings.GetComponent<RectTransform>());
        menu.settingsPanel = settings;

        Label("SettingsTitle", settings.transform, "SETTINGS", 72, Accent, new Vector2(0, 720), new Vector2(940, 140), FontStyles.Bold);

        menu.masterVolumeSlider = VolumeRow("Master", settings.transform, 470f, GameAudioSettings.MasterDefault);
        menu.musicVolumeSlider  = VolumeRow("Music",  settings.transform, 250f, GameAudioSettings.MusicDefault);
        menu.sfxVolumeSlider    = VolumeRow("Sound Effects", settings.transform, 30f, GameAudioSettings.SfxDefault);

        menu.backButton = MakeButton("Back", settings.transform, new Vector2(0, -280), new Vector2(500, 130));
        settings.SetActive(false);

        // "How to Play" now teleports to the Tutorial scene, so no in-menu overlay is built here.

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[MirrorMenu] Menu built on the magic mirror at " + pos.ToString("F3") + ". Enter Play mode to use it.");
    }

    // --- builder helpers ---------------------------------------------------
    static void AddXRRaycaster(GameObject go)
    {
        var t = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (t == null)
        {
            Debug.LogWarning("[MirrorMenu] TrackedDeviceGraphicRaycaster not found; VR ray-clicking may need it added manually.");
            return;
        }

        var comp = go.AddComponent(t);

        // Never let a 3D collider (the mirror mesh, a hand, etc.) occlude the world-space UI ray —
        // an occluded raycast silently blocks button presses, and headsets can differ from the
        // simulator here. Clearing the blocking mask makes the menu reliably hittable.
        var so = new UnityEditor.SerializedObject(comp);
        var mask = so.FindProperty("m_BlockingMask");
        if (mask != null) { mask.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
    }

    /// <summary>
    /// A layout-only panel: a RectTransform with no Image behind it. Used for the full-bleed
    /// panels, which exist to group and to be switched on and off, never to be seen. Deliberately
    /// has no Graphic at all rather than a transparent one — an Image with alpha 0 still swallows
    /// UI raycasts, so an invisible panel would quietly eat clicks meant for the mirror.
    /// </summary>
    static GameObject Container(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, Vector2 pos, Vector2 sizeD, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = sizeD;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (s_MenuFont != null) tmp.font = s_MenuFont;   // every menu label goes through here
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    static Button MakeButton(string label, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = Color.white;                       // tinted by the Button color block

        var btn = go.GetComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = BtnNormal; cb.highlightedColor = BtnHi; cb.pressedColor = BtnPress;
        cb.selectedColor = BtnHi; cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        var txt = Label("Text", go.transform, label, 46, TextCol, Vector2.zero, size, FontStyles.Bold);
        Stretch(txt.GetComponent<RectTransform>());
        return btn;
    }

    /// <summary>A caption with a volume slider under it, as one settings row.</summary>
    static Slider VolumeRow(string caption, Transform parent, float y, float value)
    {
        Label(caption + "Label", parent, caption, 40, TextCol, new Vector2(0, y), new Vector2(760, 60), FontStyles.Normal);
        return MakeSlider(caption, parent, new Vector2(0, y - 80f), new Vector2(760, 56), value);
    }

    static Slider MakeSlider(string name, Transform parent, Vector2 pos, Vector2 size, float value)
    {
        var go = new GameObject(name + "Slider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var bg = Panel("Background", go.transform, new Color(0.10f, 0.05f, 0.20f, 0.95f));
        Stretch(bg.GetComponent<RectTransform>());

        // Fill and handle are driven by Slider itself, which rewrites their anchors from the value.
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());
        var fill = Panel("Fill", fillArea.transform, Accent);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>());
        var handle = Panel("Handle", handleArea.transform, BtnHi);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero; handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.sizeDelta = new Vector2(56f, 0f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = value;

        var cb = slider.colors;
        cb.normalColor = BtnHi; cb.highlightedColor = Color.white; cb.pressedColor = BtnPress;
        slider.colors = cb;
        return slider;
    }
#endif
}
