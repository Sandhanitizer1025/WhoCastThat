using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhoCastThat.Interactions;

namespace WhoCastThat.Audio
{
    /// <summary>
    /// One object, alive for the whole session, that owns music and stings.
    ///
    /// Installs itself the way <c>InGameMenu</c> does — from a runtime hook, with no presence in
    /// any scene. That is deliberate: music that lives in a scene restarts every time you walk
    /// through a door, and three scenes each holding their own AudioSource is three places to
    /// forget when the volume rules change.
    ///
    /// BootScene is left alone entirely. <c>BootAudioManager</c> already plays the menu theme
    /// there, and two directors fading against each other is a bug, not a mix.
    /// </summary>
    public class GameAudioDirector : MonoBehaviour
    {
        private const string LibraryResourceName = "GameAudioLibrary";
        private const string BootSceneName = "BootScene";
        private const string LobbySceneName = "LobbyMirrorScene";

        private const float FadeSeconds = 0.6f;

        // The player can move a volume slider at any time. Polling PlayerPrefs on a slow tick is
        // how this stays in step without editing GameAudioSettings, which is a teammate's file:
        // its SetMusic only pushes to BootAudioManager, which does not exist outside BootScene.
        private const float VolumePollSeconds = 0.25f;

        private static GameAudioDirector instance;

        /// <summary>
        /// Raised when the Boot -> Lobby sting starts, carrying its length in seconds. The loading
        /// screen holds for exactly this long, so the two are driven by one clip rather than by a
        /// duration written down in two places that can drift apart.
        /// </summary>
        public static event System.Action<float> TransitionStingerStarted;

        private GameAudioLibrary library;
        private AudioSource music;
        private AudioSource sfx;

        private string currentTrackScene;
        private float currentTrim = 1f;
        private float nextVolumePoll;
        private Coroutine fadeRoutine;

        // What the previous scene was, so arriving in the lobby can tell a fresh login from a
        // player who just left a match -- only the first gets the transition sting.
        private string previousScene;

        // The track cut short by a scene change, and where it had got to. Remembered ONLY so that
        // re-entering a scene that uses the same track can pick it up rather than snapping back to
        // bar one -- the behaviour the fade below was written to protect.
        private AudioClip stoppedClip;
        private float stoppedTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var go = new GameObject("GameAudioDirector");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudioDirector>();
        }

        private void Awake()
        {
            library = Resources.Load<GameAudioLibrary>(LibraryResourceName);
            if (library == null)
            {
                // Loud on purpose. Silent audio is the single hardest kind of missing asset to
                // notice, and the failure is a file in the wrong folder, not a code bug.
                Debug.LogWarning($"[GameAudio] No {LibraryResourceName} in a Resources folder — " +
                                 "the game will run silent. Expected " +
                                 $"Assets/Resources/{LibraryResourceName}.asset");
                enabled = false;
                return;
            }

            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f; // 2D: music has no position in the room

            sfx = gameObject.AddComponent<AudioSource>();
            sfx.loop = false;
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            NetworkedSpellGame.SpellCastStarted += OnSpellCastStarted;
            NetworkedSpellGame.PlayerCursed += OnPlayerCursed;

            ApplyScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            NetworkedSpellGame.SpellCastStarted -= OnSpellCastStarted;
            NetworkedSpellGame.PlayerCursed -= OnPlayerCursed;

            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextVolumePoll)
            {
                return;
            }

            nextVolumePoll = Time.unscaledTime + VolumePollSeconds;

            // Skipped mid-fade: the fade owns the volume until it is finished, and fighting it
            // here would make every scene change stutter.
            if (fadeRoutine == null && music != null && music.isPlaying)
            {
                music.volume = GameAudioSettings.Music * currentTrim;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            ApplyScene(scene.name);
        }

        /// <summary>
        /// Cuts the outgoing scene's music at the scene boundary.
        ///
        /// Without this the old track outlives the scene it belongs to. This object is
        /// DontDestroyOnLoad, so its fade coroutine is not bound to the scene at all: sceneUnloaded
        /// fires, the new scene loads, and only THEN does OnSceneLoaded start a fade-out that takes
        /// another <see cref="FadeSeconds"/> to finish. The lobby's music is therefore still audible
        /// well into the tutorial, over the top of the tutorial's own opening narration.
        ///
        /// sceneUnloaded is the right hook because it fires BEFORE the next scene appears, so the
        /// old track stops with the scene that owned it rather than bleeding past it.
        /// </summary>
        private void OnSceneUnloaded(Scene scene)
        {
            if (music == null)
            {
                return;
            }

            // The fade owns the volume; leaving it running would have it write to a stopped source
            // and then fade in a track this method has just decided should not be playing.
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            if (music.isPlaying)
            {
                stoppedClip = music.clip;
                stoppedTime = music.time;
                music.Stop();
            }

            music.volume = 0f;
        }

