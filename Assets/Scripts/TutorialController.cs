using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Controls the tutorial scene for "Who Cast That?". Shows Controls + Rules
/// info-boards that follow the player's camera (via CameraFollowUI) and a Back
/// button that returns to the main menu scene. The editor builder constructs
/// the world-space boards as children of a follow root.
/// </summary>
public class TutorialController : MonoBehaviour
{
    [Header("Navigation")]
    public Button backButton;
    public string mainSceneName = "zelda";

    void Awake()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnBack);
            backButton.onClick.AddListener(OnBack);
        }
    }

    public void OnBack()
    {
        Debug.Log("[Tutorial] Returning to '" + mainSceneName + "'.");
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainSceneName);
    }

#if UNITY_EDITOR
    // =======================================================================
    //  EDITOR BUILDER  -  builds the follow-camera tutorial boards.
    // =======================================================================
    static readonly Color PanelBg   = new Color(0.05f, 0.02f, 0.12f, 0.92f);
    static readonly Color Accent    = new Color(0.72f, 0.45f, 1f, 1f);
    static readonly Color BtnNormal = new Color(0.24f, 0.11f, 0.44f, 0.95f);
    static readonly Color BtnHi     = new Color(0.52f, 0.30f, 0.88f, 1f);
    static readonly Color BtnPress  = new Color(0.14f, 0.05f, 0.28f, 1f);
    static readonly Color TextCol   = new Color(0.96f, 0.93f, 1f, 1f);

    const float Scale = 0.00075f; // 1200px -> 0.9m wide, 1600px -> 1.2m tall

    const string ControlsText =
        "<b>Move</b>:  Push the thumbstick\n\n" +
        "<b>Turn</b>:  Flick the thumbstick left / right\n\n" +
        "<b>Point & Select</b>:  Aim the ray, pull the Trigger\n\n" +
        "<b>Grab a potion</b>:  Squeeze the Grip\n\n" +
        "<b>Stir the cauldron</b>:  Reach in and swirl your hand\n\n" +
        "<b>Draw a potion</b>:  Stir the cauldron\n\n" +
        "<b>Cast a spell</b>:  Drop a potion in the center play zone";

    const string RulesText =
        "Draw a <color=#b06cff>CURSE</color> and you explode — unless you cast a <b>Counterspell</b>. Last wizard standing wins!\n\n" +
        "<b>Hex</b>:  Next player takes 2 turns.\n" +
        "<b>Tribute</b>:  A player gives you a card.\n" +
        "<b>Dispel</b>:  Cancel another action.\n" +
        "<b>Foresight</b>:  Peek the top 3 potions.\n" +
        "<b>Warp</b>:  Shuffle the draw pile.\n" +
        "<b>Phase</b>:  End turn without drawing.\n" +
        "<b>Reflection</b>:  Copy the last card.\n" +
        "<b>Counterspell</b>:  Dodge a Curse.\n" +
        "<b>Curse</b>:  You explode!";

    [MenuItem("WhoCastThat/Build Tutorial UI")]
    public static void BuildTutorial()
    {
        var old = GameObject.Find("TutorialUI");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject("TutorialUI");
        Undo.RegisterCreatedObjectUndo(root, "Build Tutorial UI");
        var ctrl = root.AddComponent<TutorialController>();
        root.AddComponent<CameraFollowUI>();

        // Preview pose so it looks right in the editor before Play mode.
        // At runtime CameraFollowUI positions it in front of the player.
        var cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : new Vector3(0.01f, 0.458f, -12.02f);
        Vector3 fwd = new Vector3(1f, 0f, 0f); // preview facing +X (toward the mirror)
        root.transform.position = camPos + fwd * 1.8f + Vector3.up * -0.1f;
        root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        // Two info boards side-by-side + Back button below, as local offsets.
        var controls = MakeBoard("ControlsBoard", root.transform, new Vector3(-0.72f, 0.12f, 0f));
        Header(controls, "Controls", 84);
        Body(controls, ControlsText, 44);

        var rules = MakeBoard("RulesBoard", root.transform, new Vector3(0.72f, 0.12f, 0f));
        Header(rules, "Rules", 84);
        Body(rules, RulesText, 40);

        var backBoard = MakeBoard("BackBoard", root.transform, new Vector3(0f, -0.78f, 0f), 1000f, 300f);
        backBoard.color = new Color(0, 0, 0, 0); // no panel, just the button
        ctrl.backButton = MakeButton("◄  Back to Menu", backBoard.transform, Vector2.zero, new Vector2(900, 220), 60);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Tutorial] Follow-camera tutorial UI built.");
    }

    // --- builder helpers ---------------------------------------------------
    static Image MakeBoard(string name, Transform parent, Vector3 localPos, float w = 1200f, float h = 1600f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        go.AddComponent<GraphicRaycaster>();
        var t = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (t != null) go.AddComponent(t);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        go.transform.localScale = Vector3.one * Scale;
        go.GetComponent<Image>().color = PanelBg;
        return go.GetComponent<Image>();
    }

    static void Header(Image board, string text, float size)
    {
        var rt = board.rectTransform;
        Label("Header", board.transform, text, size, Accent,
            new Vector2(0, rt.sizeDelta.y * 0.5f - 120f), new Vector2(rt.sizeDelta.x - 70f, 170f),
            FontStyles.Bold, TextAlignmentOptions.Center);
    }

    static void Body(Image board, string text, float size)
    {
        var rt = board.rectTransform;
        Label("Body", board.transform, text, size, TextCol,
            new Vector2(0, -70f), new Vector2(rt.sizeDelta.x - 90f, rt.sizeDelta.y - 320f),
            FontStyles.Normal, TextAlignmentOptions.TopLeft);
    }

    static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
        Vector2 pos, Vector2 sizeD, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = sizeD;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
        tmp.alignment = align; tmp.enableWordWrapping = true;
        return tmp;
    }

    static Button MakeButton(string label, Transform parent, Vector2 pos, Vector2 size, float fontSize)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.GetComponent<Image>().color = Color.white;
        var btn = go.GetComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = BtnNormal; cb.highlightedColor = BtnHi; cb.pressedColor = BtnPress;
        cb.selectedColor = BtnHi; cb.fadeDuration = 0.08f;
        btn.colors = cb;
        var txt = Label("Text", go.transform, label, fontSize, TextCol, Vector2.zero, size, FontStyles.Bold, TextAlignmentOptions.Center);
        var trt = txt.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        return btn;
    }
#endif
}
