using UnityEngine;

public class BootMenuManager : MonoBehaviour
{
    [Header("Menu Groups")]
    [SerializeField] private GameObject mainMenuGroup;
    [SerializeField] private GameObject authGroup;
    [SerializeField] private GameObject settingsGroup;
    [SerializeField] private GameObject creditsGroup;

    private void Start()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        mainMenuGroup.SetActive(true);
        authGroup.SetActive(false);
        settingsGroup.SetActive(false);
        creditsGroup.SetActive(false);
    }

    public void ShowAuth()
    {
        mainMenuGroup.SetActive(false);
        authGroup.SetActive(true);
        settingsGroup.SetActive(false);
        creditsGroup.SetActive(false);
    }

    public void ShowSettings()
    {
        mainMenuGroup.SetActive(false);
        authGroup.SetActive(false);
        settingsGroup.SetActive(true);
        creditsGroup.SetActive(false);
    }

    public void ShowCredits()
    {
        mainMenuGroup.SetActive(false);
        authGroup.SetActive(false);
        settingsGroup.SetActive(false);
        creditsGroup.SetActive(true);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}