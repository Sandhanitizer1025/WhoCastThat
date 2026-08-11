using UnityEngine;

// How the effect MOVES. This matters as much as colour: motion reads faster than
// hue in a headset, and it still works for the ~8% of male players with colour
// vision deficiency, who cannot reliably separate Hex red from Phase green.
// Every archetype below is deliberately a different silhouette in motion.
public enum SpellMotion
{
    Strike,     // Hex - fast directional stab toward the victim
    Pull,       // Tribute - ribbon dragging from target back to the caster
    Negate,     // Dispel - instant flat shockwave, no ease-in, cuts everything off
    Reveal,     // Foresight - slow upward drift, calm, no impact
    Swirl,      // Warp - rotation around the deck
    Dissipate,  // Phase - soft downward fade, the caster "isn't there"
    Mirror,     // Reflection - shimmer that echoes the previous spell's shape
    Ward,       // Counterspell - protective dome snapping shut around the player
    Implode     // Curse - suck inward, hold, then erupt. The whole table should feel it.
}

// One asset per potion type. Create via:
//   Assets > Create > Who Cast That > Potion VFX Profile
//
// This replaces "9 prefabs each with their own particle rig" with "1 prefab +
// 9 data assets". Adding a tenth card later is then an inspector job, not a
// prefab-surgery job - which is what the plan's scalability requirement needs.
[CreateAssetMenu(menuName = "Who Cast That/Potion VFX Profile", fileName = "VFX_")]
public class PotionVFXProfile : ScriptableObject
{
    public PotionType type;

    [Header("Identity")]
    [Tooltip("Main colour of the liquid, glow and motes. Push Intensity above 1 " +
             "so Bloom picks it up.")]
    [ColorUsage(true, true)] public Color primary = Color.cyan;

    [Tooltip("Used for the darker liquid sides and trail fade. Leave black to " +
             "auto-derive a darker version of Primary.")]
    [ColorUsage(true, true)] public Color secondary = Color.black;

    [Header("Cast effect")]
    public SpellMotion motion = SpellMotion.Strike;

    [Tooltip("How long the cast effect lives, in seconds. Keep the common cards " +
             "short - players see Hex 5 times a game and it gets old fast.")]
    [Range(0.1f, 4f)] public float duration = 0.8f;

    [Tooltip("Overall size multiplier. Curse should dwarf everything else.")]
    [Range(0.2f, 4f)] public float scale = 1f;

    [Tooltip("Particle count for the cast burst. Watch this total - stacked " +
             "transparent particles are the main cost on standalone headsets.")]
    [Range(0, 120)] public int burstCount = 30;

    [Header("Idle aura (while held or in the rack)")]
    [Range(0f, 3f)] public float lightIntensity = 0.6f;
    [Range(0f, 4f)] public float pulseSpeed = 1.1f;
    [Range(0f, 1f)] public float pulseDepth = 0.25f;

    [Header("Audio")]
    public AudioClip castSound;
    [Range(0f, 1f)] public float volume = 0.8f;

    // Secondary falls back to a darker, slightly desaturated Primary so the
    // liquid still reads as having volume even if you never fill this in.
    public Color ResolvedSecondary
    {
        get
        {
            if (secondary.maxColorComponent > 0.001f) return secondary;
            Color.RGBToHSV(primary, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), Mathf.Clamp01(v * 0.55f));
        }
    }
}
