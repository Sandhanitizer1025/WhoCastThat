using UnityEngine;
using UnityEngine.InputSystem;

// Temporary test script - put this on any object in your scene, drag
// PS_CardBurst_Universal's Flash and Ring Particle System components into
// the "Target" array, and press Space in Play Mode to fire it in a random
// color. Delete this script once you're happy with how it looks - it's just
// for checking the effect in isolation before wiring it into a real card system.
public class BurstTester : MonoBehaviour
{
    [SerializeField] ParticleSystem[] target; // drag Flash and Ring here
    [SerializeField] TintableVFX tintable;    // drag the root object here

    void Update()
    {
#if !UNITY_EDITOR
        // Never in a build. Bare Space collides with the XR device simulator, and
        // MultiplayerTestHarness had to put every one of its keys behind Left Ctrl
        // for the same reason. A tester on a headset pressing Space should not be
        // firing debug particles.
        return;
#else
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
        {
            Color c = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
            if (tintable) tintable.SetColor(c);
            foreach (var ps in target)
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }
#endif
    }
}
