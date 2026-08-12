using UnityEngine;

// Put this on the ROOT of PS_CardBurst_Universal (the parent of Flash and Ring).
// Call SetColor(...) right after spawning/playing this effect to recolor
// both child particle systems at once - lets one effect serve every card.
public class TintableVFX : MonoBehaviour
{
    ParticleSystem[] systems;

    void Awake() => systems = GetComponentsInChildren<ParticleSystem>();

    public void SetColor(Color c)
    {
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.startColor = c;
        }
    }
}
