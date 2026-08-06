using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Turns the magic mirror's placeholder buttons into the real Boot -> Lobby -> Game flow.
    ///
    /// DELIBERATELY A SEPARATE COMPONENT. MagicMirrorMenu.cs is a teammate's file that lives on
    /// main and is still being changed there, and its scene is a 114 KB YAML that cannot be
    /// merged by hand. Because MagicMirrorMenu wires its own stub handlers in Awake() with
    /// AddListener, this router can simply clear them in Start() and install its own -- same
    /// buttons, same look, zero edits to their file, so no merge conflict is ever possible.
    /// </summary>
    public class MirrorMenuRouter : MonoBehaviour
    {
        [Tooltip("Scene loaded once the player has chosen how to play.")]
        [SerializeField] private string gameSceneName = "InteractionTestScene";

        [Tooltip("Length of a Unity session join code.")]
        [SerializeField] private int roomCodeLength = 6;

        [Tooltip("Tutorial scene reached from How to Play.")]
        [SerializeField] private string tutorialSceneName = "TutorialScene";

        [Tooltip("Fallback room name if the player somehow arrived without a saved username.")]
        [SerializeField] private string fallbackPlayerName = "Mage";

        // Written by FirebaseLoginManager on a successful login/sign-up in BootScene.
        const string LocalUsernameKey = "PlayerUsername";

        MagicMirrorMenu m_Menu;
        RoomCodePad m_Pad;

        void Start()
        {
            m_Menu = FindAnyObjectByType<MagicMirrorMenu>();
            if (m_Menu == null)
            {
                Debug.LogError("[LobbyFlow] No MagicMirrorMenu in the scene. " +
                               "Run WhoCastThat/Build Mirror Menu first.");
                return;
            }

            // Any stale intent from a previous round must not survive a return to the lobby.
            LobbyIntent.Clear();

            Rewire(m_Menu.playButton, OnPlay);
            Rewire(m_Menu.hostButton, OnHost);
            Rewire(m_Menu.joinButton, OnJoin);
            Rewire(m_Menu.howToButton, OnHowToPlay);

            RectTransform canvasRect = m_Menu.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                m_Pad = RoomCodePad.Build(canvasRect, roomCodeLength);
                m_Pad.Submitted += OnCodeEntered;
                m_Pad.Cancelled += OnCodeCancelled;
            }
        }

        static void Rewire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();   // drops MagicMirrorMenu's Debug.Log stubs
            button.onClick.AddListener(action);
        }

        string PlayerName()
        {
            string saved = PlayerPrefs.GetString(LocalUsernameKey, "");
            return string.IsNullOrEmpty(saved) ? fallbackPlayerName : saved;
        }

        // --- button handlers ---------------------------------------------------------------

        void OnPlay()
        {
            Debug.Log("[LobbyFlow] Quick play.");
            LobbyIntent.SetQuickPlay();
            GoToGame();
        }

        void OnHost()
        {
            string room = PlayerName() + "'s Room";
            Debug.Log("[LobbyFlow] Hosting \"" + room + "\".");
            LobbyIntent.SetCreate(room);
            GoToGame();
        }

        void OnJoin()
        {
            // The join code is issued by the Unity session service, so it cannot be chosen here.
            // The host reads theirs off the HUD in the game scene and passes it to the others.
            if (m_Pad == null)
            {
                Debug.LogError("[LobbyFlow] Room code pad was never built.");
                return;
            }
            SetMainPanelVisible(false);
            m_Pad.Show();
        }

        void OnCodeEntered(string code)
        {
            Debug.Log("[LobbyFlow] Joining room code " + code + ".");
            LobbyIntent.SetJoin(code);
            GoToGame();
        }

        void OnCodeCancelled()
        {
            SetMainPanelVisible(true);
        }

        void OnHowToPlay()
        {
            // TutorialDriver's Back button hardcodes LoadScene("zelda"), so arm the redirect
            // that brings the player back here instead. See TutorialReturnRedirect.
            TutorialReturnRedirect.Arm(SceneManager.GetActiveScene().name);
            SceneManager.LoadScene(tutorialSceneName);
        }

        void SetMainPanelVisible(bool visible)
        {
            Transform main = m_Menu.transform.Find("MainPanel");
            if (main != null) main.gameObject.SetActive(visible);
        }

        void GoToGame()
        {
            // Heading into the match, not the tutorial: make sure a stale redirect cannot fire.
            TutorialReturnRedirect.Disarm();
            SceneManager.LoadScene(gameSceneName);
        }
    }
}
