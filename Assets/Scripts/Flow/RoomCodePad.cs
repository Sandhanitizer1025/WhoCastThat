using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// An on-mirror keypad for typing a room code in VR.
    ///
    /// Built entirely from code and parented to the existing MirrorMenu canvas, so there is
    /// nothing to wire in the scene and nothing that can break when the mirror UI is rebuilt
    /// by MagicMirrorMenu's editor builder. A virtual keyboard was rejected: a 36-key pad with
    /// large targets is far more reliable to hit with a poke or a ray than a full QWERTY.
    /// </summary>
    public class RoomCodePad : MonoBehaviour
    {
        public event Action<string> Submitted;
        public event Action Cancelled;

        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        static readonly Color PanelBg  = new Color(0.05f, 0.02f, 0.12f, 0.95f);
        static readonly Color Accent   = new Color(0.72f, 0.45f, 1f, 1f);
        static readonly Color BtnNormal = new Color(0.24f, 0.11f, 0.44f, 0.92f);
        static readonly Color BtnHi     = new Color(0.52f, 0.30f, 0.88f, 1f);
        static readonly Color BtnPress  = new Color(0.14f, 0.05f, 0.28f, 1f);
        static readonly Color TextCol   = new Color(0.95f, 0.92f, 1f, 1f);
        static readonly Color GoNormal  = new Color(0.15f, 0.42f, 0.22f, 0.95f);

        int m_CodeLength = 6;
        string m_Code = "";
        TextMeshProUGUI m_Display;
        Button m_SubmitButton;

        /// <summary>
        /// Builds the pad as a child of the mirror canvas. Starts hidden.
        /// </summary>
        public static RoomCodePad Build(RectTransform parent, int codeLength)
        {
            GameObject root = new GameObject("RoomCodePad", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = PanelBg;

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            RoomCodePad pad = root.AddComponent<RoomCodePad>();
            pad.m_CodeLength = codeLength;
            pad.BuildContents(rt);
            root.SetActive(false);
            return pad;
        }

        void BuildContents(RectTransform rt)
        {
            Label("Title", rt, "ENTER ROOM CODE", 70, Accent, new Vector2(0, 740), new Vector2(940, 130), FontStyles.Bold);

            m_Display = Label("Code", rt, Placeholder(), 90, TextCol, new Vector2(0, 590), new Vector2(940, 140), FontStyles.Bold);

            // 36 keys in a 6x6 grid.
            const int cols = 6;
            const float cellW = 150f, cellH = 118f;
            float startX = -(cols - 1) * cellW * 0.5f;
            float startY = 420f;

            for (int i = 0; i < Alphabet.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                string ch = Alphabet[i].ToString();
                Vector2 pos = new Vector2(startX + col * cellW, startY - row * cellH);
                Button b = MakeButton(ch, rt, pos, new Vector2(cellW - 12f, cellH - 12f), BtnNormal, 52);
                string captured = ch;
                b.onClick.AddListener(delegate { Append(captured); });
            }

            float actionY = startY - 6 * cellH - 20f;
            Button del = MakeButton("DELETE", rt, new Vector2(-300f, actionY), new Vector2(280f, 120f), BtnNormal, 40);
            del.onClick.AddListener(Backspace);

            m_SubmitButton = MakeButton("JOIN", rt, new Vector2(0f, actionY), new Vector2(280f, 120f), GoNormal, 44);
            m_SubmitButton.onClick.AddListener(Submit);

            Button back = MakeButton("BACK", rt, new Vector2(300f, actionY), new Vector2(280f, 120f), BtnNormal, 40);
            back.onClick.AddListener(Cancel);

            RefreshDisplay();
        }

        string Placeholder()
        {
            return new string('_', m_CodeLength);
        }

        public void Show()
        {
            m_Code = "";
            RefreshDisplay();
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void Append(string ch)
        {
            if (m_Code.Length >= m_CodeLength) return;
            m_Code += ch;
            RefreshDisplay();
        }

        void Backspace()
        {
            if (m_Code.Length == 0) return;
            m_Code = m_Code.Substring(0, m_Code.Length - 1);
            RefreshDisplay();
        }

        void Submit()
        {
            if (m_Code.Length != m_CodeLength) return;
            Action<string> handler = Submitted;
            if (handler != null) handler(m_Code);
        }

        void Cancel()
        {
            Hide();
            Action handler = Cancelled;
            if (handler != null) handler();
        }

        void RefreshDisplay()
        {
            if (m_Display != null)
            {
                m_Display.text = m_Code + new string('_', Mathf.Max(0, m_CodeLength - m_Code.Length));
            }
            // Only lets you press JOIN once the code is actually complete, so a half-typed code
            // cannot fire a guaranteed-to-fail JoinLobbyByCode and burn a lobby rate-limit slot.
            if (m_SubmitButton != null)
            {
                m_SubmitButton.interactable = m_Code.Length == m_CodeLength;
            }
        }

        // --- builder helpers (kept local; MagicMirrorMenu's are private and editor-only) -----

        static TextMeshProUGUI Label(string name, Transform parent, string text, float size,
                                     Color color, Vector2 pos, Vector2 sizeD, FontStyles style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeD;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        static Button MakeButton(string label, Transform parent, Vector2 pos, Vector2 size,
                                 Color normal, float fontSize)
        {
            GameObject go = new GameObject(label + "Key", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            go.GetComponent<Image>().color = Color.white;   // tinted by the colour block

            Button btn = go.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = normal;
            cb.highlightedColor = BtnHi;
            cb.pressedColor = BtnPress;
            cb.selectedColor = BtnHi;
            cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            TextMeshProUGUI txt = Label("Text", go.transform, label, fontSize, TextCol, Vector2.zero, size, FontStyles.Bold);
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
