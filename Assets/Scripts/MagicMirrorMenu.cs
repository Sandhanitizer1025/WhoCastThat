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
    [Header("Buttons")]
    public Button playButton;
    public Button hostButton;
    public Button joinButton;
    public Button howToButton;
    public Button settingsButton;
    public Button quitButton;
    public Button backButton;

    [Header("Panels")]
    public GameObject howToPanel;

    void Awake()
    {
        Wire(playButton, OnPlay);
        Wire(hostButton, OnHost);
        Wire(joinButton, OnJoin);
        Wire(howToButton, OnHowToPlay);
        Wire(settingsButton, OnSettings);
        Wire(quitButton, OnQuit);
        Wire(backButton, OnBack);

        if (howToPanel != null) howToPanel.SetActive(false);
    }

    static void Wire(Button b, UnityEngine.Events.UnityAction action)
    {
        if (b == null) return;
        b.onClick.RemoveListener(action);
        b.onClick.AddListener(action);
    }

    // --- Button handlers ---------------------------------------------------

    public void OnPlay()     { Debug.Log("[MirrorMenu] Play pressed. TODO: start the game / load the play area."); }
    public void OnHost()     { Debug.Log("[MirrorMenu] Host pressed. TODO: create a networked session."); }
    public void OnJoin()     { Debug.Log("[MirrorMenu] Join pressed. TODO: browse / join a session."); }
    public void OnSettings() { Debug.Log("[MirrorMenu] Settings pressed. TODO: open settings panel."); }

    public void OnHowToPlay()
    {
        Debug.Log("[MirrorMenu] Loading TutorialScene...");
        UnityEngine.SceneManagement.SceneManager.LoadScene("TutorialScene");
    }

    public void OnBack() { if (howToPanel != null) howToPanel.SetActive(false); }

    public void OnQuit()
    {
        Debug.Log("[MirrorMenu] Quit pressed.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

#if UNITY_EDITOR
    // =======================================================================
    //  EDITOR BUILDER  -  constructs the mirror menu UI and wires references.
    // =======================================================================
    static readonly Color PanelBg   = new Color(0.05f, 0.02f, 0.12f, 0.88f);
    static readonly Color Accent     = new Color(0.72f, 0.45f, 1f, 1f);
    static readonly Color BtnNormal  = new Color(0.24f, 0.11f, 0.44f, 0.92f);
    static readonly Color BtnHi      = new Color(0.52f, 0.30f, 0.88f, 1f);
    static readonly Color BtnPress   = new Color(0.14f, 0.05f, 0.28f, 1f);
    static readonly Color TextCol    = new Color(0.95f, 0.92f, 1f, 1f);

    [MenuItem("WhoCastThat/Build Mirror Menu")]
    public static void BuildMenu()
    {
        var mirror = GameObject.Find("magik mirror");
        if (mirror == null) { Debug.LogError("[MirrorMenu] 'magik mirror' not found."); return; }
        var mr = mirror.GetComponent<MeshRenderer>();
        Vector3 faceCenter = mr != null ? mr.bounds.center : mirror.transform.position;

        // Player camera is on the -X side, so the readable face points toward -X.
        Vector3 viewDir = new Vector3(1f, 0f, 0f);              // camera looks +X
        Vector3 pos = faceCenter - viewDir * 0.05f;            // 5cm in front of the glass
        Quaternion rot = Quaternion.LookRotation(viewDir, Vector3.up);

        var old = GameObject.Find("MirrorMenu");
        if (old != null) Undo.DestroyObjectImmediate(old);

        // Root canvas -------------------------------------------------------
        var root = new GameObject("MirrorMenu", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Build Mirror Menu");
        root.transform.SetPositionAndRotation(pos, rot);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        root.AddComponent<GraphicRaycaster>();
        AddXRRaycaster(root);
        var rrt = root.GetComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(1000f, 1800f);
        root.transform.localScale = Vector3.one * 0.001f;      // 1000px -> 1.0m wide, 1800px -> 1.8m tall

        var menu = root.AddComponent<MagicMirrorMenu>();

        // Main panel --------------------------------------------------------
        var main = Panel("MainPanel", rrt, PanelBg);
        Stretch(main.GetComponent<RectTransform>());

        Label("Title", main.transform, "WHO CAST THAT?", 82, Accent, new Vector2(0, 720), new Vector2(940, 160), FontStyles.Bold);
        Label("Subtitle", main.transform, "A Magical Duel of Potions", 34, TextCol, new Vector2(0, 620), new Vector2(940, 70), FontStyles.Italic);

        string[] labels = { "Play", "Host", "Join", "How to Play", "Settings", "Quit" };
        float y = 430f, step = 170f;
        var buttons = new Button[6];
        for (int i = 0; i < labels.Length; i++)
            buttons[i] = MakeButton(labels[i], main.transform, new Vector2(0, y - i * step), new Vector2(700, 140));

        menu.playButton     = buttons[0];
        menu.hostButton     = buttons[1];
        menu.joinButton     = buttons[2];
        menu.howToButton    = buttons[3];
        menu.settingsButton = buttons[4];
        menu.quitButton     = buttons[5];

        // "How to Play" now teleports to the Tutorial scene, so no in-menu overlay is built here.

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[MirrorMenu] Menu built on the magic mirror at " + pos.ToString("F3") + ". Enter Play mode to use it.");
    }

    // --- builder helpers ---------------------------------------------------
    static void AddXRRaycaster(GameObject go)
    {
        var t = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (t != null) go.AddComponent(t);
        else Debug.LogWarning("[MirrorMenu] TrackedDeviceGraphicRaycaster not found; VR ray-clicking may need it added manually.");
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
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
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
#endif
}
