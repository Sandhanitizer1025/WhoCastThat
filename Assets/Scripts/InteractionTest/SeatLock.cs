using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Keeps a player at their seat while a round is running.
    ///
    /// Once <see cref="NetworkedSpellGame.GameActive"/> goes true this re-seats the local
    /// rig, switches off its artificial locomotion (move / teleport / climb / grab-move)
    /// and then holds physical room-scale walking inside <see cref="leashRadius"/> of the
    /// seat. Turning is deliberately left enabled: turning in place is comfortable and
    /// players still need to look around the table.
    ///
    /// Providers are located by type name rather than by inspector reference, so this
    /// needs no wiring and survives the namespace moves XRI makes between versions.
    /// Put it on the same object as <see cref="PlayerSeater"/>.
    /// </summary>
    public class SeatLock : MonoBehaviour
    {
        [Tooltip("Seat anchors in turn order - the same array PlayerSeater uses.")]
        [SerializeField] private Transform[] seats;

        [Tooltip("How far the headset may drift from its seat before it is held back.")]
        [SerializeField] private float leashRadius = 0.6f;

        [Tooltip("Also hold back physical room-scale walking, not just stick/teleport movement.")]
        [SerializeField] private bool clampPhysicalWalking = true;

        // Locomotion switched off for the duration of a round. Turn providers are
        // deliberately absent from this list.
        private static readonly string[] LockedProviders =
        {
            "DynamicMoveProvider",
            "ContinuousMoveProvider",
            "TeleportationProvider",
            "ClimbProvider",
            "GrabMoveProvider",
            "TwoHandedGrabMoveProvider",
        };

        private readonly List<MonoBehaviour> disabled = new List<MonoBehaviour>();
        private XROrigin origin;
        private Transform seat;
        private bool locked;

        private void Update()
        {
            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            bool shouldLock = game != null && game.GameActive;

            if (shouldLock && !locked)
            {
                Lock(game);
            }
            else if (!shouldLock && locked)
            {
                Unlock();
            }

            if (locked && clampPhysicalWalking)
            {
                HoldAtSeat();
            }
        }

        private void Lock(NetworkedSpellGame game)
        {
            if (!ResolveRig(game))
            {
                return; // our seat isn't known yet - try again next frame
            }

            disabled.Clear();
            MonoBehaviour[] all = origin.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour behaviour = all[i];
                if (behaviour == null || !behaviour.enabled)
                {
                    continue; // never re-enable something that was already off
                }

                string type = behaviour.GetType().Name;
                for (int j = 0; j < LockedProviders.Length; j++)
                {
                    if (type == LockedProviders[j])
                    {
                        behaviour.enabled = false;
                        disabled.Add(behaviour);
                        break;
                    }
                }
            }

            Teleport(seat.position);
            locked = true;
        }

        private void Unlock()
        {
            for (int i = 0; i < disabled.Count; i++)
            {
                if (disabled[i] != null)
                {
                    disabled[i].enabled = true;
                }
            }

            disabled.Clear();
            locked = false;
        }

        private bool ResolveRig(NetworkedSpellGame game)
        {
            if (seats == null || seats.Length == 0)
            {
                return false;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsConnectedClient)
            {
                return false;
            }

            int index = game.GetSeatIndex(manager.LocalClientId);
            if (index < 0)
            {
                return false;
            }

            seat = seats[Mathf.Clamp(index, 0, seats.Length - 1)];

            if (origin == null)
            {
                origin = FindAnyObjectByType<XROrigin>();
            }

            return origin != null && seat != null;
        }

        // Room-scale walking moves the camera inside the rig, so the rig itself is
        // counter-moved by however far the head strayed past the leash. Reads as a
        // soft wall rather than a snap, which keeps it comfortable.
        private void HoldAtSeat()
        {
            if (origin == null || seat == null || origin.Camera == null)
            {
                return;
            }

            Vector3 head = origin.Camera.transform.position;
            Vector3 offset = new Vector3(head.x - seat.position.x, 0f, head.z - seat.position.z);
            float distance = offset.magnitude;
            if (distance <= leashRadius)
            {
                return;
            }

            Teleport(origin.transform.position - offset.normalized * (distance - leashRadius));
        }

        // Same trick PlayerSeater uses: the CharacterController fights direct writes to
        // the rig transform, so switch it off for the move.
        private void Teleport(Vector3 position)
        {
            CharacterController controller = origin.GetComponent<CharacterController>();
            bool hadController = controller != null && controller.enabled;
            if (hadController)
            {
                controller.enabled = false;
            }

            origin.transform.position = position;

            if (hadController)
            {
                controller.enabled = true;
            }
        }
    }
}
