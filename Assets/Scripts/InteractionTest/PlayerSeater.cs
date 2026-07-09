using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Moves the local player's XR rig to a seat around the table once connected, so
    /// players are separated (and can actually see each other) instead of overlapping
    /// at the VR template's default spawn point far from the table.
    ///
    /// Seat is chosen from the player's turn-order index, so each connected player
    /// takes a different chair.
    /// </summary>
    public class PlayerSeater : MonoBehaviour
    {
        [Tooltip("Seat anchors around the table, in seating order. The local rig is placed at seats[myIndex].")]
        [SerializeField] private Transform[] seats;

        private bool seated;

        private void Update()
        {
            if (seated || seats == null || seats.Length == 0)
            {
                return;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient)
            {
                return;
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null)
            {
                return;
            }

            int index = game.GetSeatIndex(nm.LocalClientId);
            if (index < 0)
            {
                return; // wait until our seat is known so two players never share seat 0
            }
            index = Mathf.Clamp(index, 0, seats.Length - 1);

            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                return;
            }

            SeatRig(origin, seats[index]);
            seated = true;
        }

        // Place the XR rig floor at the seat. Temporarily disable the CharacterController
        // so it doesn't fight the teleport.
        private void SeatRig(XROrigin origin, Transform seat)
        {
            CharacterController controller = origin.GetComponent<CharacterController>();
            bool hadController = controller != null && controller.enabled;
            if (hadController)
            {
                controller.enabled = false;
            }

            origin.transform.SetPositionAndRotation(seat.position, seat.rotation);

            if (hadController)
            {
                controller.enabled = true;
            }
        }
    }
}
