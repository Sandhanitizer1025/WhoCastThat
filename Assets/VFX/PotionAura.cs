using UnityEngine;

// Put this on the potion ROOT. It drives every visual layer from ONE colour,
// the same way TintableVFX lets one burst effect serve every card - set
// PotionColour (or call SetColour at runtime) and the liquid, motes, glow light
// and trail all recolour together.
//
// Everything here is deliberately geometry- or light-based rather than sprite
// based. On a headset, flat billboards this close to the face read as cardboard,
// and stacked transparent sprites are what actually kills the frame budget.
// ExecuteAlways so you can see the colour change in the Scene view while
// authoring the nine profiles, instead of having to enter Play Mode each time.
[ExecuteAlways]
public class PotionAura : MonoBehaviour
{
    // HDR so you can push Intensity above 1 in the picker - that overbright value
    // is what Bloom picks up to make the liquid actually glow rather than just
    // being a bright flat colour.
    [ColorUsage(true, true)]
    [SerializeField] Color potionColour = new Color(0.2f, 0.9f, 1f);

    [Header("Layers (all optional - leave empty to skip)")]
    [SerializeField] Renderer liquidRenderer;
    [SerializeField] ParticleSystem[] motes;
    [Tooltip("Inverted-hull outline child. This is the primary type cue - it " +
             "reads across a table where the liquid alone does not, and unlike " +
             "a glow it costs no realtime lights and no post-processing.")]
    [SerializeField] Renderer outlineRenderer;
    [Tooltip("Left null on purpose since the outline replaced the glow lights. " +
             "Wire a Light here again if you ever want hand-tinting back - " +
             "everything below stays null-safe either way.")]
    [SerializeField] Light glowLight;
    [SerializeField] TrailRenderer trail;

    [Header("Outline")]
    [Tooltip("Outline thickness in METRES. The tubes are ~1cm across, so 0.0006 " +
             "(0.6mm) is a firm but not cartoonish edge.")]
    [SerializeField] float outlineWidth = 0.0006f;

    [Header("Idle pulse")]
    [Tooltip("Breathing speed of the glow. Keep this slow - fast pulsing is " +
             "genuinely unpleasant at arm's length in a headset.")]
    [SerializeField] float pulseSpeed = 1.1f;
    [Tooltip("How deep the pulse dips. 0 = steady glow.")]
    [SerializeField] float pulseDepth = 0.25f;
    [SerializeField] float baseLightIntensity = 0.6f;

    [Header("Liquid brightness")]
    [Tooltip("The liquid shader is Unlit, so it has no emission port and there is " +
             "no Bloom in the project to feed - 'glow' here means lifting the " +
             "colour's VALUE toward full brightness while keeping its hue and " +
             "saturation. Value is lerped rather than multiplied because HDR is " +
             "off: a plain multiply clips the bright cards to white, while this " +
             "leaves them nearly alone and lifts the dark ones, which are the " +
             "ones that need it. 0 = exactly the profile colour, 1 = fully bright.")]
    [Range(0f, 1f)]
    [SerializeField] float liquidGlow = 0.35f;

    [Header("Held")]
    [Tooltip("Glow multiplier while the player is holding it.")]
    [SerializeField] float heldBoost = 1.7f;
    [SerializeField] float boostSmoothing = 6f;

    static readonly int TopColour = Shader.PropertyToID("_TopColour");
    static readonly int SideColour = Shader.PropertyToID("_SideColour");
    static readonly int OutlineColour = Shader.PropertyToID("_OutlineColour");
    static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");

    MaterialPropertyBlock mpb;
    float boost = 1f;
    float targetBoost = 1f;
    float appliedBoost = -1f;

