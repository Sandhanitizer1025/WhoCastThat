using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// THROWAWAY TEST DRIVER — lets the MPPM clone be driven from the main editor.
    ///
    /// Delete once the lobby flow is signed off. It spawns itself via
    /// RuntimeInitializeOnLoadMethod rather than being placed in a scene, so it leaves no
    /// serialized reference behind and is inert outside the editor.
    ///
    /// The channel is a FILE in the system temp folder, not EditorPrefs. EditorPrefs are shared
    /// between the main editor and its clones, but each process caches them in memory, so a
    /// write from the main editor is not observed live by a running clone. A file is read fresh
    /// every poll and genuinely crosses the process boundary. Commands are obeyed only by
    /// clones, never the main editor, so the two never drive each other.
    /// </summary>
    public class MppmLobbyProbe : MonoBehaviour
    {
        public static string CommandFile
        {
            get { return Path.Combine(Path.GetTempPath(), "wct_test_cmd.txt"); }
        }

        string m_LastSeen = "";
        bool m_IsClone;
        float m_NextPoll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (!Application.isEditor)
            {
                return;
            }
            GameObject go = new GameObject("~MppmLobbyProbe");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<MppmLobbyProbe>();
            DontDestroyOnLoad(go);
        }

        void Start()
        {
            m_IsClone = !IsMainEditor();
            m_LastSeen = ReadCommand();
            Debug.Log($"[Probe v2] running as {(m_IsClone ? "CLONE" : "MAIN EDITOR")}; " +
                      $"{(m_IsClone ? "watching for commands in " : "will publish room code to ")}{CommandFile}");
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextPoll)
            {
                return;
            }
            m_NextPoll = Time.unscaledTime + 0.5f;

            if (!m_IsClone)
            {
                return;   // the main editor is driven explicitly; it publishes nothing on its own
            }

            string cmd = ReadCommand();
            if (string.IsNullOrEmpty(cmd) || cmd == m_LastSeen)
            {
                return;
            }
            m_LastSeen = cmd;
            Handle(cmd);
        }

        public static void WriteCommand(string body)
        {
            try
            {
                File.WriteAllText(CommandFile, body + "#" + System.DateTime.Now.Ticks);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Probe] could not write command: " + e.Message);
            }
        }

        static string ReadCommand()
        {
            try
            {
                return File.Exists(CommandFile) ? File.ReadAllText(CommandFile).Trim() : "";
            }
            catch (IOException)
            {
                return "";   // mid-write by the other process; just try again next poll
            }
        }

        void Handle(string cmd)
        {
            string scene = SceneManager.GetActiveScene().name;
            Debug.Log($"[Probe] got '{cmd}' while in '{scene}'.");

            // Strip the nonce the main editor appends so repeated identical commands still fire.
            string body = cmd;
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                body = body.Substring(0, hash);
            }

            // LOBBY is the one command that is valid mid-match: it resets this clone so the
            // next scenario can be set up without restarting play mode.
            if (body == "LOBBY")
            {
                Debug.Log("[Probe] returning to the lobby.");
                LobbyIntent.Clear();
                SessionIntentConnector.ResetHandled();
                if (XRMultiplayer.XRINetworkGameManager.Instance != null)
                {
                    XRMultiplayer.XRINetworkGameManager.Instance.Disconnect();
                }
                SceneManager.LoadScene("LobbyMirrorScene");
                return;
            }

            // The rest only make sense from the lobby.
            if (scene != "LobbyMirrorScene")
            {
                Debug.Log("[Probe] not in the lobby — ignoring.");
                return;
            }

            if (body.StartsWith("JOIN:"))
            {
                string code = body.Substring(5);
                Debug.Log($"[Probe] joining with code '{code}'.");
                LobbyIntent.SetJoin(code);
                SessionIntentConnector.ResetHandled();
                SceneManager.LoadScene("InteractionTestScene");
            }
            else if (body == "HOST")
            {
                LobbyIntent.SetCreate(LobbyIntent.SuggestedRoomName() + " (clone)");
                SessionIntentConnector.ResetHandled();
                SceneManager.LoadScene("InteractionTestScene");
            }
            else if (body == "QUICK")
            {
                LobbyIntent.SetQuickPlay();
                SessionIntentConnector.ResetHandled();
                SceneManager.LoadScene("InteractionTestScene");
            }
        }

        // Assembly-CSharp does not reference the MPPM assembly, so this has to go through
        // reflection. Absent MPPM entirely, treat the process as the main editor.
        static bool IsMainEditor()
        {
            System.Type t = System.Type.GetType(
                "Unity.Multiplayer.Playmode.CurrentPlayer, Unity.Multiplayer.Playmode");
            if (t == null)
            {
                return true;
            }
            PropertyInfo p = t.GetProperty("IsMainEditor", BindingFlags.Public | BindingFlags.Static);
            if (p == null)
            {
                return true;
            }
            return (bool)p.GetValue(null, null);
        }
    }
}
