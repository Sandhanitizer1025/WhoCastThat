using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils; // XROrigin
using TMPro;
using WhoCastThat.Interactions; // NetworkedSpellGame, NetworkedPotion, SeatLock, PlayerSeater

namespace WhoCastThat.Tutorial
{
    /// <summary>
    /// Single-player guided tutorial for "Who Cast That?", layered on top of the real
    /// (networked) gameplay in a copy of InteractionTestScene running as a solo host.
    ///
    /// It drives a 7-step flow with a screen-space HUD:
    ///   1. Show the VR controls.
    ///   2. Point the player to their seat with a floating marker (walk there).
    ///   3. Seat + lock them (look-only) via the existing SeatLock.
    ///   4. Show their dealt hand (5 potions incl. a Counterspell).
    ///   5. Guide a cast: grab a potion, drop it in the center ring.
    ///   6. Guide a draw, force a Curse, and teach the Counterspell save.
    ///   7. Explain the win condition.
    ///
    /// Nothing here modifies the shipped gameplay scripts; it only reads their public
    /// state / events and toggles the seating helpers so the walk-to-seat step works.
    /// Every step also has a "Next" button so testing can advance manually.
    /// </summary>
    public class TutorialDriver : MonoBehaviour
    {
        [Tooltip("Seat the tutorial guides the player to. Defaults to the object named 'Seat_0'.")]
        [SerializeField] private Transform targetSeat;

        [Tooltip("How close the headset must get to the seat (metres, horizontal) to count as 'arrived'.")]
        [SerializeField] private float seatArriveRadius = 0.9f;

        [Header("Guide Book")]
        [Tooltip("Background image for the Guide Book — import the open-book PNG as a Sprite (2D and UI) and assign it here.")]
        public Sprite bookBackground;

        private const int TotalSteps = 7;

        // Guide Book pages: left = heading/subtitle, right = details. Flipped with the side arrows.
        private static readonly string[] PageTitle =
            { "Controls", "How to Play", "How to Win", "The Potions", "About" };
        private static readonly string[] PageSubtitle =
            { "Your VR toolkit", "Taking a turn", "Be the last mage", "What's in the cauldron", "Who Cast That?" };
        private static readonly string[] PageBody =
        {
            "<b>Left Stick</b> — Move / walk\n\n<b>Right Stick</b> — Turn to look\n\n<b>Grip</b> — Grab a potion\n\n<b>Trigger</b> — Select · draw from the cauldron\n\n<b>Release</b> — Drop a held potion\n\n<b>E</b> — Open / close this book",
            "On your turn:\n\n<b>1.</b> Cast potions — grab one and drop it in the glowing ring in the centre.\n\n<b>2.</b> End your turn by drawing — dip a hand in the cauldron and pull a potion.\n\nSome potions end your turn early or change who plays next.",
            "Be the <b>last wizard standing</b>.\n\nDraw a <b>Curse</b> with no <b>Counterspell</b> and you explode — you're out of the game.\n\nOutlast every rival and the victory is yours!",
            "<b>Hex</b> ×5 — next player takes 2 turns\n<b>Tribute</b> ×4 — take a card from someone\n<b>Dispel</b> ×4 — cancel an action\n<b>Foresight</b> ×5 — peek the top 3\n<b>Warp</b> ×4 — shuffle the deck\n<b>Phase</b> ×4 — end turn, no draw\n<b>Reflection</b> ×4 — copy the last card\n<b>Counterspell</b> ×6 — dodge a Curse\n<b>Curse</b> ×4 — you explode!",
            "<b>Ages 5+</b>\n\n<b>2–4 players</b>\n\n<b>~15 minutes</b>\n\nA magical, last-mage-standing potion duel.",
        };

        // Seating helpers we suppress during the walk, then hand back.
        private SeatLock seatLock;
        private PlayerSeater playerSeater;

        // Screen-space UI
        private TMP_Text bannerText;
        private GameObject controlsPanel; // the Guide Book
        private GameObject finishPanel;
        private Button nextButton;
        private TMP_Text nextLabel;

