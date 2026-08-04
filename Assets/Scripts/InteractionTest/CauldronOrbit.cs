using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Floats the cauldron rig to an anchor in front of whoever's turn it is, so the
    /// pot "orbits" the table following the active player.
    ///
    /// Deterministic on purpose: it reads only the already-replicated turn index
    /// (via <see cref="NetworkedSpellGame.CurrentSeatIndex"/>) and moves a plain
    /// (non-networked) rig, so every client runs the identical motion with no extra
    /// NetworkObject/NetworkTransform to keep in sync. Attach to the CauldronRig.
    /// </summary>
    public class CauldronOrbit : MonoBehaviour
    {
        [Tooltip("Centre of the table; the pot rests 'radius' metres from here toward the active seat.")]
        [SerializeField] private Transform tableCenter;

        [Tooltip("Seat anchors in turn order (same array PlayerSeater uses). The active seat picks the pot direction.")]
        [SerializeField] private Transform[] seats;

        [Tooltip("How far in front of the active player the pot floats, measured from the table centre.")]
        [SerializeField] private float radius = 0.45f;

        [Tooltip("Swing the pot this many degrees to one side of the active player, instead of " +
                 "parking it dead in front of them. 0 = straight ahead, which hides the play zone.")]
        [SerializeField] private float sideOffsetDegrees = 30f;

        [Tooltip("Higher = the pot snaps to the new player faster.")]
        [SerializeField] private float followSpeed = 2.5f;

        [Tooltip("Gentle vertical bob so the pot reads as magically floating.")]
        [SerializeField] private float bobAmplitude = 0.015f;
        [SerializeField] private float bobSpeed = 1.5f;

        // How close to the anchor counts as "arrived".
        private const float SettleDistance = 0.05f;

        private float baseHeight;
        private Vector3 targetXZ;
        private bool hasTarget;

        /// <summary>
        /// True once the pot has finished floating to the active player's side. Callers
        /// use this to avoid reacting while the pot is still in transit — most importantly
        /// <see cref="StirZone"/>, which must not read the pot sliding over a resting hand
        /// as a deliberate dip.
        /// </summary>
        public bool IsSettled
        {
            get
            {
                if (!hasTarget)
                {
                    return true;
                }

                Vector3 position = transform.position;
                float dx = position.x - targetXZ.x;
                float dz = position.z - targetXZ.z;
                return dx * dx + dz * dz < SettleDistance * SettleDistance;
            }
        }

        private void Start()
        {
            baseHeight = transform.position.y;
            targetXZ = transform.position;
        }

        private void Update()
        {
            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game != null && tableCenter != null && seats != null && seats.Length > 0)
            {
                int seat = game.CurrentSeatIndex;
                if (seat >= 0 && seat < seats.Length && seats[seat] != null)
                {
                    Vector3 c = tableCenter.position;
                    Vector3 s = seats[seat].position;
                    Vector3 dir = new Vector3(s.x - c.x, 0f, s.z - c.z);
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        dir.Normalize();
                    }

                    // Parked straight in front, the pot sits squarely on the player's line of
                    // sight to the middle of the table and hides the play zone almost entirely
                    // — you cannot see where to drop a potion to cast it. Swinging it to one
                    // side keeps it plainly "yours" and within reach while clearing the view.
                    if (Mathf.Abs(sideOffsetDegrees) > 0.01f)
                    {
                        dir = Quaternion.Euler(0f, sideOffsetDegrees, 0f) * dir;
                    }

                    targetXZ = new Vector3(c.x + dir.x * radius, baseHeight, c.z + dir.z * radius);
                    hasTarget = true;
                }
            }

            Vector3 pos = transform.position;
            Vector3 goal = hasTarget ? targetXZ : new Vector3(pos.x, baseHeight, pos.z);

            // Frame-rate independent ease toward the goal, plus a floating bob.
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            Vector3 next = Vector3.Lerp(pos, goal, t);
            next.y = baseHeight + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            transform.position = next;
        }
    }
}
