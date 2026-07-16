using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Billboards this object to the local camera each frame so a shared world-space
    /// HUD stays readable from any seated player's viewpoint.
    /// </summary>
    public class FaceLocalCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // Align to the camera's orientation so text faces the viewer un-mirrored.
            transform.LookAt(
                transform.position + cam.transform.rotation * Vector3.forward,
                cam.transform.rotation * Vector3.up);
        }
    }
}
