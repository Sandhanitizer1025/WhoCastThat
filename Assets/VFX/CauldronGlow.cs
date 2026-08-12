using UnityEngine;

// Subtle firelight flicker for the cauldron's glow light.
//
// A perfectly steady light reads as a lamp, not as something boiling. But a
// noisy flicker at arm's length in a headset is genuinely unpleasant, so this
// is deliberately gentle: two slow sine layers at unrelated frequencies, which
// gives an organic wander without ever snapping.
//
// The whole point of this component is restraint - the brief was a glow that
// does not blind. baseIntensity is the dial, and maxFlicker is capped low on
// purpose. If you raise these, check it in the headset rather than on a
// monitor; a value that looks tame on a desktop screen is much harsher when
// it fills your peripheral vision.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class CauldronGlow : MonoBehaviour
{
    [Tooltip("Resting intensity in CANDELA, and it needs to stay small. The pot " +
             "interior is only ~14cm across, so the rim sits a few centimetres " +
             "from the bulb and illuminance falls off with distance squared - " +
             "2.6cd blew the rim to pure white in testing. The soup shader's own " +
             "emission carries the look; this light only adds the spill.")]
    [SerializeField] float baseIntensity = 0.45f;

    [Tooltip("How far the flicker swings either side of base, as a fraction. " +
             "0.12 is a simmer; above ~0.3 it starts to read as a fire alarm.")]
    [Range(0f, 0.4f)]
    [SerializeField] float maxFlicker = 0.12f;

    [SerializeField] float primarySpeed = 1.7f;
    [SerializeField] float secondarySpeed = 0.63f;

    Light glow;

    void OnEnable()
    {
        glow = GetComponent<Light>();
    }

    void Update()
    {
        if (!glow) return;

        // Outside Play Mode this settles on the resting value and then stops
        // writing. intensity is a SERIALIZED property, so animating it in edit
        // mode re-dirties the scene every frame and YeKai.unity never shows as
        // clean - the same churn the Liquid.cs sharedMaterial fix removed, only
        // relocated from the .mat to the .unity. The compare matters: assigning
        // even an unchanged value each frame is enough to keep it dirty.
        //
        // The cost is that the flicker now previews in Play Mode rather than the
        // Scene View. That is the cheaper half of the trade while three people
        // are sharing this repo and scene YAML cannot be merged by hand.
        if (!Application.isPlaying)
        {
            if (!Mathf.Approximately(glow.intensity, baseIntensity))
                glow.intensity = baseIntensity;
            return;
        }

        // Two incommensurate frequencies so the pattern never audibly loops.
        float t = Time.time;
        float a = Mathf.Sin(t * primarySpeed);
        float b = Mathf.Sin(t * secondarySpeed + 1.7f);
        float flicker = 1f + maxFlicker * (a * 0.6f + b * 0.4f);

        glow.intensity = baseIntensity * flicker;
    }
}
