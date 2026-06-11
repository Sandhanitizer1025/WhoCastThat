using UnityEngine;
using UnityEngine.UI;

public class GameSettingsManager : MonoBehaviour
{
    [Header("Options UI")]
    [SerializeField] private Toggle snapTurnToggle;

    private const string SnapTurnKey = "SnapTurnEnabled";

    public static bool SnapTurnEnabled
    {
        get
        {
            return PlayerPrefs.GetInt(SnapTurnKey, 1) == 1;
        }
    }

    private void Awake()
    {
        bool savedSnapTurn = PlayerPrefs.GetInt(SnapTurnKey, 1) == 1;

        if (snapTurnToggle != null)
        {
            snapTurnToggle.isOn = savedSnapTurn;
            snapTurnToggle.onValueChanged.AddListener(SetSnapTurn);
        }
    }

    public void SetSnapTurn(bool enabled)
    {
        PlayerPrefs.SetInt(SnapTurnKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log("Snap Turn Enabled: " + enabled);
    }
}