using UnityEngine;

namespace WhoCastThat.Tutorial
{
    /// <summary>
    /// Keeps the tutorial's world-space HUD in front of the player's head.
    ///
    /// Deliberately a *lazy* follow rather than a rigid head-lock: a panel welded to the head is
    /// uncomfortable to read in VR and is a reliable way to make people queasy, because it never
    /// holds still relative to the eye. This parks the panel in the world and only chases once the
    /// player has turned far enough that it would otherwise drift out of view.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialHudFollow : MonoBehaviour
    {
        [Tooltip("Metres in front of the head the panel sits at.")]
        public float distance = 2f;

        [Tooltip("Metres above (+) or below (-) eye height.")]
        public float heightOffset = -0.1f;

        [Tooltip("How quickly the panel catches up once it starts chasing.")]
        public float followSpeed = 3.5f;

        [Tooltip("Degrees the head may turn away before the panel starts following.")]
        public float reCentreAngle = 30f;

        [Tooltip("Keep the panel vertical instead of tipping back to point at the eye. A panel " +
                 "sitting above eye height has to pitch to face the head, which reads as a " +
                 "skewed, keystoned page — upright looks straight even when viewed from below.")]
        public bool keepUpright = true;

        Camera head;
        bool chasing;

        void LateUpdate()
        {
            if (!TryGetHead(out Vector3 headPos, out Vector3 flatForward)) return;

            // Yaw between where the player is looking and where the panel currently sits.
            Vector3 toPanel = Vector3.ProjectOnPlane(transform.position - headPos, Vector3.up);
            float drift = toPanel.sqrMagnitude < 0.0001f ? 180f : Vector3.Angle(flatForward, toPanel.normalized);

            // Hysteresis: start chasing past the threshold, stop only once basically centred, so the
            // panel cannot judder in and out of following at the boundary.
            if (drift > reCentreAngle) chasing = true;
            else if (drift < 3f) chasing = false;

            if (chasing)
            {
                Vector3 target = headPos + flatForward * distance + Vector3.up * heightOffset;
                transform.position = Vector3.Lerp(transform.position, target,
                    1f - Mathf.Exp(-followSpeed * Time.deltaTime));   // frame-rate independent
            }

            // Face the player even while parked, so the panel is never read edge-on.
            transform.rotation = FacingRotation(headPos);
        }

        /// <summary>Places the panel in view immediately, with no glide. Used when it first appears.</summary>
        public void SnapToView()
        {
            if (!TryGetHead(out Vector3 headPos, out Vector3 flatForward)) return;
            transform.position = headPos + flatForward * distance + Vector3.up * heightOffset;
            transform.rotation = FacingRotation(headPos);
            chasing = false;
        }

        /// <summary>
        /// Which way the panel should face. Upright mode flattens the look direction, so the panel
        /// only ever yaws — it never tips back — and the page reads square from below.
        /// </summary>
        Quaternion FacingRotation(Vector3 headPos)
        {
            Vector3 away = transform.position - headPos;
            if (keepUpright)
            {
                away = Vector3.ProjectOnPlane(away, Vector3.up);
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            }
            return Quaternion.LookRotation(away, Vector3.up);
        }

        bool TryGetHead(out Vector3 position, out Vector3 flatForward)
        {
            position = default;
            flatForward = Vector3.forward;

            if (head == null) head = Camera.main;
            if (head == null) return false;   // XR rig not up yet; try again next frame

            position = head.transform.position;
            flatForward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f) return false;   // looking straight up or down
            flatForward.Normalize();
            return true;
        }
    }
}
