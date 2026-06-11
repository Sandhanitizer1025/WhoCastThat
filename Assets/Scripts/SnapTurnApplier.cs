using UnityEngine;

public class SnapTurnApplier : MonoBehaviour
{
    [Header("Turn Provider Objects")]
    [SerializeField] private GameObject snapTurnObject;
    [SerializeField] private GameObject continuousTurnObject;

    private void Start()
    {
        ApplySnapTurnSetting();
    }

    public void ApplySnapTurnSetting()
    {
        bool snapTurnEnabled = GameSettingsManager.SnapTurnEnabled;

        if (snapTurnObject != null)
        {
            snapTurnObject.SetActive(snapTurnEnabled);
        }

        if (continuousTurnObject != null)
        {
            continuousTurnObject.SetActive(!snapTurnEnabled);
        }

        Debug.Log("Applied Snap Turn Setting: " + snapTurnEnabled);
    }
}
