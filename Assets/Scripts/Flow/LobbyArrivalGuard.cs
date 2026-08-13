using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Makes arriving in the lobby safe, whatever the player just came from.
    ///
    /// Two jobs, both about the same bug: after finishing the tutorial and returning here, the
    /// player fell through the world.
    ///
    /// The cause is that the tutorial leaves a LIVE session running. Verified in the editor:
    /// coming out of TutorialScene the lobby still had NetworkManager listening and connected.
    /// That breaks the project's standing rule that the lobby never holds a session — the lobby
    /// records intent and the game scene makes the one connection — and a live session in a scene
    /// with no seats is exactly the state that moves a rig somewhere the floor is not. The
    /// template's own CharacterResetter, for instance, snaps to (0, 0.15, 0) when a session comes
    /// up, and the lobby's floor is nowhere near the world origin.
    ///
    /// So: shut the session down, and separately refuse to let the player leave the floor. The
    /// second is a safety net rather than the fix. The fall reproduced on a headset and not in
    /// the editor, so anchoring the cure to one exact mechanism would be guessing; a net that
    /// catches ANY route to falling is worth having in a scene the player returns to constantly.
    /// </summary>
    public class LobbyArrivalGuard : MonoBehaviour
    {
        private const string LobbySceneName = "LobbyMirrorScene";

        // How far below the authored spawn counts as "gone". Generous, so a step down or a
        // legitimate crouch is never mistaken for a fall.
        private const float FallThreshold = 3f;

        private static LobbyArrivalGuard instance;

        private XROrigin origin;
        private CharacterController body;
        private Vector3 safePosition;
        private bool safePositionKnown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Spawn(SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive)
            {
                Spawn(scene.name);
            }
        }

        private static void Spawn(string sceneName)
        {
            if (sceneName != LobbySceneName || instance != null)
            {
                return;
            }

            instance = new GameObject("LobbyArrivalGuard").AddComponent<LobbyArrivalGuard>();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Start()
        {
            EndAnyLiveSession();
            CaptureSafePosition();
        }

        /// <summary>
        /// The lobby must never hold a session. Anything still connected here arrived from a
        /// scene that did not clean up after itself, and leaving it running would also break the
        /// next Host or Join: SessionIntentConnector expects to make the one and only connection.
        /// </summary>
        private static void EndAnyLiveSession()
        {
            NetworkManager network = NetworkManager.Singleton;
            bool live = network != null && (network.IsListening || network.IsClient || network.IsServer);

            if (!live)
            {
                return;
            }

            Debug.Log("[LobbyFlow] A session was still live on arriving at the lobby — shutting it " +
                      "down. The lobby never holds a session.");

            // Disconnect over Shutdown where possible: it unwinds the lobby service state too,
            // where a bare Shutdown just drops the transport and leaves the service thinking the
            // player is still in the room.
            if (XRINetworkGameManager.Instance != null)
            {
                XRINetworkGameManager.Instance.Disconnect();
            }
            else
            {
                network.Shutdown();
            }

            // So the next Host or Join is treated as a fresh intent rather than a repeat.
            LobbyIntent.Clear();
            SessionIntentConnector.ResetHandled();
        }

        /// <summary>
        /// Remember where the rig starts, having checked there is actually ground under it. The
        /// authored spawn is only a safe place to return to if it is verified as one.
        /// </summary>
        private void CaptureSafePosition()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                enabled = false;
                return;
            }

            body = origin.GetComponentInChildren<CharacterController>();

            Vector3 candidate = origin.transform.position;
            if (Physics.Raycast(candidate + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 50f))
            {
                // Sit the rig just above whatever it found, so the net never drops the player back
                // into the same mid-air spot they fell from.
                candidate.y = hit.point.y + 0.05f;
                safePositionKnown = true;
            }
            else
            {
                Debug.LogWarning("[LobbyFlow] Nothing under the lobby spawn — the fall-through net " +
                                 "has no verified ground to return the player to.");
            }

            safePosition = candidate;
        }

        private void Update()
        {
            if (!safePositionKnown || origin == null)
            {
                return;
            }

            if (origin.transform.position.y >= safePosition.y - FallThreshold)
            {
                return;
            }

            Debug.LogWarning("[LobbyFlow] Player fell out of the lobby (y=" +
                             origin.transform.position.y.ToString("F2") + ") — returning them to " +
                             safePosition.ToString("F2") + ".");

            // The controller has to be off for the move: CharacterController owns its transform
            // and silently discards a position written underneath it.
            bool had = body != null && body.enabled;
            if (had)
            {
                body.enabled = false;
            }

            origin.transform.position = safePosition;

            if (had)
            {
                body.enabled = true;
            }
        }
    }
}
