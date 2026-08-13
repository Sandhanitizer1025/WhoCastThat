using System.Collections;
using System.Reflection;
using Unity.VRTemplate;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns off the XR template's controller tooltips — the little labelled callouts that fade in
/// over the controllers when you look down at them.
///
/// ATTACHES rather than edits, which is the project's standing rule for someone else's code. The
/// callouts live on <c>XR Origin Hands (XR Rig) MP Template Variant.prefab</c> inside VRMPAssets,
/// which is template content we do not own; ten <see cref="Callout"/> components are spread across
/// that rig and it is a prefab INSTANCE in all three scenes, so switching them off per-scene would
/// mean three sets of prefab overrides to keep in step — including two in scenes that are not ours.
///
/// Uses the callout's own <c>m_UseGazeCallout</c> switch instead of deactivating GameObjects. That
/// is the difference between off and hidden: with the flag cleared, <c>Callout.Start()</c> takes
/// its early-out branch, so the tooltip is never unparented to the scene root in the first place
/// and every later gaze disables it rather than showing it. Hiding the objects instead would leave
/// orphaned tooltips at the root that the gaze controller keeps trying to re-enable.
/// </summary>
public class ControllerTooltipSuppressor : MonoBehaviour
{
    private const string UseGazeCalloutField = "m_UseGazeCallout";

    // The local rig is present from the moment the scene loads, but a rig can also be spawned a
    // beat later. Re-sweeping for a few seconds is cheaper than a permanent Update, and the sweep
    // is idempotent — a callout already switched off costs one field write.
    private const float SweepSeconds = 4f;
    private const float SweepInterval = 0.5f;

    private static ControllerTooltipSuppressor instance;
    private static FieldInfo useGazeCallout;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null)
        {
            return;
        }

        // AfterSceneLoad runs once Awake has been called on everything in the scene but before the
        // first Start, which is the window where clearing the flag still prevents the unparenting.
        useGazeCallout = typeof(Callout).GetField(
            UseGazeCalloutField, BindingFlags.Instance | BindingFlags.NonPublic);

        if (useGazeCallout == null)
        {
            // Loud, because the failure is otherwise invisible: the tooltips would simply carry on
            // appearing with nothing to say why. A rename in the template is the likely cause.
            Debug.LogWarning("[Tooltips] Callout." + UseGazeCalloutField + " not found — the " +
                             "controller tooltips cannot be suppressed. Has VRMPAssets changed?");
            return;
        }

        var go = new GameObject("ControllerTooltipSuppressor");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ControllerTooltipSuppressor>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(SweepFor(SweepSeconds));
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive)
        {
            return;
        }

        StopAllCoroutines();
        StartCoroutine(SweepFor(SweepSeconds));
    }

    private IEnumerator SweepFor(float seconds)
    {
        Suppress();

        for (float t = 0f; t < seconds; t += SweepInterval)
        {
            yield return new WaitForSeconds(SweepInterval);
            Suppress();
        }
    }

    private static void Suppress()
    {
        // Inactive ones too: a rig can be switched on later, and a callout that was missed while
        // disabled would come back with its tooltip intact.
        Callout[] callouts = FindObjectsByType<Callout>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < callouts.Length; i++)
        {
            Callout callout = callouts[i];
            if (callout == null)
            {
                continue;
            }

            useGazeCallout.SetValue(callout, false);

            // Clears anything already on screen. With the flag now false this takes Callout's
            // "not using gaze callouts" branch, which stops its coroutines and hides the tooltip
            // and its curve outright.
            callout.GazeHoverEnd();
        }
    }
}
