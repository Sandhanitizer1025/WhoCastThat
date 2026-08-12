using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Makes world-space UI (the magic-mirror menu) pressable by both XR controllers.
///
/// XR interactors only ever drive UI through XRUIInputModule. When a scene ships an EventSystem
/// carrying InputSystemUIInputModule instead, XRI silently adds its own XRUIInputModule alongside
/// it, but the EventSystem activates the module that was already there — so the XR one never
/// becomes currentInputModule and its Process() never runs. Rays still hover, presses do nothing.
/// XRI's own cleanup only knows how to remove the older StandaloneInputModule, so it never
/// notices the conflict.
///
/// LobbyMirrorScene inherited exactly that EventSystem when it was cloned from zelda.unity.
/// This runs for every loaded scene, so a scene copied from a broken one is covered too, and no
/// teammate's scene has to be hand-edited to get the fix.
/// </summary>
public static class XRUIInputModuleGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    static void Apply()
    {
        foreach (var eventSystem in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            // Nothing to arbitrate unless a screen-space module is live to win the race.
            if (!eventSystem.TryGetComponent<InputSystemUIInputModule>(out var screenModule) || !screenModule.enabled)
                continue;

            // XRI normally adds this from an interactor's OnEnable. Add it here in case no
            // interactor has enabled yet, so the EventSystem is never left with no module at all.
            if (!eventSystem.TryGetComponent<XRUIInputModule>(out _))
                eventSystem.gameObject.AddComponent<XRUIInputModule>();

            // Disabled rather than destroyed: the EventSystem skips inactive modules when choosing
            // the current one, and XRUIInputModule handles mouse and touch itself, so the XR
            // simulator and any screen-space UI keep working.
            screenModule.enabled = false;

            Debug.Log($"[XRUIInputModuleGuard] '{eventSystem.name}' carried an InputSystemUIInputModule, " +
                      "which blocks XR controller UI presses. Disabled it and ensured XRUIInputModule is active.",
                      eventSystem);
        }
    }
}