        // Guide Book flip-book widgets
        private TMP_Text bookLeftText, bookRightText, bookPageLabel;
        private Button bookPrevBtn, bookNextBtn;
        private int bookPage;

        // Floating world marker over the seat.
        private GameObject seatMarker;

        private bool advanceRequested;
        private bool guideEverOpened;
        private Camera headCam;
        private InputAction guideToggle; // VR controller button (+ E) that opens/closes the book

        private void Awake()
        {
            if (targetSeat == null)
            {
                GameObject s = GameObject.Find("Seat_0");
                if (s != null) targetSeat = s.transform;
            }

            // Suppress auto-seating so the player can physically walk to the chair first.
            seatLock = FindAnyObjectByType<SeatLock>();
            playerSeater = FindAnyObjectByType<PlayerSeater>();
            if (seatLock != null) seatLock.enabled = false;
            if (playerSeater != null) playerSeater.enabled = false;

            BuildUI();
            BuildSeatMarker();
        }

        private void OnEnable()
        {
            NetworkedSpellGame.AnnouncementChanged += OnAnnouncement;

            // Open/close the Guide Book with a VR controller face button (A / X on either hand),
            // the controller menu button, or E on the keyboard for desktop testing.
            guideToggle = new InputAction("GuideToggle", InputActionType.Button);
            guideToggle.AddBinding("<XRController>{LeftHand}/primaryButton");
            guideToggle.AddBinding("<XRController>{RightHand}/primaryButton");
            guideToggle.AddBinding("<XRController>{LeftHand}/menuButton");
            guideToggle.AddBinding("<XRController>{RightHand}/menuButton");
            guideToggle.AddBinding("<Keyboard>/e");
            guideToggle.Enable();
        }

        private void OnDisable()
        {
            NetworkedSpellGame.AnnouncementChanged -= OnAnnouncement;
            if (guideToggle != null) { guideToggle.Disable(); guideToggle.Dispose(); guideToggle = null; }
        }

        private void Start()
        {
            StartCoroutine(RunTutorial());
            StartCoroutine(DiagnosticLoop());
        }

        // Temporary: prints the networked game state to the Console every ~1.5s so the
        // "no potions / cast bounces back" problem can be diagnosed from the logs.
        private System.Collections.IEnumerator DiagnosticLoop()
        {
            var wait = new WaitForSeconds(1.5f);
            while (true)
            {
                var g = NetworkedSpellGame.Instance;
                var nm = Unity.Netcode.NetworkManager.Singleton;
                Debug.Log("[TUT-DIAG] instance=" + (g != null)
                    + " connected=" + (nm != null && nm.IsConnectedClient)
                    + " active=" + (g != null && g.GameActive)
                    + " myTurn=" + (g != null && g.IsLocalPlayersTurn)
                    + " turnClient=" + (g != null ? g.CurrentTurnClientId.ToString() : "-")
                    + " localId=" + (nm != null ? nm.LocalClientId.ToString() : "-")
                    + " seatIdx=" + (g != null ? g.LocalSeatIndex.ToString() : "-")
                    + " potions=" + CountPotions()
                    + " cursed=" + (g != null && g.IsLocalPlayerCursed)
                    + " announce=\"" + (g != null ? g.CurrentAnnouncement : "") + "\"");
                yield return wait;
            }
        }

        private void OnAnnouncement(string _) { /* reserved for future step hooks */ }

        // Guide Book can be toggled any time with the controller button (or E).
        private void Update()
        {
            if (guideToggle != null && guideToggle.WasPressedThisFrame() && controlsPanel != null)
            {
                bool show = !controlsPanel.activeSelf;
                ShowControls(show);
                if (show) guideEverOpened = true;
            }
        }

        // ===================== Step flow =====================

