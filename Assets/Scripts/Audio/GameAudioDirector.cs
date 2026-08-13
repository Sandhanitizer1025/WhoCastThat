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

            NetworkedSpellGame.SpellCastStarted += OnSpellCastStarted;
            NetworkedSpellGame.PlayerCursed += OnPlayerCursed;

            ApplyScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

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

        private void ApplyScene(string sceneName)
        {
            // The transition sting belongs to login -> lobby specifically. Leaving a match also
            // lands you in the lobby, and stinging that would make Leave Match sound like an
            // achievement.
            if (sceneName == LobbySceneName && previousScene == BootSceneName)
            {
                PlaySting(library.SceneTransitionSfx);
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
            float startVolume = music.volume;

            // Unscaled throughout: a fade is chrome, and must not stall if anything ever touches
            // Time.timeScale.
            if (music.isPlaying && startVolume > 0f)
            {
                for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
                {
                    music.volume = Mathf.Lerp(startVolume, 0f, t / FadeSeconds);
                    yield return null;
                }
            }

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
            PlaySting(library.CastSfx);
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
