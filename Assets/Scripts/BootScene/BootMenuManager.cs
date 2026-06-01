using UnityEngine;
using UnityEngine.SceneManagement;

public class BootMenuManager : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string lobbySceneName = "LobbyScene";

    [Header("Menu Groups")]
    [SerializeField] private GameObject mainMenuGroup;
    [SerializeField] private GameObject settingsGroup;
    [SerializeField] private GameObject creditsGroup;

    private void Start()
    {
        ShowMainMenu();
    }

    public void PlayGame()
    {
        SceneManager.LoadScene(lobbySceneName);
    }

    public void ShowMainMenu()
    {
        mainMenuGroup.SetActive(true);
        settingsGroup.SetActive(false);
        creditsGroup.SetActive(false);
    }

    public void ShowSettings()
    {
        mainMenuGroup.SetActive(false);
        settingsGroup.SetActive(true);
        creditsGroup.SetActive(false);
    }

    public void ShowCredits()
    {
        mainMenuGroup.SetActive(false);
        settingsGroup.SetActive(false);
        creditsGroup.SetActive(true);
    }

    public void QuitGame()
    {
        Debug.Log("Quit Game");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