    // Set by ApplyProfile so a profile's authored secondary colour wins over the
    // auto-darkened one we derive for hand-tinted potions.
    Color sideColourOverride;
    bool hasSideOverride;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyColour();
    }

    void Update()
    {
        // The pulse and the held-boost are runtime behaviour. glowLight.intensity
        // is serialized, so driving it in edit mode would keep the scene dirty
        // every frame. This is latent rather than live today - glowLight is
        // deliberately left null now that the outline carries the type cue - but
        // it bites the moment someone wires a Light back in, which the field's
        // own tooltip invites. OnValidate still drives the colour preview, and
        // that is the part that actually helps while authoring the profiles.
        if (!Application.isPlaying) return;

        boost = Mathf.Lerp(boost, targetBoost, 1f - Mathf.Exp(-boostSmoothing * Time.deltaTime));

        // Offset by GetInstanceID so several potions in the same scene don't
        // all breathe in lockstep, which instantly reads as fake.
        float phase = Time.time * pulseSpeed + (GetInstanceID() % 100) * 0.06f;
        float pulse = 1f - pulseDepth * (0.5f - 0.5f * Mathf.Cos(phase));

        if (glowLight)
            glowLight.intensity = baseLightIntensity * pulse * boost;

        // The outline deliberately does NOT pulse - it is the always-on type
        // identifier, and something breathing on the edge of every tube in
        // peripheral vision is exactly the kind of thing that gets tiring in a
        // headset. It only responds to being picked up. Guarded so we are not
        // pushing a property block every frame once the boost has settled.
        if (outlineRenderer && !Mathf.Approximately(boost, appliedBoost))
            ApplyOutline();
    }

    // Same entry point as TintableVFX.SetColor so calling code stays familiar.
    public void SetColour(Color c)
    {
        potionColour = c;
        ApplyColour();
    }

    // Preferred entry point: hand it the profile for this potion's type and the
    // whole aura reconfigures. Lets ONE potion prefab serve all nine cards
    // instead of maintaining nine near-identical prefabs.
    public void ApplyProfile(PotionVFXProfile profile)
    {
        if (!profile) return;

        potionColour = profile.primary;
        sideColourOverride = profile.ResolvedSecondary;
        hasSideOverride = true;

        baseLightIntensity = profile.lightIntensity;
        pulseSpeed = profile.pulseSpeed;
        pulseDepth = profile.pulseDepth;

        ApplyColour();
    }

    // Lifts a colour's brightness while leaving hue and saturation alone, so the
    // liquid can read as lit-from-within without drifting toward white the way a
    // straight multiply does as soon as a channel clips at 1.0. Only the liquid
    // uses this - the outline stays on the exact profile colour, because it is the
    // type cue and it has to stay comparable between cards.
    static Color Brighten(Color c, float amount)
    {
        if (amount <= 0f) return c;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        Color lifted = Color.HSVToRGB(h, s, Mathf.Lerp(v, 1f, amount));
        lifted.a = c.a;
        return lifted;
    }

    void ApplyColour()
    {
        // Lazy init - OnValidate can fire before Awake in the editor.
        if (mpb == null) mpb = new MaterialPropertyBlock();

        // Sides sit darker and less saturated than the top so the liquid reads as
        // having volume - a single flat colour looks painted on.
        Color side;
        if (hasSideOverride)
        {
            side = sideColourOverride;
        }
        else
        {
            Color.RGBToHSV(potionColour, out float h, out float s, out float v);
            side = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), Mathf.Clamp01(v * 0.55f));
        }

        if (liquidRenderer)
        {
            liquidRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(TopColour, Brighten(potionColour, liquidGlow));
            mpb.SetColor(SideColour, Brighten(side, liquidGlow));
            liquidRenderer.SetPropertyBlock(mpb);
        }

        if (motes != null)
        {
            foreach (var ps in motes)
            {
                if (!ps) continue;
                var main = ps.main;
                main.startColor = potionColour;
            }
        }

        if (glowLight) glowLight.color = potionColour;

        ApplyOutline();

        if (trail)
        {
            // Fade the trail out along its length so it dissipates instead of
            // ending in a hard edge.
            trail.startColor = potionColour;
            Color end = potionColour;
            end.a = 0f;
            trail.endColor = end;
        }
    }

    // Pushed through a MaterialPropertyBlock so all nine tubes can share one
    // outline material. Writing .material would clone a material per tube and
    // .sharedMaterial would rewrite the .mat on disk every editor frame - the
    // exact bug that used to make Shader Graphs_fake liquid.mat churn in git.
    void ApplyOutline()
    {
        if (!outlineRenderer) return;
        if (mpb == null) mpb = new MaterialPropertyBlock();

        // Read-modify-write: SetPropertyBlock replaces the block wholesale, so
        // skipping the Get would wipe anything else set on this renderer.
        outlineRenderer.GetPropertyBlock(mpb);

        Color c = potionColour * boost;
        c.a = 1f;
        mpb.SetColor(OutlineColour, c);
        mpb.SetFloat(OutlineWidth, outlineWidth);

        outlineRenderer.SetPropertyBlock(mpb);
        appliedBoost = boost;
    }

    // Hook these to XRGrabInteractable's Select Entered / Select Exited events.
    public void OnGrabbed()
    {
        targetBoost = heldBoost;
        if (trail) trail.emitting = true;
    }

    public void OnReleased()
    {
        targetBoost = 1f;
        if (trail) trail.emitting = false;
    }

    void OnValidate()
    {
        if (!Application.isPlaying) ApplyColour();
    }
}