        private IEnumerator RunTutorial()
        {
            // --- Step 1: guide book (press E) ---
            SetBanner(1, "Welcome, wizard! Press the <b>A / X</b> button on your controller to open your Guide Book — flip through the controls, rules, cards and more.");
            SetNext("Continue →", true);
            yield return WaitUntil(() => guideEverOpened);
            SetBanner(1, "Use the <b>← →</b> arrows to flip pages. Reopen the book anytime with the <b>A / X</b> button. Press <b>→</b> when ready.");
            yield return WaitForAdvance();
            ShowControls(false);

            // --- Step 2: walk to the seat ---
            SetBanner(2, "Follow the glowing marker and walk to your seat.");
            if (seatMarker != null) seatMarker.SetActive(true);
            SetNext("Skip →", true);
            yield return WaitUntil(HeadNearSeat);

            // --- Step 3: sit + lock ---
            SetBanner(3, "Take your seat. You're seated now — you can look around, but not walk off.");
            if (seatMarker != null) seatMarker.SetActive(false);
            SeatPlayerAtChair();                            // explicit, guaranteed placement in the chair
            LockLocomotion();                               // hard-disable walk/teleport, independent of network state
            if (seatLock != null) seatLock.enabled = true;  // SeatLock also holds the leash while the game is active
            SetNext("Continue →", true);
            yield return WaitForAdvance();

            // --- Step 4: your hand ---
            SetBanner(4, "This is your hand in the rack in front of you: 5 potions, including one Counterspell - your lifesaver.");
            SetNext("Continue →", true);
            yield return WaitForAdvance();

            // --- Step 5: cast a potion ---
            SetBanner(5, "Your turn! Grab a potion (Grip) and drop it in the glowing ring in the center to cast it.");
            SetNext("Skip →", true);
            int startPotions = CountPotions();
            yield return WaitUntil(() => CountPotions() < startPotions);
            SetBanner(5, "Nicely cast! That spell resolved on the table.");
            SetNext("Continue →", true);
            yield return WaitForAdvance();

            // --- Step 6: draw -> forced curse -> counterspell ---
            SetBanner(6, "End your turn by drawing — put a hand into the cauldron. (This draw is rigged: you'll pull a CURSE!)");
            SetNext("Skip →", true);
            // Fire the curse the instant a hand goes into the pot. Being cursed also blocks the
            // normal brew, so the player never also draws a random potion in the same motion.
            yield return WaitUntil(() => StirZone.LocalHandCanDraw || IsCursed());
            if (!IsCursed()) ForceCurse();
            SetBanner(6, "<color=#FF6B6B>You drew a CURSE!</color> Quick — drop your <b>Counterspell</b> in the ring to survive!");
            SetNext("Skip →", true);
            yield return WaitUntil(() => !IsCursed());
            SetBanner(6, "Saved! A Counterspell dodges a Curse and shuffles it back into the cauldron. Without one, you'd be out.");
            SetNext("Continue →", true);
            yield return WaitForAdvance();

            // --- Step 7: win condition ---
            SetBanner(7, "That's it! Keep casting and surviving. The last wizard who hasn't exploded WINS.");
            SetNext("Finish →", true);
            yield return WaitForAdvance();

            // --- Done ---
            SetBanner(7, "Tutorial complete — you're ready to duel!");
            ShowFinish(true);
        }

        // ===================== Conditions / actions =====================

        private bool HeadNearSeat()
        {
            if (targetSeat == null) return true;
            if (headCam == null) headCam = Camera.main;
            if (headCam == null) return false;
            Vector3 a = headCam.transform.position;
            Vector3 b = targetSeat.position;
            float dx = a.x - b.x, dz = a.z - b.z;
            return (dx * dx + dz * dz) <= seatArriveRadius * seatArriveRadius;
        }

        // In a solo tutorial every spawned potion is the local player's, so the scene count
        // is a good enough "did they cast one" signal.
        private int CountPotions() => FindObjectsByType<NetworkedPotion>(FindObjectsSortMode.None).Length;

        private bool IsCursed() =>
            NetworkedSpellGame.Instance != null && NetworkedSpellGame.Instance.IsLocalPlayerCursed;

