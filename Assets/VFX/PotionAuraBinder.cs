using UnityEngine;
using WhoCastThat.Interactions;

// Bridges the networked game to the VFX data. Put this on the potion ROOT,
// alongside NetworkedPotion and PotionAura, and drop PotionVFXLibrary.asset in.
//
// This is the hook VFX_HANDOFF.md's "Priority 4 - hook into gameplay" was
// looking for. That section aimed at PotionGameManager.GetPrefabForType(), but
// PotionGameManager is LEGACY, pre-networking, and nothing in a real match runs
// it - potions in the live game are spawned by NetworkedSpellGame and carry a
// NetworkedPotion. The refactor it proposed is therefore unnecessary rather
// than pending: one prefab plus the library is already how the networked path
// works, and this component is the whole of the wiring.
//
// The type is REPLICATED, so this fires on every client independently and no
// VFX call ever has to cross the network. NetworkedPotion.ApplyVisual() raises
// TypeApplied from OnNetworkSpawn and again on every later change, which covers
// a client that joined late and receives the value after spawn.
[RequireComponent(typeof(PotionAura))]
public class PotionAuraBinder : MonoBehaviour
{
    [Tooltip("The one PotionVFXLibrary asset holding all nine profiles.")]
    [SerializeField] PotionVFXLibrary library;

    PotionAura aura;
    NetworkedPotion potion;

    void Awake()
    {
        aura = GetComponent<PotionAura>();
        potion = GetComponent<NetworkedPotion>();

        if (potion == null)
            Debug.LogWarning("[PotionAuraBinder] No NetworkedPotion on this object - " +
                             "the aura will keep whatever colour it was authored with.", this);

        if (library == null)
            Debug.LogWarning("[PotionAuraBinder] No PotionVFXLibrary assigned - " +
                             "potions will not pick up their per-type colours.", this);
    }

    // Subscribe and unsubscribe in strict pairs, with a METHOD GROUP rather than a
    // lambda. This is the exact bug StaleSubscriptionScrubber exists to clean up
    // after: OfflinePlayerAvatar "unsubscribes" with a freshly allocated lambda,
    // which never matches the one it added, so the handler leaks and fires into a
    // destroyed object. A method group compares equal by target + method, so -=
    // genuinely removes it. Potions are despawned and respawned constantly - every
    // cast destroys one - so a leak here would accumulate fast.
    void OnEnable()
    {
        if (potion == null) return;

        potion.TypeApplied += Apply;

        // Covers being enabled AFTER the spawn call already fired, which is the one
        // ordering TypeApplied alone would miss.
        if (potion.IsSpawned) Apply(potion.Type);
    }

    void OnDisable()
    {
        if (potion != null) potion.TypeApplied -= Apply;
    }

    void Apply(PotionType type)
    {
        if (library == null || aura == null) return;

        PotionVFXProfile profile = library.Get(type);
        if (profile != null) aura.ApplyProfile(profile);
    }
}
