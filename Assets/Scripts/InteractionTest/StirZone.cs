using System.Collections.Generic;
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

        // Hands currently overlapping the pot, and whether we will accept the next one.
        private readonly HashSet<Collider> handsInside = new HashSet<Collider>();
        private CauldronOrbit orbit;
        private bool armed;

        private void Awake()
        {
            orbit = GetComponentInParent<CauldronOrbit>();
        }

        // The pot orbits to the active player, so it can slide over a hand that was
        // already resting there — Unity raises OnTriggerEnter for that just the same, and
        // the round would brew itself the moment the pot arrived. So we only arm once the
        // pot has settled AND the zone is empty; the dip then has to be a real entry.
        private void Update()
        {
            handsInside.RemoveWhere(hand => hand == null || !hand.gameObject.activeInHierarchy);

            if (!armed && handsInside.Count == 0 && (orbit == null || orbit.IsSettled))
            {
                armed = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsLocalHand(other))
            {
                return;
            }

            handsInside.Add(other);

            if (!armed || Time.time < nextDipTime)
            {
                return;
            }

            if (orbit != null && !orbit.IsSettled)
            {
                return; // pot is still flying to us
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null || !game.IsLocalPlayersTurn || !game.CanBrew)
            {
                return;
            }

            armed = false;
            nextDipTime = Time.time + dipCooldown;
            game.RequestBrew();
        }

        private void OnTriggerExit(Collider other)
        {
            handsInside.Remove(other);
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
