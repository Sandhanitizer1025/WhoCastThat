using UnityEngine;

/// <summary>
/// Keeps a world-space UI group comfortably in front of the player's camera.
/// Uses a smoothed "lazy" follow and ignores head pitch/roll (panel stays
/// upright at a fixed height) to avoid VR motion discomfort.
/// </summary>
public class CameraFollowUI : MonoBehaviour
{
    [Tooltip("Camera to follow. Leave empty to use Camera.main at runtime.")]
    public Transform target;

    [Tooltip("Distance in front of the camera (metres).")]
    public float distance = 1.8f;

    [Tooltip("Vertical offset from the camera height (metres).")]
    public float heightOffset = -0.1f;

    [Tooltip("How quickly the panel eases to its target position.")]
    public float followSpeed = 4f;

    [Tooltip("How quickly the panel eases to face the player.")]
    public float rotateSpeed = 4f;

    [Tooltip("Keep the panel upright and level regardless of where the player looks.")]
    public bool ignorePitch = true;

    Transform cam;

    void OnEnable()
    {
        ResolveCamera();
        SnapToTarget();
    }

    void ResolveCamera()
    {
        if (target != null) { cam = target; return; }
        var main = Camera.main;
        if (main != null) cam = main.transform;
    }

    void LateUpdate()
    {
        if (cam == null) { ResolveCamera(); if (cam == null) return; }

        Vector3 fwd = cam.forward;
        if (ignorePitch)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
            fwd.Normalize();
        }

        Vector3 targetPos = cam.position + fwd * distance + Vector3.up * heightOffset;
        Quaternion targetRot = Quaternion.LookRotation(fwd, Vector3.up);

        float tp = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        float tr = 1f - Mathf.Exp(-rotateSpeed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, tp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, tr);
    }

    void SnapToTarget()
    {
        if (cam == null) return;
        Vector3 fwd = cam.forward;
        if (ignorePitch) { fwd.y = 0f; if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward; fwd.Normalize(); }
        transform.position = cam.position + fwd * distance + Vector3.up * heightOffset;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }
}