        private void ForceCurse()
        {
            if (NetworkedSpellGame.Instance != null)
            {
                NetworkedSpellGame.Instance.DebugDrawCurse();
            }
        }

        // Place the rig directly in the seat, independent of network state. The
        // CharacterController fights direct transform writes, so switch it off for the move.
        private void SeatPlayerAtChair()
        {
            GameObject seat = GameObject.Find("Seat_0");
            XROrigin rig = FindAnyObjectByType<XROrigin>();
            if (seat == null || rig == null) return;

            CharacterController cc = rig.GetComponent<CharacterController>();
            bool had = cc != null && cc.enabled;
            if (had) cc.enabled = false;
            rig.transform.SetPositionAndRotation(seat.transform.position, seat.transform.rotation);
            if (had) cc.enabled = true;
        }

        // Disable walk / teleport / climb locomotion so the player stays put in the chair
        // (turning is left enabled). Works even if the networked game never becomes active,
        // unlike SeatLock which only engages once GameActive is true.
        private static readonly string[] LockedProviders =
        {
            "DynamicMoveProvider", "ContinuousMoveProvider", "TeleportationProvider",
            "ClimbProvider", "GrabMoveProvider", "TwoHandedGrabMoveProvider",
        };

        private void LockLocomotion()
        {
            XROrigin rig = FindAnyObjectByType<XROrigin>();
            if (rig == null) return;
            foreach (MonoBehaviour mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.enabled) continue;
                string n = mb.GetType().Name;
                for (int i = 0; i < LockedProviders.Length; i++)
                {
                    if (n == LockedProviders[i]) { mb.enabled = false; break; }
                }
            }
        }

        // ===================== Wait helpers (Next button always advances) =====================

        private IEnumerator WaitForAdvance()
        {
            advanceRequested = false;
            while (!advanceRequested) yield return null;
            advanceRequested = false;
        }

        private IEnumerator WaitUntil(Func<bool> cond)
        {
            advanceRequested = false;
            while (!advanceRequested && !(cond != null && cond())) yield return null;
            advanceRequested = false;
        }

        private void RequestAdvance() => advanceRequested = true;

        // ===================== UI construction (screen space) =====================

        private void BuildUI()
        {
            var canvasGo = new GameObject("TutorialHUD", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Bottom banner
            var banner = Panel(canvasGo.transform, new Color(0.05f, 0.02f, 0.12f, 0.92f));
            var brt = banner.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0, 60);
            brt.sizeDelta = new Vector2(1400, 200);

            bannerText = Text(banner.transform, "", 42, TextAlignmentOptions.Center);
            var trt = bannerText.rectTransform;
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(120, 20); trt.offsetMax = new Vector2(-120, -16);

            // Small arrow button in the bottom-right corner, clear of the text.
            nextButton = MakeButton(banner.transform, "▸", new Vector2(1, 0),
                new Vector2(-26, 26), new Vector2(88, 72), out nextLabel);
            nextLabel.fontSize = 54;
            nextButton.onClick.AddListener(RequestAdvance);
            // Just the arrow glyph — no square button background (still clickable via the transparent image).
            nextButton.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var ncb = nextButton.colors;
            ncb.normalColor = ncb.highlightedColor = ncb.pressedColor = ncb.selectedColor = new Color(1f, 1f, 1f, 0f);
            nextButton.colors = ncb;

            // Guide Book (opened with E) — a flip-book laid over the uploaded open-book image.
            controlsPanel = new GameObject("GuideBook", typeof(RectTransform), typeof(Image));
            controlsPanel.transform.SetParent(canvasGo.transform, false);
            var bookImg = controlsPanel.GetComponent<Image>();
            if (bookBackground != null)
            {
                bookImg.sprite = bookBackground;
                bookImg.color = Color.white;
                bookImg.preserveAspect = true;
            }
            else
            {
                bookImg.color = new Color(0.86f, 0.78f, 0.60f, 1f); // parchment placeholder until the PNG is assigned
            }
            var crt = controlsPanel.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0, 80);
            crt.sizeDelta = new Vector2(1450, 997); // matches the 602x414 book image aspect so text sits on the pages

