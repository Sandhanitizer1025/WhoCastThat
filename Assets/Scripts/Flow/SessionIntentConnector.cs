using System.Collections;
using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Game-scene component that makes the one and only network connection, driven by whatever
    /// the player chose at the magic mirror (see <see cref="LobbyIntent"/>).
    ///
    /// Drop this on the same object as the test harness. It replaces the harness's blind
    /// QuickJoinLobby() auto-connect for the real Boot -> Lobby -> Game flow; the harness still
    /// handles the case where you enter the game scene directly for rules testing.
    /// </summary>
    public class SessionIntentConnector : MonoBehaviour
    {
        [Tooltip("Players allowed in a created room. The board seats exactly seatRacks.Length " +
                 "(4); the template default of 20 would let extra players in with nowhere to sit.")]
        [SerializeField] private int maxPlayers = 4;

        [Tooltip("Seconds to wait for authentication to finish before giving up.")]
        [SerializeField] private float authTimeoutSeconds = 20f;

        [Tooltip("If there is no pending lobby intent (entered the game scene directly), " +
                 "quick-join anyway. Leave OFF so the harness stays in charge of that case.")]
        [SerializeField] private bool quickJoinWhenNoIntent = false;

        /// <summary>
        /// True once this component has taken responsibility for connecting, so the test harness
        /// knows to stand down rather than firing a second, competing QuickJoinLobby().
        /// </summary>
        public static bool HandledConnection { get; private set; }

        private IEnumerator Start()
        {
            if (!LobbyIntent.HasPending && !quickJoinWhenNoIntent)
            {
                yield break;
            }

            HandledConnection = true;

            // The manager authenticates in Awake, and when we arrive from BootScene the Firebase
            // OIDC sign-in has already happened -- AuthenticationManager then skips both the UGS
            // init and the anonymous sign-in and simply caches the Firebase-backed PlayerId.
            // Either way we must not call Create/Join before the state leaves Authenticating.
            float deadline = Time.time + authTimeoutSeconds;
            while (Time.time < deadline)
            {
                if (XRINetworkGameManager.Instance != null &&
                    XRINetworkGameManager.CurrentConnectionState.Value >=
                        XRINetworkGameManager.ConnectionState.Authenticated)
                {
                    break;
                }
                yield return null;
            }

            if (XRINetworkGameManager.Instance == null)
            {
                Debug.LogError("[LobbyFlow] No XRINetworkGameManager in the game scene; cannot connect.");
                yield break;
            }

            string roomName;
            string roomCode;
            LobbyRequest request = LobbyIntent.Consume(out roomName, out roomCode);

            switch (request)
            {
                case LobbyRequest.Create:
                    Debug.Log("[LobbyFlow] Creating room \"" + roomName + "\" (max " + maxPlayers + ").");
                    XRINetworkGameManager.Instance.CreateNewLobby(roomName, false, maxPlayers);
                    break;

                case LobbyRequest.Join:
                    Debug.Log("[LobbyFlow] Joining by room code " + roomCode + ".");
                    XRINetworkGameManager.Instance.JoinLobbyByCode(roomCode);
                    break;

                default:
                    Debug.Log("[LobbyFlow] Quick play.");
                    XRINetworkGameManager.Instance.QuickJoinLobby();
                    break;
            }
        }
    }
}
