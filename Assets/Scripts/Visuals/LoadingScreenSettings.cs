using UnityEngine;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// Art and timing for the Boot -> Lobby loading screen.
    ///
    /// A ScriptableObject in Resources for the same reason the audio and prop libraries are: the
    /// screen installs itself from a runtime hook and lives in no scene, so it has no inspector
    /// slot anyone could drop a sprite into.
    ///
    /// Must live at Assets/Resources/LoadingScreen.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Who Cast That/Loading Screen", fileName = "LoadingScreen")]
    public class LoadingScreenSettings : ScriptableObject
    {
        // A Texture rather than a Sprite, deliberately. Neither logo in the project yields a usable
        // Sprite: WhoCastThat_logo is imported as a plain texture, and WhoCastThat_logo_sprite is
        // set to Multiple with no slices defined, so it generates no sprite sub-asset at all.
        // RawImage takes the texture as-is and does not depend on import settings that could be
        // changed by someone re-importing the art.
        [SerializeField] private Texture logo;

        [Tooltip("Used only if the transition sting is missing, so the screen still clears itself.")]
        [SerializeField] private float fallbackSeconds = 2.5f;

        [Tooltip("How long the black takes to lift once the hold is over.")]
        [SerializeField] private float fadeOutSeconds = 0.6f;

        [Header("Logo motion")]
        [Tooltip("Degrees per second. This is the loading cue, so it should read as continuous.")]
        [SerializeField] private float spinDegreesPerSecond = 90f;

        [Tooltip("Metres the logo drifts up and down.")]
        [SerializeField] private float floatAmplitude = 0.06f;

        [SerializeField] private float floatDegreesPerSecond = 70f;

        [Header("Placement")]
        [Tooltip("Metres in front of the eyes. Close enough to fill the view, far enough to focus.")]
        [SerializeField] private float distance = 1.0f;

        [Tooltip("Logo width in metres.")]
        [SerializeField] private float logoSize = 0.72f;

        public Texture Logo => logo;
        public float FallbackSeconds => fallbackSeconds;
        public float FadeOutSeconds => fadeOutSeconds;
        public float SpinDegreesPerSecond => spinDegreesPerSecond;
        public float FloatAmplitude => floatAmplitude;
        public float FloatDegreesPerSecond => floatDegreesPerSecond;
        public float Distance => distance;
        public float LogoSize => logoSize;
    }
}
