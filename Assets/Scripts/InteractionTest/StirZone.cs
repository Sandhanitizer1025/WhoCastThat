using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Trigger volume in the cauldron. When the LOCAL player dips a hand in on their
    /// turn, it asks the authority to brew: the ladle stirs for a moment and then a
    /// potion floats out to the rack. The authority validates the turn, so out-of-turn
    /// dips are harmless.
    ///
    /// A hand counts as "local" if the entering collider belongs to a NetworkObject we
    /// own (the avatar hands are networked) — remote players' hands are owned by them and
    /// ignored. Keep this as a child of the cauldron rig so it orbits with the pot.
    /// Requires a trigger Collider (and, since hands carry no Rigidbody, a kinematic
    /// Rigidbody for trigger events to fire).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StirZone : MonoBehaviour
    {
        [Tooltip("Minimum seconds between dips, so one hand-dip starts a single brew.")]
        [SerializeField] private float dipCooldown = 1f;

        private float nextDipTime;
        private Transform localRig;

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time < nextDipTime)
            {
                return;
            }

            if (!IsLocalHand(other))
            {
                return;
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null || !game.IsLocalPlayersTurn || !game.CanBrew)
            {
                return;
            }

            nextDipTime = Time.time + dipCooldown;
            game.RequestBrew();
        }

        // The avatar hands are networked objects (Player/<hand>/HandCollider): the ones
        // WE own are the local player's, everyone else's are remote. Ownership is the
        // reliable local/remote test here (these colliders have no interactor and are not
        // under the XR Origin). Fall back to interactor / XR-Origin checks for setups
        // where the local hand is a plain (non-networked) collider.
        private bool IsLocalHand(Collider other)
        {
            Unity.Netcode.NetworkObject netObj = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
            if (netObj != null)
            {
                return netObj.IsOwner;
            }

            if (other.GetComponentInParent<XRBaseInteractor>() != null)
            {
                return true;
            }

            if (localRig == null)
            {
                XROrigin origin = FindAnyObjectByType<XROrigin>();
                if (origin != null)
                {
                    localRig = origin.transform;
                }
            }
            return localRig != null && other.transform.IsChildOf(localRig);
        }
    }
}
