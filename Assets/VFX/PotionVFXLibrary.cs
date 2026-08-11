using UnityEngine;

// Single lookup point for "what does a Hex look like?". Create ONE asset via
//   Assets > Create > Who Cast That > Potion VFX Library
// drop all nine profiles in, and reference it from PotionGameManager instead of
// keeping nine differently-decorated prefabs in sync by hand.
[CreateAssetMenu(menuName = "Who Cast That/Potion VFX Library", fileName = "PotionVFXLibrary")]
public class PotionVFXLibrary : ScriptableObject
{
    [SerializeField] PotionVFXProfile[] profiles;

    // PotionType is a small contiguous enum, so a flat array indexed by it is
    // faster and allocation-free compared with a Dictionary, and it rebuilds
    // itself after a domain reload without any init call.
    PotionVFXProfile[] byType;

    public PotionVFXProfile Get(PotionType type)
    {
        if (byType == null) Rebuild();

        int i = (int)type;
        return (i >= 0 && i < byType.Length) ? byType[i] : null;
    }

    void Rebuild()
    {
        int count = System.Enum.GetValues(typeof(PotionType)).Length;
        byType = new PotionVFXProfile[count];

        if (profiles == null) return;
        foreach (var p in profiles)
        {
            if (!p) continue;
            int i = (int)p.type;
            if (i < 0 || i >= count) continue;

            // Warn rather than silently letting the later asset win - two profiles
            // claiming the same type is the kind of thing you only notice in a
            // playtest when Tribute inexplicably glows red.
            if (byType[i])
                Debug.LogWarning($"[PotionVFXLibrary] {p.name} and {byType[i].name} " +
                                 $"both claim {p.type}. Using {byType[i].name}.", this);
            else
                byType[i] = p;
        }
    }

    void OnValidate() => byType = null; // force a rebuild after inspector edits

    // Handy in the editor for spotting a type you forgot to author.
    public bool TryGetMissing(out string missing)
    {
        if (byType == null) Rebuild();
        missing = "";
        foreach (PotionType t in System.Enum.GetValues(typeof(PotionType)))
            if (byType[(int)t] == null) missing += (missing.Length > 0 ? ", " : "") + t;
        return missing.Length > 0;
    }
}
