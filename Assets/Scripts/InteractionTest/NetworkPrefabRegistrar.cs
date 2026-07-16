using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Registers gameplay network prefabs with the NetworkManager at runtime, before
    /// the session connects, so spawnable prefabs (potions, etc.) don't require editing
    /// the shared Network Manager prefab or its prefab lists. This keeps the gameplay
    /// drop-in and merge-safe.
    ///
    /// Runs on every client, registering the same list, so Netcode's ForceSamePrefabs
    /// requirement stays satisfied. Registration happens in Start (NetworkManager exists
    /// by then) which is before the harness/mirror UI triggers the connection.
    /// </summary>
    public class NetworkPrefabRegistrar : MonoBehaviour
    {
        [Tooltip("Prefabs with a NetworkObject to register for spawning at runtime.")]
        [SerializeField] private List<GameObject> networkPrefabs = new List<GameObject>();

        private void Start()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogWarning("[NetworkPrefabRegistrar] No NetworkManager found.", this);
                return;
            }

            if (nm.IsListening)
            {
                // Already connected — too late to register safely.
                return;
            }

            foreach (GameObject prefab in networkPrefabs)
            {
                if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
                {
                    continue;
                }

                // AddNetworkPrefab throws if the prefab is already registered; that is fine.
                try
                {
                    nm.AddNetworkPrefab(prefab);
                }
                catch (System.Exception)
                {
                    // Already registered — ignore.
                }
            }
        }
    }
}
