using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace WhoCastThat.Tutorial
{
    /// <summary>
    /// Starts the tutorial as a self-contained offline host.
    ///
    /// The tutorial is single player, but the gameplay it teaches (NetworkedSpellGame) is a
    /// NetworkBehaviour, so a NetworkManager has to exist for potions to be dealt, cast or drawn.
    /// What it does NOT need is Unity's lobby: the stock flow creates and abandons a
    /// "Player's Room" session on every run, and the service eventually stops issuing join codes —
    /// at which point the tutorial silently never connects and no potions spawn at all.
    ///
    /// So this starts a host on loopback with UnityTransport instead. No lobby, no relay, no
    /// internet, nothing shared with the team's session quota. Nobody can join, which is exactly
    /// what a tutorial wants.
    ///
    /// Requires MultiplayerTestHarness.autoConnectOnStart to be OFF in this scene, or its
    /// QuickJoinLobby() lands on top of this host and tears it down to "hot join".
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class TutorialOfflineHost : MonoBehaviour
    {
        [Tooltip("Loopback port for the in-process host. Nothing outside this machine connects to it.")]
        [SerializeField] private ushort port = 7777;

        void Start()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[TutorialOfflineHost] No NetworkManager in the scene.");
                return;
            }

            if (nm.IsListening)
            {
                Debug.Log("[TutorialOfflineHost] Already listening; leaving the existing host alone.");
                return;
            }

            // The scene's transport is the distributed-authority one, which only works through the
            // multiplayer service. Swap in plain UTP so the host is purely local.
            UnityTransport utp = nm.GetComponent<UnityTransport>();
            if (utp == null) utp = nm.gameObject.AddComponent<UnityTransport>();
            utp.SetConnectionData("127.0.0.1", port);

            // Pointing NetworkConfig at UTP is not enough. The distributed-authority transport is
            // still a live component on this object and polices topology on its own: it sees a
            // ClientServer host, reports "Topology Mismatch", and shuts the whole NetworkManager
            // down a second later — which despawns the game and every potion with it.
            foreach (NetworkTransport other in nm.GetComponents<NetworkTransport>())
            {
                if (other != utp && other is Behaviour behaviour)
                {
                    behaviour.enabled = false;
                    Debug.Log("[TutorialOfflineHost] Disabled " + other.GetType().Name + " for offline play.");
                }
            }

            nm.NetworkConfig.NetworkTransport = utp;

            // The scene is configured for DistributedAuthority. Starting a plain host while the
            // topology still says DistributedAuthority is what produces "Detected DAHost mode ...
            // Topology Mismatch ... Disconnecting from session" a second after the hand is dealt.
            nm.NetworkConfig.NetworkTopology = NetworkTopologyTypes.ClientServer;

            if (nm.StartHost())
                Debug.Log("[TutorialOfflineHost] Offline host started on 127.0.0.1:" + port + " — no lobby involved.");
            else
                Debug.LogError("[TutorialOfflineHost] StartHost() failed.");
        }
    }
}
