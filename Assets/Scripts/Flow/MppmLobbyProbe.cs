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
                PublishRoomCode();
                return;
            }

            string cmd = ReadCommand();
            if (string.IsNullOrEmpty(cmd) || cmd == m_LastSeen)
            {
                return;
            }
            m_LastSeen = cmd;
            Handle(cmd);
        }

        // Main editor side. Publishing the code the moment the room exists keeps the whole
        // handshake in-process: driving it over MCP instead meant ~25 s per step, and the host's
        // relay allocation was dropped for inactivity before the clone ever got the code.
        void PublishRoomCode()
        {
            string code = XRMultiplayer.XRINetworkGameManager.ConnectedRoomCode;
            if (string.IsNullOrEmpty(code) || code == m_LastSeen)
            {
                return;
            }
            m_LastSeen = code;
            WriteCommand("JOIN:" + code);
            Debug.Log($"[Probe] published room code {code} for the clone.");
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

            // Commands are only meaningful from the lobby; ignore them mid-match.
            if (scene != "LobbyMirrorScene")
            {
                Debug.Log("[Probe] not in the lobby — ignoring.");
                return;
            }

            // Strip the nonce the main editor appends so repeated identical commands still fire.
            string body = cmd;
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                body = body.Substring(0, hash);
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