            Color ink = new Color(0.24f, 0.16f, 0.09f);

            // Left page: heading + subtitle
            bookLeftText = Text(controlsPanel.transform, "", 40, TextAlignmentOptions.Center);
            bookLeftText.color = ink;
            var lp = bookLeftText.rectTransform;
            lp.anchorMin = new Vector2(0.09f, 0.18f); lp.anchorMax = new Vector2(0.46f, 0.82f);
            lp.offsetMin = Vector2.zero; lp.offsetMax = Vector2.zero;

            // Right page: details
            bookRightText = Text(controlsPanel.transform, "", 30, TextAlignmentOptions.TopLeft);
            bookRightText.color = ink;
            var rp = bookRightText.rectTransform;
            rp.anchorMin = new Vector2(0.55f, 0.18f); rp.anchorMax = new Vector2(0.92f, 0.82f);
            rp.offsetMin = Vector2.zero; rp.offsetMax = Vector2.zero;

            // Page indicator
            bookPageLabel = Text(controlsPanel.transform, "", 26, TextAlignmentOptions.Center);
            bookPageLabel.color = ink;
            var pgl = bookPageLabel.rectTransform;
            pgl.anchorMin = new Vector2(0.5f, 0.06f); pgl.anchorMax = new Vector2(0.5f, 0.06f);
            pgl.pivot = new Vector2(0.5f, 0f); pgl.sizeDelta = new Vector2(320, 48);

            // Flip arrows on the left/right edges of the book
            bookPrevBtn = MakeBookArrow(controlsPanel.transform, "←", new Vector2(0f, 0.5f), new Vector2(40, 0));
            bookPrevBtn.onClick.AddListener(() => FlipPage(-1));
            bookNextBtn = MakeBookArrow(controlsPanel.transform, "→", new Vector2(1f, 0.5f), new Vector2(-40, 0));
            bookNextBtn.onClick.AddListener(() => FlipPage(1));

            controlsPanel.SetActive(false);

