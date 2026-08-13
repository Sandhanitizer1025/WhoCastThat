using System.Collections;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Game-scene component that makes the one and only network connection, driven by whatever
    /// the player chose at the magic mirror (see <see cref="LobbyIntent"/>).
    ///
    /// It also owns every way that connection can FAIL. Left to the template, a failed join
    /// simply logs and leaves the player standing in the game scene, unconnected, with no
    /// message and no way back — a wrong room code looks identical to the game hanging.
    /// </summary>
    public class SessionIntentConnector : MonoBehaviour
    {
        [Tooltip("Players allowed in a created room. The board seats exactly seatRacks.Length " +
                 "(4); the template default of 20 would let extra players in with nowhere to sit.")]
        [SerializeField] private int maxPlayers = 4;

        [Tooltip("Seconds to wait for authentication before giving up.")]
        [SerializeField] private float authTimeoutSeconds = 20f;

        [Tooltip("Seconds to wait for the session itself before giving up.")]
        [SerializeField] private float connectTimeoutSeconds = 45f;

        [Tooltip("Scene to fall back to when connecting fails.")]
        [SerializeField] private string lobbySceneName = "LobbyMirrorScene";

        [Tooltip("If there is no pending lobby intent (entered the game scene directly), " +
                 "quick-join anyway. Leave OFF so the harness stays in charge of that case.")]
        [SerializeField] private bool quickJoinWhenNoIntent = false;

        /// <summary>
        /// True once this component has taken responsibility for connecting, so the test harness
        /// knows to stand down rather than firing a second, competing QuickJoinLobby().
        /// </summary>
        public static bool HandledConnection { get; private set; }

        /// <summary>
        /// Cleared by the lobby so a second trip through the flow connects normally. Without
        /// this the static stays true for the rest of the process.
        /// </summary>
        public static void ResetHandled()
        {
            HandledConnection = false;
        }

        string m_Failure;

        private IEnumerator Start()
        {
            if (!LobbyIntent.HasPending && !quickJoinWhenNoIntent)
            {
                yield break;
            }

            HandledConnection = true;

            // Name the player before the connection, so the name is already correct when
            // XRINetworkPlayer spawns and copies it into its replicated field.
            PlayerIdentity.Apply();

            // The manager authenticates in Awake. Arriving from BootScene the Firebase OIDC
            // sign-in has already happened, so AuthenticationManager skips both the UGS init
            // and the anonymous sign-in and just caches the Firebase-backed PlayerId.
            float deadline = Time.time + authTimeoutSeconds;
            while (Time.time < deadline &&
                   (XRINetworkGameManager.Instance == null ||
                    XRINetworkGameManager.CurrentConnectionState.Value <
                        XRINetworkGameManager.ConnectionState.Authenticated))
            {
                yield return null;
            }

            if (XRINetworkGameManager.Instance == null)
            {
                FailAndReturn("No network manager in the game scene.");
                yield break;
            }

            if (XRINetworkGameManager.CurrentConnectionState.Value <
                XRINetworkGameManager.ConnectionState.Authenticated)
            {
                FailAndReturn("Could not sign in. Check your connection and try again.");
                yield break;
            }

            XRINetworkGameManager.Instance.OnConnectionFailedAction += OnConnectionFailed;

            string roomName;
            string roomCode;
            LobbyRequest request = LobbyIntent.Consume(out roomName, out roomCode);

            switch (request)
            {
                case LobbyRequest.Create:
                    Debug.Log($"[LobbyFlow] Creating room \"{roomName}\" (max {maxPlayers}).");
                    XRINetworkGameManager.Instance.CreateNewLobby(roomName, false, maxPlayers);
                    break;

                case LobbyRequest.Join:
                    // An empty code must NEVER reach JoinLobbyByCode: SessionManager.JoinLobby
                    // falls through to QuickJoinLobby() when the code is blank, which silently
                    // drops the player into an unrelated room instead of reporting the mistake.
                    if (string.IsNullOrWhiteSpace(roomCode))
                    {
                        FailAndReturn("No room code entered.");
                        yield break;
                    }

                    roomCode = roomCode.Trim().ToUpperInvariant();
                    Debug.Log($"[LobbyFlow] Joining by room code {roomCode}.");
                    XRINetworkGameManager.Instance.JoinLobbyByCode(roomCode);
                    break;

                default:
                    yield return QuickPlay();
                    break;
            }

            // Nothing above blocks, so watch for the outcome. A wrong code, a full room and a
            // dropped network all land here rather than leaving the player stranded.
            float connectDeadline = Time.time + connectTimeoutSeconds;
            while (Time.time < connectDeadline)
            {
                if (XRINetworkGameManager.Connected.Value)
                {
                    yield break;                       // connected: this component is done
                }
                if (m_Failure != null)
                {
                    FailAndReturn(m_Failure);
                    yield break;
                }
                yield return null;
            }

            FailAndReturn("Timed out trying to reach the room.");
        }

        /// <summary>
        /// Quick play, but only into OUR game. The template's QuickJoinLobby uses an EMPTY
        /// filter, so it will happily drop the player into any session in the whole project —
        /// including a teammate's unrelated scene. The session's scene name is recorded in its
        /// properties, so filter on it here. Those properties are not indexed server-side, so
        /// the match has to be done client-side.
        /// </summary>
        private IEnumerator QuickPlay()
        {
            Debug.Log("[LobbyFlow] Quick play: looking for an existing room in this game.");

            Task<QuerySessionsResults> query =
                MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            while (!query.IsCompleted)
            {
                yield return null;
            }

            ISessionInfo best = null;
            if (query.Status == TaskStatus.RanToCompletion && query.Result != null)
            {
                Debug.Log($"[LobbyFlow] Quick play saw {query.Result.Sessions.Count} session(s).");
                foreach (ISessionInfo session in query.Result.Sessions)
                {
                    bool ours = IsOurGame(session);
                    bool room = session.AvailableSlots > 0;
                    Debug.Log($"[LobbyFlow]   \"{session.Name}\" scene={SceneTagOf(session)} " +
                              $"slots={session.AvailableSlots} ours={ours} -> " +
                              $"{(ours && room ? "JOIN" : "skip")}");
                    if (room && ours && best == null)
                    {
                        best = session;
                    }
                }
            }

            if (best != null)
            {
                Debug.Log($"[LobbyFlow] Quick play joining \"{best.Name}\".");
                XRINetworkGameManager.Instance.JoinLobbySpecific(best);
            }
            else
            {
                string room = LobbyIntent.SuggestedRoomName();
                Debug.Log($"[LobbyFlow] Quick play found no room; creating \"{room}\".");
                XRINetworkGameManager.Instance.CreateNewLobby(room, false, maxPlayers);
            }
        }

        private static string SceneTagOf(ISessionInfo session)
        {
            SessionProperty scene;
            if (session.Properties != null &&
                session.Properties.TryGetValue(SessionManager.k_SceneKeyIdentifier, out scene) &&
                scene != null)
            {
                return scene.Value;
            }
            return "<none>";
        }

        private bool IsOurGame(ISessionInfo session)
        {
            if (session.Properties == null)
            {
                return false;
            }

            SessionProperty scene;
            if (!session.Properties.TryGetValue(SessionManager.k_SceneKeyIdentifier, out scene))
            {
                return false;
            }
            if (scene == null || scene.Value != SceneManager.GetActiveScene().name)
            {
                return false;
            }

            // Different builds cannot play together; the host records its version too.
            SessionProperty build;
            if (session.Properties.TryGetValue(SessionManager.k_BuildIdKeyIdentifier, out build) &&
                build != null && build.Value != Application.version)
            {
                return false;
            }

            return true;
        }

        // The service's own wording is for developers ("lobby code 'ZZZZZZ' contains an invalid
        // character 'Z' (U+005A) at index 0"). Translate the cases a player can actually cause.
        private void OnConnectionFailed(string reason)
        {
            string text = reason ?? "";
            string lower = text.ToLowerInvariant();

            if (lower.Contains("invalid character") || lower.Contains("invalid join code"))
            {
                m_Failure = "That room code isn't a valid one. Check it and try again.";
            }
            else if (lower.Contains("not found") || lower.Contains("no longer exists"))
            {
                m_Failure = "No room with that code. It may have closed.";
            }
            else if (lower.Contains("full") || lower.Contains("no available slots"))
            {
                m_Failure = "That room is full.";
            }
            else if (lower.Contains("rate limit"))
            {
                m_Failure = "Too many attempts. Wait a moment and try again.";
            }
            else
            {
                m_Failure = string.IsNullOrEmpty(text) ? "Could not reach that room." : text;
            }
        }

        private void FailAndReturn(string reason)
        {
            Debug.LogWarning($"[LobbyFlow] {reason} Returning to the lobby.");
            LobbyIntent.SetError(reason);
            ResetHandled();
            SceneManager.LoadScene(lobbySceneName);
        }

        private void OnDestroy()
        {
            if (XRINetworkGameManager.Instance != null)
            {
                XRINetworkGameManager.Instance.OnConnectionFailedAction -= OnConnectionFailed;
            }
        }
    }
}
