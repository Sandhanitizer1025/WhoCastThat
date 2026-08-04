using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Desktop / Multiplayer-Play-Mode test harness for <see cref="NetworkedSpellGame"/>.
    /// Lets you drive connection and turns from the keyboard and shows an on-screen
    /// readout, so the networked card logic can be validated with two virtual players
    /// BEFORE the physical VR potion interactions are wired up.
    ///
    /// Keys (Game view focused):
    ///   C        Quick-join a session (both MPPM players land in the same one)
    ///   D        Request Draw for the local player
    ///   1/2/3/4  Cast Hex / Phase / Curse / Counterspell (targets the other player)
    ///
    /// This is a testing aid only — safe to disable or delete once real interactions
    /// drive RequestDraw()/RequestCast().
    /// </summary>
    public class MultiplayerTestHarness : MonoBehaviour
    {
        [Tooltip("Auto quick-join a session shortly after entering play mode.")]
        [SerializeField] private bool autoConnectOnStart = true;

        [Tooltip("Seconds to wait after start before auto-connecting (lets services init).")]
        [SerializeField] private float autoConnectDelay = 1.5f;

        private string lastAnnouncement = "";
        private string lastForesight = "";

        private void OnEnable()
        {
            NetworkedSpellGame.AnnouncementChanged += OnAnnouncement;
            NetworkedSpellGame.ForesightRevealed += OnForesight;
        }

        private void OnDisable()
        {
            NetworkedSpellGame.AnnouncementChanged -= OnAnnouncement;
            NetworkedSpellGame.ForesightRevealed -= OnForesight;
        }

        // Foresight is private information: this fires only on the client that cast it.
        private void OnForesight(PotionType[] top)
        {
            lastForesight = top.Length == 0 ? "(cauldron empty)" : string.Join(", ", top);
        }

        private void Start()
        {
            if (autoConnectOnStart)
            {
                Invoke(nameof(Connect), autoConnectDelay);
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            // HOLD LEFT CTRL for any of these. Every one of these keys is ALSO an XR simulator
            // binding — D is "X Translate" (strafe right), 1-8 are the controller buttons, and
            // C and 9 are simulator toggles. Bare presses meant that simply walking sideways
            // fired RequestDraw(), which draws instantly with no stir or float animation and
            // ends the turn, so the cauldron jumped to the next player on its own. Requiring a
            // modifier the simulator does not use keeps the shortcuts without the collisions.
            if (!kb.leftCtrlKey.isPressed)
            {
                return;
            }

            if (kb.cKey.wasPressedThisFrame) Connect();
            if (kb.dKey.wasPressedThisFrame) Draw();
            if (kb.digit1Key.wasPressedThisFrame) Cast(PotionType.Hex);
            if (kb.digit2Key.wasPressedThisFrame) Cast(PotionType.Phase);
            if (kb.digit3Key.wasPressedThisFrame) DrawCurse();
            if (kb.digit4Key.wasPressedThisFrame) Cast(PotionType.Counterspell);
            if (kb.digit5Key.wasPressedThisFrame) Cast(PotionType.Dispel);
            if (kb.digit6Key.wasPressedThisFrame) Cast(PotionType.Reflection);
            if (kb.digit7Key.wasPressedThisFrame) Cast(PotionType.Tribute);
            if (kb.digit8Key.wasPressedThisFrame) Cast(PotionType.Foresight);
            if (kb.digit9Key.wasPressedThisFrame) Cast(PotionType.Warp);
        }

        private void Connect()
        {
            if (XRINetworkGameManager.Instance != null)
            {
                XRINetworkGameManager.Instance.QuickJoinLobby();
            }
        }

        private void Draw()
        {
            if (NetworkedSpellGame.Instance != null)
            {
                NetworkedSpellGame.Instance.RequestDraw();
            }
        }

        private void Cast(PotionType type)
        {
            if (NetworkedSpellGame.Instance != null)
            {
                NetworkedSpellGame.Instance.RequestCast(type, GetOtherClientId());
            }
        }

        private void DrawCurse()
        {
            if (NetworkedSpellGame.Instance != null)
            {
                NetworkedSpellGame.Instance.DebugDrawCurse();
            }
        }

        // First connected client id that is not us (fallback: our own id).
        private ulong GetOtherClientId()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return 0;
            }

            foreach (ulong id in nm.ConnectedClientsIds)
            {
                if (id != nm.LocalClientId)
                {
                    return id;
                }
            }
            return nm.LocalClientId;
        }

        private void OnAnnouncement(string text)
        {
            lastAnnouncement = text;
        }

        private void OnGUI()
        {
            const int w = 520;
            GUILayout.BeginArea(new Rect(10, 10, w, 300), GUI.skin.box);

            bool connected = XRINetworkGameManager.Connected != null && XRINetworkGameManager.Connected.Value;
            GUILayout.Label($"<b>Networked Spell Game — Test Harness</b>");
            GUILayout.Label($"Connected: {connected}   LocalId: {(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId.ToString() : "-")}");

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game != null)
            {
                GUILayout.Label($"Game active: {game.GameActive}");
                GUILayout.Label($"Current turn: Player {game.CurrentTurnClientId}   (yours: {game.IsLocalPlayersTurn})   turns owed: {game.TurnsRemaining}");
                if (game.InterruptWindowOpen)
                {
                    GUILayout.Label("<b>*** DISPEL / REFLECT WINDOW OPEN ***</b>");
                }
            }
            else
            {
                GUILayout.Label("SpellGameManager not spawned yet.");
            }

            GUILayout.Label($"Announcement: {lastAnnouncement}");
            if (!string.IsNullOrEmpty(lastForesight))
            {
                GUILayout.Label($"Foresight (you only): {lastForesight}");
            }
            GUILayout.Space(6);
            GUILayout.Label("Keys: C=Connect  D=Draw(ends turn)  3=DrawCurse");
            GUILayout.Label("Cast: 1=Hex 2=Phase 4=Counterspell 5=Dispel 6=Reflection");
            GUILayout.Label("      7=Tribute 8=Foresight 9=Warp");

            GUILayout.EndArea();
        }
    }
}