            // Finish panel (Back to Menu)
            finishPanel = Panel(canvasGo.transform, new Color(0.04f, 0.02f, 0.10f, 0.95f));
            var frt = finishPanel.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0.5f, 0.5f); frt.anchorMax = new Vector2(0.5f, 0.5f);
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = new Vector2(0, 120);
            frt.sizeDelta = new Vector2(900, 360);
            var ft = Text(finishPanel.transform, "Tutorial Complete! ✨", 56, TextAlignmentOptions.Center);
            ft.color = new Color(0.72f, 0.45f, 1f);
            ft.rectTransform.anchorMin = new Vector2(0, 1); ft.rectTransform.anchorMax = new Vector2(1, 1);
            ft.rectTransform.pivot = new Vector2(0.5f, 1f);
            ft.rectTransform.anchoredPosition = new Vector2(0, -40);
            ft.rectTransform.sizeDelta = new Vector2(-60, 100);
            var backBtn = MakeButton(finishPanel.transform, "Back to Menu", new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(520, 96), out _);
            var brt2 = ((RectTransform)backBtn.transform); brt2.pivot = new Vector2(0.5f, 0f);
            backBtn.onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("zelda"));
            finishPanel.SetActive(false);
        }

        private void SetBanner(int step, string msg)
        {
            if (bannerText != null)
                bannerText.text = $"<size=28><color=#B78BFF>STEP {step} / {TotalSteps}</color></size>\n{msg}";
        }

        // The banner text carries the instruction, so the advance control is always just an
        // arrow (the 'label' arg is kept for call-site readability but not shown).
        private void SetNext(string label, bool visible)
        {
            if (nextLabel != null) nextLabel.text = "→";
            if (nextButton != null) nextButton.gameObject.SetActive(visible);
        }

        private void ShowControls(bool on)
        {
            if (controlsPanel == null) return;
            controlsPanel.SetActive(on);
            if (on) { bookPage = 0; RenderBookPage(); }
        }

        private void ShowFinish(bool on) { if (finishPanel != null) finishPanel.SetActive(on); if (on) SetNext("", false); }

        private void FlipPage(int dir)
        {
            bookPage = Mathf.Clamp(bookPage + dir, 0, PageBody.Length - 1);
            RenderBookPage();
        }

        private void RenderBookPage()
        {
            if (bookLeftText == null) return;
            bookLeftText.text = $"<size=64><b>{PageTitle[bookPage]}</b></size>\n\n<size=30><i>{PageSubtitle[bookPage]}</i></size>";
            bookRightText.text = PageBody[bookPage];
            if (bookPageLabel != null) bookPageLabel.text = $"{bookPage + 1} / {PageBody.Length}";
            if (bookPrevBtn != null) bookPrevBtn.gameObject.SetActive(bookPage > 0);
            if (bookNextBtn != null) bookNextBtn.gameObject.SetActive(bookPage < PageBody.Length - 1);
        }

        private Button MakeBookArrow(Transform parent, string glyph, Vector2 anchor, Vector2 pos)
        {
            Button b = MakeButton(parent, glyph, anchor, pos, new Vector2(96, 118), out TMP_Text lbl);
            lbl.fontSize = 58;
            var cb = b.colors;
            cb.normalColor = new Color(0.36f, 0.24f, 0.13f, 0.9f);
            cb.highlightedColor = new Color(0.52f, 0.36f, 0.20f, 1f);
            cb.pressedColor = new Color(0.26f, 0.16f, 0.08f, 1f);
            b.colors = cb;
            return b;
        }

        // ---- tiny UI factory ----
        private GameObject Panel(Transform parent, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private TMP_Text Text(Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align;
            tmp.color = new Color(0.96f, 0.93f, 1f); tmp.enableWordWrapping = true;
            return tmp;
        }

        private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 anchoredPos,
            Vector2 size, out TMP_Text labelOut)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
            go.GetComponent<Image>().color = Color.white;
            var btn = go.GetComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = new Color(0.36f, 0.20f, 0.66f);
            cb.highlightedColor = new Color(0.52f, 0.30f, 0.88f);
            cb.pressedColor = new Color(0.24f, 0.11f, 0.44f);
            cb.fadeDuration = 0.08f; btn.colors = cb;
            labelOut = Text(go.transform, label, 34, TextAlignmentOptions.Center);
            var lrt = labelOut.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            return btn;
        }

        // ---- floating seat marker (world space) ----
        private void BuildSeatMarker()
        {
            if (targetSeat == null) return;
            seatMarker = new GameObject("TutorialSeatMarker");
            seatMarker.transform.position = targetSeat.position + Vector3.up * 1.6f;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = ball.GetComponent<Collider>(); if (col != null) Destroy(col);
            ball.transform.SetParent(seatMarker.transform, false);
            ball.transform.localScale = Vector3.one * 0.18f;
            var mr = ball.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) { var m = new Material(sh); m.color = new Color(0.6f, 0.3f, 1f); mr.sharedMaterial = m; }

            var label = new GameObject("MarkerLabel");
            label.transform.SetParent(seatMarker.transform, false);
            label.transform.localPosition = Vector3.up * 0.35f;
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.text = "YOUR SEAT"; tmp.fontSize = 4; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.85f, 0.7f, 1f);

            seatMarker.AddComponent<MarkerBob>();
            seatMarker.SetActive(false);
        }

        /// <summary>Small self-contained bob/spin + billboard so the marker reads as "here".</summary>
        private class MarkerBob : MonoBehaviour
        {
            private Vector3 basePos;
            private void Start() => basePos = transform.position;
            private void Update()
            {
                transform.position = basePos + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.1f;
                Camera c = Camera.main;
                if (c != null)
                {
                    foreach (var t in GetComponentsInChildren<TMP_Text>())
                    {
                        Vector3 dir = t.transform.position - c.transform.position; dir.y = 0f;
                        if (dir.sqrMagnitude > 1e-4f) t.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    }
                }
            }
        }
    }
}