        private void ApplyScene(string sceneName)
        {
            // The transition sting belongs to login -> lobby specifically. Leaving a match also
            // lands you in the lobby, and stinging that would make Leave Match sound like an
            // achievement.
            if (sceneName == LobbySceneName && previousScene == BootSceneName)
            {
                PlaySting(library.SceneTransitionSfx);

                // Raised even when the clip is missing, with 0 — the loading screen then knows to
                // use its own fallback rather than hanging on a stinger that never arrives.
                float length = library.SceneTransitionSfx != null ? library.SceneTransitionSfx.length : 0f;
                TransitionStingerStarted?.Invoke(length);
            }

            previousScene = sceneName;

            // BootAudioManager owns BootScene. Stop rather than ignore, so coming BACK to boot
            // does not leave lobby music playing underneath the menu theme.
            if (sceneName == BootSceneName)
            {
                StartFade(null, 1f, sceneName);
                return;
            }

            if (!library.TryGetTrack(sceneName, out AudioClip clip, out float trim))
            {
                StartFade(null, 1f, sceneName);
                return;
            }

            // Re-entering a scene that uses the track already playing must not restart it. A
            // hard cut back to bar one is the tell that music is scene-owned, and it is exactly
            // what this class exists to avoid.
            if (music.isPlaying && music.clip == clip)
            {
                currentTrackScene = sceneName;
                currentTrim = trim;
                return;
            }

            StartFade(clip, trim, sceneName);
        }

        private void StartFade(AudioClip clip, float trim, string sceneName)
        {
            currentTrackScene = sceneName;

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }

            fadeRoutine = StartCoroutine(FadeTo(clip, trim));
        }

        private IEnumerator FadeTo(AudioClip clip, float trim)
        {
            // NO FADE-OUT. This runs from ApplyScene, which is only ever reached from Awake or a
            // scene load, so anything still playing here belongs to a scene the player has already
            // left. Fading it took FadeSeconds of the NEW scene, which is what "the lobby music
            // carries on into the tutorial" actually was.
            //
            // OnSceneUnloaded normally stops it a moment earlier, before the new scene even
            // appears. This is the backstop for any path where that does not fire -- the two
            // together mean no route into a scene can bring the previous scene's music with it.
            // The fade-IN below is kept: arriving music rising from silence is not a bleed.
            music.Stop();
            currentTrim = trim;

            if (clip == null)
            {
                music.clip = null;
                music.volume = 0f;
                fadeRoutine = null;
                yield break;
            }

            music.clip = clip;
            music.volume = 0f;
            music.Play();

            // Re-entering a scene whose track was cut by the scene change picks it up where it
            // stopped, rather than snapping back to bar one -- a hard cut is the tell that music is
            // scene-owned, which is what this class exists to avoid. Set AFTER Play(): assigning
            // time to a source that has not started is not reliable for compressed clips.
            if (clip == stoppedClip && stoppedTime > 0f && stoppedTime < clip.length)
            {
                music.time = stoppedTime;
            }

            stoppedClip = null;
            stoppedTime = 0f;

            float target = GameAudioSettings.Music * currentTrim;
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                // Target is re-read each frame so a slider moved mid-fade still lands correctly.
                target = GameAudioSettings.Music * currentTrim;
                music.volume = Mathf.Lerp(0f, target, t / FadeSeconds);
                yield return null;
            }

            music.volume = target;
            fadeRoutine = null;
        }

        private void PlaySting(AudioClip clip)
        {
            if (clip == null || sfx == null)
            {
                return;
            }

            sfx.PlayOneShot(clip, GameAudioSettings.Sfx);
        }

        // windowSeconds is deliberately ignored: it is 0 whenever nobody holds a Dispel, so it
        // cannot be used to time anything, and the cast sound belongs on the cast either way.
        private void OnSpellCastStarted(PotionType type, ulong casterId, float windowSeconds)
        {
            // Per-spell sound where one exists, generic cast otherwise. Heard by everyone: which
            // spell was cast is already public — the HUD announces it by name — so this discloses
            // nothing. What Foresight actually SHOWS the caster stays private.
            PlaySting(library.CastSfxFor(type));
        }

        // Heard by everyone. Curse status already replicates to every client (cursedNetwork is
        // read-Everyone) and the HUD says so out loud, so this discloses nothing that is not
        // already on screen.
        private void OnPlayerCursed(ulong playerId)
        {
            PlaySting(library.CursedSfx);
        }
    }
}
