using Unity.Netcode;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Trigger volume in the centre of the table. When a potion the LOCAL player owns
    /// is dropped in, this casts that potion's spell (targeting the next player) and
    /// despawns the potion across the network.
    ///
    /// The owner check ensures only one client sends the cast (and the owner has the
    /// network authority needed to despawn the potion in Distributed Authority).
    /// Requires a trigger Collider on this GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlayZone : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            NetworkedPotion potion = other.GetComponentInParent<NetworkedPotion>();
            if (potion == null || NetworkedSpellGame.Instance == null)
            {
                return;
            }

            NetworkObject netObj = potion.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned || !netObj.IsOwner)
            {
                return; // only the potion's owner resolves the cast
            }

            // Resolve the spell on the authority, then remove the played potion.
            NetworkedSpellGame.Instance.RequestCast(potion.Type);
            netObj.Despawn();
        }
    }
}
