using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// Gives every networked player a <see cref="PlayerHat"/> as they appear.
    ///
    /// A slow scan rather than a spawn callback, deliberately. Players arrive through the
    /// template's netcode, which spawns the avatar prefab — a prefab in VRMPAssets that we cannot
    /// add a component to. Scanning is indifferent to HOW a player showed up, so it also covers
    /// reconnects and late joiners without hooking anything template-side.
    ///
    /// Installs itself from a runtime hook, like the other presentation pieces.
    /// </summary>
    public class PlayerHatInstaller : MonoBehaviour
    {
        // Players join on human timescales; four checks a second is far more than enough and
        // costs nothing next to a per-frame scan.
        private const float ScanIntervalSeconds = 0.25f;

        private static PlayerHatInstaller instance;
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var go = new GameObject("PlayerHatInstaller");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PlayerHatInstaller>();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan)
            {
                return;
            }

            nextScan = Time.unscaledTime + ScanIntervalSeconds;

            XRINetworkPlayer[] players =
                FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None);

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].GetComponent<PlayerHat>() == null)
                {
                    players[i].gameObject.AddComponent<PlayerHat>();
                }
            }
        }
    }
}
