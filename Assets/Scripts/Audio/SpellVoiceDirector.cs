using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhoCastThat.Interactions;

namespace WhoCastThat.Audio
{
    /// <summary>
    /// Plays the wizard narration off the rules, without the rules knowing it exists.
    ///
    /// Installs itself from a runtime hook exactly as <see cref="GameAudioDirector"/> does, so it
    /// is present in InteractionTestScene and TutorialScene alike with nothing wired in either
    /// scene. A voice component dropped into one scene is a component missing from the other, and
    /// a serialized clip list per scene is the "column of nulls" failure the audio library was
    /// built to avoid.
    ///
    /// Which event carries which line is not obvious, so it is written down here:
    ///
    ///   SpellCastStarted   the five normally-cast spells (Hex, Tribute, Foresight, Warp, Phase).
    ///   SpellFizzled       the "dispel (x)" lines. The event carries the DISPELLED spell's type,
    ///                      which is precisely the x in the filename.
    ///   SpellResolved      the "reflection (x)" lines. A Reflection raises this TWICE: once as
    ///                      Reflection, then immediately again as the spell it copied. See
    ///                      <see cref="OnSpellResolved"/>.
    ///   PlayerCursed       the Curse line.
    ///   (polled)           the Counterspell line — see <see cref="PollCounterspell"/>. There is
    ///                      no event for it.
    /// </summary>
    public class SpellVoiceDirector : MonoBehaviour
    {
        private const string LibraryResourceName = "SpellVoiceLibrary";

        // Narration is seconds long, so two lines landing together talk over each other. One extra
        // line waits; a third is dropped rather than queued, because a backlog of commentary about
        // a spell three turns ago is worse than missing it.
        private const int MaxQueued = 1;

        private const float PollSeconds = 0.1f;

        private static SpellVoiceDirector instance;

        private SpellVoiceLibrary library;
        private AudioSource voice;

        private readonly Queue<AudioClip> pending = new Queue<AudioClip>();

        private bool subscribed;

        // The events are static and NetworkedSpellGame nulls all of them in OnNetworkDespawn, so a
        // subscription does not survive a match ending. Tracking the instance is how we notice a
        // fresh game object and re-arm; without it the second match of a session is silent.
        private NetworkedSpellGame lastGame;

        private float nextPoll;

        // Counterspell has no event, so it is inferred from the local player's curse flag clearing.
        private bool wasLocalCursed;

        // RemoveCurse is also called when a player is eliminated or drops. Elimination is the one
        // that overlaps with answering a Curse (play the wrong potion while cursed and you are
        // destroyed), and it must not be narrated as a successful Counterspell.
        private bool localEliminated;

        // True between a Reflection resolving and the spell it copied resolving.
        private bool awaitingReflectionCopy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var go = new GameObject("SpellVoiceDirector");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SpellVoiceDirector>();
        }

        private void Awake()
        {
            library = Resources.Load<SpellVoiceLibrary>(LibraryResourceName);
            if (library == null)
            {
                // Loud on purpose, for the same reason GameAudioDirector is: a voice line that
                // never plays looks identical to one nobody recorded, and the usual cause is a
                // file outside Resources rather than a bug in here.
                Debug.LogWarning($"[SpellVoice] No {LibraryResourceName} in a Resources folder — " +
                                 "the wizard stays silent. Expected " +
                                 $"Assets/Resources/{LibraryResourceName}.asset");
                enabled = false;
                return;
            }

            voice = gameObject.AddComponent<AudioSource>();
            voice.loop = false;
            voice.playOnAwake = false;
            voice.spatialBlend = 0f; // 2D: narration is not coming from a point in the room

            SceneManager.sceneLoaded += OnSceneLoaded;
            Subscribe();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unsubscribe();

            if (instance == this)
            {
                instance = null;
            }
        }

        // Strict pairs with method groups, never lambdas: these are static events, and a lambda
        // cannot be handed back to -= , so it would leak a subscription per scene load.
        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            NetworkedSpellGame.SpellCastStarted += OnSpellCastStarted;
            NetworkedSpellGame.SpellResolved += OnSpellResolved;
            NetworkedSpellGame.SpellFizzled += OnSpellFizzled;
            NetworkedSpellGame.PlayerCursed += OnPlayerCursed;
            NetworkedSpellGame.PlayerEliminated += OnPlayerEliminated;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            NetworkedSpellGame.SpellCastStarted -= OnSpellCastStarted;
            NetworkedSpellGame.SpellResolved -= OnSpellResolved;
            NetworkedSpellGame.SpellFizzled -= OnSpellFizzled;
            NetworkedSpellGame.PlayerCursed -= OnPlayerCursed;
            NetworkedSpellGame.PlayerEliminated -= OnPlayerEliminated;
            subscribed = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            // Leaving a match unloads the game object, which nulls every static event on the way
            // out. Re-arming here is what makes the next match speak. Also drop anything still
            // queued: narration about the previous match arriving in the lobby is a bug.
            pending.Clear();
            if (voice != null)
            {
                voice.Stop();
            }

