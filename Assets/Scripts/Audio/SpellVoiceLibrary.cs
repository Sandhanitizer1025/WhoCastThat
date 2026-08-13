using System;
using UnityEngine;

namespace WhoCastThat.Audio
{
    /// <summary>
    /// The wizard narration, in one asset. Separate from <see cref="GameAudioLibrary"/> on purpose,
    /// for two reasons that are not stylistic:
    ///
    ///   1. <c>GameAudioLibrary.spellStings</c> plays a spell's clip INSTEAD of the generic cast
    ///      sting. Narration is a second layer over the sound design, not a replacement for it —
    ///      putting a voice line in there would silence cast_sfx (and, for Foresight, delete the
    ///      only reference to foresight_sfx in the project).
    ///   2. Dispel and Reflection have no single line. Which line plays depends on WHAT was
    ///      dispelled or copied, which the flat type -> clip table in spellStings cannot express.
    ///
    /// Must live at Assets/Resources/SpellVoiceLibrary.asset — <see cref="SpellVoiceDirector"/>
    /// loads it by that name with no scene reference to follow.
    /// </summary>
    [CreateAssetMenu(menuName = "Who Cast That/Spell Voice Library", fileName = "SpellVoiceLibrary")]
    public class SpellVoiceLibrary : ScriptableObject
    {
        /// <summary>One narration line, keyed by a spell.</summary>
        [Serializable]
        public struct SpellVoice
        {
            [Tooltip("Which spell. Matches PotionType by index.")]
            public PotionType type;

            public AudioClip clip;
        }

        [Header("Cast lines")]
        [Tooltip("Played when the spell lands on the table. Only the five spells that are cast " +
                 "normally belong here — Dispel, Reflection, Counterspell and Curse all have " +
                 "their own slots below because they are announced at a different moment.")]
        [SerializeField] private SpellVoice[] castVoices = Array.Empty<SpellVoice>();

        [Header("Curse")]
        [Tooltip("A player has just been cursed.")]
        [SerializeField] private AudioClip curseVoice;

        [Tooltip("On: only the cursed player hears the line. Off: everyone does, matching the " +
                 "existing cursed_sfx. Turn this on if the recording is written in second person.")]
        [SerializeField] private bool curseVoiceVictimOnly;

        [Tooltip("Played when the local player answers a Curse with a Counterspell and survives.")]
        [SerializeField] private AudioClip counterspellVoice;

        [Header("Dispel")]
        [Tooltip("A Dispel cancelled a spell. 'type' is the spell that WAS DISPELLED, so the entry " +
                 "for Hex is the 'dispel (hex)' recording.")]
        [SerializeField] private SpellVoice[] dispelVoices = Array.Empty<SpellVoice>();

        [Header("Reflection")]
        [Tooltip("A Reflection copied a spell. 'type' is the spell that WAS COPIED, so the entry " +
                 "for Hex is the 'reflection (hex)' recording. The reflectable set is exactly " +
                 "Hex, Tribute, Foresight, Warp and Phase — a Reflection can never copy a Dispel.")]
        [SerializeField] private SpellVoice[] reflectionVoices = Array.Empty<SpellVoice>();

        [Header("Mix")]
        [Range(0f, 1f)]
        [Tooltip("Trim on top of the player's SFX volume. Narration recorded hotter or quieter " +
                 "than the stings is balanced here rather than by re-exporting the clips.")]
        [SerializeField] private float voiceTrim = 1f;

        public AudioClip CurseVoice => curseVoice;
        public bool CurseVoiceVictimOnly => curseVoiceVictimOnly;
        public AudioClip CounterspellVoice => counterspellVoice;
        public float VoiceTrim => voiceTrim <= 0f ? 1f : voiceTrim;

        public AudioClip CastVoiceFor(PotionType type) => Lookup(castVoices, type);

        /// <summary>The line for a spell that was dispelled, or null if none is authored.</summary>
        public AudioClip DispelVoiceFor(PotionType dispelledType) => Lookup(dispelVoices, dispelledType);

        /// <summary>The line for a spell a Reflection copied, or null if none is authored.</summary>
        public AudioClip ReflectionVoiceFor(PotionType copiedType) => Lookup(reflectionVoices, copiedType);

        // A missing entry returns null rather than falling back to anything. Silence is the right
        // answer for an unauthored line: substituting a different spell's narration would be a
        // worse bug than saying nothing, because it would be wrong out loud.
        private static AudioClip Lookup(SpellVoice[] table, PotionType type)
        {
            if (table == null)
            {
                return null;
            }

            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].type == type && table[i].clip != null)
                {
                    return table[i].clip;
                }
            }

            return null;
        }
    }
}
