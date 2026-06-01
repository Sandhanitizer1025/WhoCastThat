using UnityEngine;

public class BootSceneMovementLock : MonoBehaviour
{
    [SerializeField] private bool lockPosition = true;

    private Vector3 startPosition;

    private void Start()
    {
        startPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (lockPosition)
        {
            transform.position = startPosition;
        }
    }
}