            ResetMatchState();
            Unsubscribe();
            Subscribe();
        }

        private void ResetMatchState()
        {
            awaitingReflectionCopy = false;
            wasLocalCursed = false;
            localEliminated = false;
            lastGame = null;
        }

        private void Update()
        {
            DrainQueue();

            if (Time.unscaledTime < nextPoll)
            {
                return;
            }

            nextPoll = Time.unscaledTime + PollSeconds;

            var game = NetworkedSpellGame.Instance;

            // A different game object than last tick means a match spawned (or respawned) and the
            // static events were cleared underneath us at some point. Re-arm before reading it.
            if (!ReferenceEquals(game, lastGame))
            {
                lastGame = game;
                wasLocalCursed = false;
                localEliminated = false;

                if (game != null)
                {
                    Unsubscribe();
                    Subscribe();
                }
            }

            PollCounterspell(game);
        }

        /// <summary>
        /// Counterspell is the one card with no seam to hang off. The curse-defence branch consumes
        /// the potion, calls RemoveCurse and goes straight to the placement choice — it raises
        /// neither SpellCastStarted nor SpellResolved, so there is nothing to subscribe to.
        ///
        /// The local player's curse flag clearing is the observable consequence, and it replicates
        /// to every client, so watching it needs no change to the rules. The one other way it can
        /// clear mid-match is elimination, which <see cref="localEliminated"/> screens out.
        ///
        /// The limitation this leaves: only the local player's Counterspell is narrated. Hearing a
        /// rival survive their own Curse needs a real event on NetworkedSpellGame, which is a
        /// shared file.
        /// </summary>
        private void PollCounterspell(NetworkedSpellGame game)
        {
            if (game == null)
            {
                wasLocalCursed = false;
                return;
            }

            bool cursed = game.IsLocalPlayerCursed;

            if (wasLocalCursed && !cursed)
            {
                if (localEliminated)
                {
                    localEliminated = false; // consumed: this clearing was death, not a save
                }
                else
                {
                    Enqueue(library.CounterspellVoice);
                }
            }

            wasLocalCursed = cursed;
        }

        private void OnSpellCastStarted(PotionType type, ulong casterId, float windowSeconds)
        {
            // Defensive: a Reflection always resolves into its copy in the same call, so this flag
            // cannot normally still be set by the time anything else is cast. Clearing it here
            // means that if it ever were, one line is missed rather than every later resolution
            // being mistaken for a reflection.
            awaitingReflectionCopy = false;

            // windowSeconds is ignored, as in GameAudioDirector: it is 0 whenever nobody holds a
            // Dispel, so it cannot be used to time anything.
            Enqueue(library.CastVoiceFor(type));
        }

        /// <summary>
        /// A Reflection raises SpellResolved twice, back to back inside one call stack: first as
        /// <c>Reflection</c> from ApplyEffect's own entry, then as the copied type when ApplyEffect
        /// recurses into it. The first carries no useful information — "reflection" alone is not a
        /// line anyone recorded — so it only arms the flag, and the second names the recording.
        ///
        /// Every other resolution is ignored here. Ordinary casts are narrated at cast time, and
        /// announcing them again on resolve would double every line.
        /// </summary>
        private void OnSpellResolved(PotionType type, ulong casterId, ulong targetId)
        {
            if (type == PotionType.Reflection)
            {
                awaitingReflectionCopy = true;
                return;
            }

            if (!awaitingReflectionCopy)
            {
                return;
            }

            awaitingReflectionCopy = false;
            Enqueue(library.ReflectionVoiceFor(type));
        }

        // The event's type is the spell that was dispelled, which is the x in "dispel (x)".
        // A Dispel that is itself dispelled never reaches here: HandleInterrupt toggles the
        // cancelled flag, so the spell comes back on and resolves normally instead.
        private void OnSpellFizzled(PotionType dispelledType, ulong casterId)
        {
            Enqueue(library.DispelVoiceFor(dispelledType));
        }

        private void OnPlayerCursed(ulong playerId)
        {
            if (library.CurseVoiceVictimOnly && !IsLocal(playerId))
            {
                return;
            }

            Enqueue(library.CurseVoice);
        }

        private void OnPlayerEliminated(ulong playerId)
        {
            if (IsLocal(playerId))
            {
                localEliminated = true;
            }
        }

        private static bool IsLocal(ulong playerId)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.LocalClientId == playerId;
        }

        private void Enqueue(AudioClip clip)
        {
            if (clip == null || voice == null)
            {
                return;
            }

            if (!voice.isPlaying && pending.Count == 0)
            {
                Play(clip);
                return;
            }

            if (pending.Count < MaxQueued)
            {
                pending.Enqueue(clip);
            }
        }

        private void DrainQueue()
        {
            if (voice == null || voice.isPlaying || pending.Count == 0)
            {
                return;
            }

            Play(pending.Dequeue());
        }

        // Volume is read at play time rather than cached, so a slider moved at the mirror applies
        // to the next line without this component knowing the settings panel exists.
        private void Play(AudioClip clip)
        {
            voice.clip = clip;
            voice.volume = GameAudioSettings.Sfx * library.VoiceTrim;
            voice.Play();
        }
    }
}
