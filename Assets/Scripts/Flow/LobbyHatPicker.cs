using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoCastThat.Visuals;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Lets the player choose their hat, on a panel beside the mirror menu.
    ///
    /// A separate world-space panel rather than a row inside MagicMirrorMenu's main panel. That
    /// menu lays its buttons out at fixed positions, so adding to it means either re-running its
    /// editor builder — which deletes Host and Join — or guessing at a gap in a layout owned by
    /// someone else. A sibling panel cannot collide with either.
    ///
    /// Choosing writes XRINetworkGameManager.LocalPlayerColor, the colour the template already
    /// replicates. <see cref="PlayerHat"/> resolves that same colour back to a hat on every
    /// client, so nothing new goes on the wire and the choice survives into the match.
    ///
    /// Swatches come from PlayerHatLibrary, the same asset the hats themselves use, so the picker
    /// cannot offer a colour that maps to a different hat than the one shown.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class LobbyHatPicker : MonoBehaviour
    {
        private const string LobbySceneName = "LobbyMirrorScene";

        private static readonly Color PanelBg = new(0.05f, 0.02f, 0.12f, 0.88f);
        private static readonly Color Accent = new(0.72f, 0.45f, 1f, 1f);
        private static readonly Color TextCol = new(0.95f, 0.92f, 1f, 1f);

        private const float PanelWidth = 520f;
        private const float RowHeight = 132f;
        private const float Gap = 60f;

        private PlayerHatLibrary library;
        private TMP_FontAsset menuFont;
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
            if (sceneName != LobbySceneName || FindAnyObjectByType<LobbyHatPicker>() != null)
            {
                return;
            }

            new GameObject("LobbyHatPicker").AddComponent<LobbyHatPicker>();
        }

        private void Start()
        {
            library = Resources.Load<PlayerHatLibrary>("PlayerHatLibrary");
            if (library == null || library.Hats.Length == 0)
            {
                Debug.LogWarning("[LobbyFlow] No PlayerHatLibrary — no hat picker in the lobby.");
                return;
            }

            MagicMirrorMenu menu = FindAnyObjectByType<MagicMirrorMenu>();
            if (menu == null)
            {
                return;
            }

            var menuRect = menu.GetComponent<RectTransform>();
            if (menuRect == null)
            {
                return;
            }

            TMP_Text sample = menu.GetComponentInChildren<TMP_Text>(true);
            menuFont = sample != null ? sample.font : null;

            Build(menuRect);

            XRINetworkGameManager.LocalPlayerColor.Subscribe(OnColourChanged);
            OnColourChanged(XRINetworkGameManager.LocalPlayerColor.Value);
        }

        private void OnDestroy()
        {
            XRINetworkGameManager.LocalPlayerColor.Unsubscribe(OnColourChanged);
        }

        private void Build(RectTransform menuRect)
        {
            var go = new GameObject("HatPickerPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(menuRect, false);
            go.GetComponent<Image>().color = PanelBg;

            var rt = (RectTransform)go.transform;
            int count = library.Hats.Length;
            float height = 180f + count * (RowHeight + 16f);
            rt.sizeDelta = new Vector2(PanelWidth, height);

            // Parented to the menu so it inherits the mirror's rotation and scale; offset in
            // canvas units so it sits clear of the menu's own 1000px width whatever the mirror
            // is angled at.
            rt.anchoredPosition = new Vector2(menuRect.sizeDelta.x * 0.5f + Gap + PanelWidth * 0.5f, 0f);

            Label(rt, "HAT", 56f, Accent, new Vector2(0f, height * 0.5f - 80f),
                  new Vector2(PanelWidth - 40f, 90f), FontStyles.Bold);

            float top = height * 0.5f - 180f;
            for (int i = 0; i < count; i++)
            {
                PlayerHatLibrary.Hat hat = library.Hats[i];
                if (hat.prefab == null)
                {
                    continue;
                }

                Color colour = hat.colour;
                colours.Add(colour);

                string label = string.IsNullOrEmpty(hat.label)
                    ? hat.prefab.name.Replace("hat_", "")
                    : hat.label;

                MakeSwatch(rt, label, colour, new Vector2(0f, top - i * (RowHeight + 16f)));
            }
        }

        private void MakeSwatch(RectTransform parent, string label, Color colour, Vector2 pos)
        {
            var go = new GameObject(label + "Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(PanelWidth - 60f, RowHeight);

            var image = go.GetComponent<Image>();
            image.color = colour;

            // The tick that shows the current choice. Added now and switched off, so selecting is
            // a visibility flip rather than building UI mid-interaction.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(6f, 6f);
            outline.enabled = false;
            marks.Add(outline);

            var button = go.GetComponent<Button>();
            ColorBlock cb = button.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            cb.fadeDuration = 0.08f;
            button.colors = cb;
            button.targetGraphic = image;

            Color captured = colour;
            button.onClick.AddListener(() => Choose(captured));

            // Dark text on light swatches, light on dark, so every label stays readable without
            // hand-picking a colour per hat.
            float luminance = colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
            Color textColour = luminance > 0.6f ? new Color(0.08f, 0.05f, 0.12f) : TextCol;

            TextMeshProUGUI text = Label(rt, label, 40f, textColour, Vector2.zero,
                                         new Vector2(PanelWidth - 60f, RowHeight), FontStyles.Bold);
            text.raycastTarget = false;
        }

        private static void Choose(Color colour)
        {
            // Writing the bindable is the whole action. XRINetworkPlayer pushes it onto its
            // replicated colour, and PlayerHat turns it back into a hat on every client.
            XRINetworkGameManager.LocalPlayerColor.Value = colour;
            Debug.Log("[LobbyFlow] Hat colour set to " + colour + ".");
        }

        private void OnColourChanged(Color colour)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < colours.Count; i++)
            {
                float dr = colours[i].r - colour.r;
                float dg = colours[i].g - colour.g;
                float db = colours[i].b - colour.b;
                float distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            // Nearest rather than exact: the player may arrive carrying a colour picked from the
            // template's own swatches, which are not the same list as ours.
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i] != null)
                {
                    marks[i].enabled = i == best;
                }
            }
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
            if (menuFont != null)
            {
                tmp.font = menuFont;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = colour;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            return tmp;
        }
    }
}
