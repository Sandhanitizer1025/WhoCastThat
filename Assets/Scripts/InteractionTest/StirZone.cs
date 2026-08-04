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
        [Tooltip("Minimum seconds between draws, so one confirm starts a single brew.")]
        [SerializeField] private float dipCooldown = 1f;

        private float nextDipTime;
        private Transform localRig;

        // Hands currently overlapping the pot.
        private readonly HashSet<Collider> handsInside = new HashSet<Collider>();
        private CauldronOrbit orbit;
        private XRBaseInputInteractor[] localInteractors;

        /// <summary>
        /// True while the local player has a hand in the pot and a draw is actually available
        /// to them. The HUD reads this to prompt for the trigger press.
        /// </summary>
        public static bool LocalHandCanDraw { get; private set; }

        private void Awake()
        {
            orbit = GetComponentInParent<CauldronOrbit>();
        }

        private void OnDisable()
        {
            LocalHandCanDraw = false;
        }

        private void Update()
        {
            handsInside.RemoveWhere(hand => hand == null || !hand.gameObject.activeInHierarchy);

            LocalHandCanDraw = handsInside.Count > 0 && DrawAvailable();

            // A trigger press is the ONLY way to draw. Putting a hand in the pot merely offers
            // the draw — it never takes it. Drawing on contact fired constantly, because the
            // pot orbits onto the active player's hand and a dropped potion tripped it too.
            if (LocalHandCanDraw && TriggerPressedThisFrame())
            {
                Draw();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsLocalHand(other))
            {
                return;
            }

            handsInside.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            handsInside.Remove(other);
        }

        private void Draw()
        {
            nextDipTime = Time.time + dipCooldown;
            NetworkedSpellGame.Instance.RequestBrew();
        }

        // Everything that must be true before a draw is even offered to the player.
        private bool DrawAvailable()
        {
            if (Time.time < nextDipTime)
            {
                return false;
            }

            if (orbit != null && !orbit.IsSettled)
            {
                return false; // pot is still flying to us
            }

            // Casting and drawing are separate actions and must never happen in one motion.
            if (NetworkedPotion.LocallyHeldCount > 0)
            {
                return false;
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            return game != null && game.IsLocalPlayersTurn && game.CanBrew;
        }

        // Reads ACTIVATE, not select. Select is the grip (G in the XR Device Simulator) and is
        // what grabs a potion — driving the draw off it meant the grab button also drew. The
        // trigger (T in the simulator) maps to activate, which is nothing else's job here.
        // Only an interactor holding nothing counts, so a potion in hand can never confirm.
        private bool TriggerPressedThisFrame()
        {
            if (localInteractors == null || localInteractors.Length == 0)
            {
                localInteractors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None);
            }

            for (int i = 0; i < localInteractors.Length; i++)
            {
                XRBaseInputInteractor interactor = localInteractors[i];
                if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection)
                {
                    continue;
                }

                if (interactor.activateInput != null && interactor.activateInput.ReadWasPerformedThisFrame())
                {
                    return true;
                }
            }

            return false;
        }

        // The avatar hands are networked objects (Player/<hand>/HandCollider): the ones
        // WE own are the local player's, everyone else's are remote. Ownership is the
        // reliable local/remote test here (these colliders have no interactor and are not
        // under the XR Origin). Fall back to interactor / XR-Origin checks for setups
        // where the local hand is a plain (non-networked) collider.
        private bool IsLocalHand(Collider other)
        {
            // A potion is a locally-owned NetworkObject too, so the ownership test below used
            // to accept one as a "hand": dropping a potion into the cauldron drew a card.
            // Nothing that is a potion is ever a hand.
            if (other.GetComponentInParent<NetworkedPotion>() != null)
            {
                return false;
            }

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
