using UnityEngine.SceneManagement;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Sends the player back to OUR lobby after the tutorial.
    ///
    /// TutorialDriver's Back button hardcodes LoadScene("zelda") in a lambda, so leaving the
    /// tutorial always lands in the teammate's standalone lobby -- where the mirror buttons are
    /// still MagicMirrorMenu's Debug.Log stubs and the flow dead-ends. Their file cannot be
    /// edited without owning a merge conflict on a file that is still moving on main.
    ///
    /// So instead of changing where they send the player, we catch the arrival. This is armed
    /// ONLY when the tutorial was entered from our lobby, which means a teammate pressing Play
    /// on zelda.unity directly is never redirected and their scene behaves exactly as before.
    /// </summary>
    public static class TutorialReturnRedirect
    {
        const string TheirLobbyScene = "zelda";

        static string s_ReturnScene;

        public static void Arm(string returnScene)
        {
            s_ReturnScene = returnScene;
            SceneManager.sceneLoaded -= OnSceneLoaded;   // never subscribe twice
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void Disarm()
        {
            s_ReturnScene = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(s_ReturnScene) || scene.name != TheirLobbyScene)
            {
                return;
            }

            string target = s_ReturnScene;
            Disarm();                    // before loading, so the next load cannot re-trigger
            SceneManager.LoadScene(target);
        }
    }
}
