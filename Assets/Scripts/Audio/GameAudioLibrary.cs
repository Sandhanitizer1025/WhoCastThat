using System;
using UnityEngine;

namespace WhoCastThat.Audio
{
    /// <summary>
    /// Every clip the game plays outside BootScene's own menu audio, in one asset.
    ///
    /// A ScriptableObject rather than serialized fields on a scene object, for the reason
    /// written into the architecture notes: fields wired per-scene are how the lobby's settings
    /// panel ended up as a column of nulls in the scene YAML. One asset in Resources is wired
    /// once and every scene gets it, including scenes nobody has opened since.
    ///
    /// Create via Assets > Create > Who Cast That > Game Audio Library, and it MUST live at
    /// Assets/Resources/GameAudioLibrary.asset — <see cref="GameAudioDirector"/> loads it by that
    /// name with no scene reference to follow.
    /// </summary>
    [CreateAssetMenu(menuName = "Who Cast That/Game Audio Library", fileName = "GameAudioLibrary")]
    public class GameAudioLibrary : ScriptableObject
    {
        /// <summary>The track for one scene. Scene names are data, not code, so adding a scene
        /// later is an inspector edit rather than a rebuild.</summary>
        [Serializable]
        public struct SceneTrack
        {
            public string sceneName;
            public AudioClip clip;

            [Range(0f, 1f)]
            [Tooltip("Per-track trim, on top of the player's music volume. Some mixes are hotter " +
                     "than others and the player should not have to re-balance at every scene.")]
            public float trim;
        }

        [Header("Music")]
        [SerializeField] private SceneTrack[] sceneTracks = Array.Empty<SceneTrack>();

        [Header("Stings")]
        [Tooltip("A spell has been cast onto the table.")]
        [SerializeField] private AudioClip castSfx;

        [Tooltip("A player has just been cursed.")]
        [SerializeField] private AudioClip cursedSfx;

        [Tooltip("Played on arrival in the lobby, coming from BootScene.")]
        [SerializeField] private AudioClip sceneTransitionSfx;

        public AudioClip CastSfx => castSfx;
        public AudioClip CursedSfx => cursedSfx;
        public AudioClip SceneTransitionSfx => sceneTransitionSfx;

        /// <summary>
        /// The track for a scene, or a null clip if that scene is meant to be quiet. Returns the
        /// trim alongside it because a caller that had to look the track up twice would be one
        /// edit away from applying the wrong scene's trim.
        /// </summary>
        public bool TryGetTrack(string sceneName, out AudioClip clip, out float trim)
        {
            clip = null;
            trim = 1f;

            if (sceneTracks == null)
            {
                return false;
            }

            for (int i = 0; i < sceneTracks.Length; i++)
            {
                if (!string.Equals(sceneTracks[i].sceneName, sceneName, StringComparison.Ordinal))
                {
                    continue;
                }

                clip = sceneTracks[i].clip;

                // A trim left at 0 is far more likely to be an un-edited default than a
                // deliberate silence -- a track meant to be silent is authored by leaving the
                // clip empty, not by zeroing the volume.
                trim = sceneTracks[i].trim <= 0f ? 1f : sceneTracks[i].trim;
                return clip != null;
            }

            return false;
        }
    }
}
