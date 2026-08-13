using UnityEngine;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// The prefabs the boot screen drifts around the player, in one asset.
    ///
    /// A ScriptableObject in Resources for the same reason the audio library is one: the thing
    /// that spawns these installs itself from a runtime hook and has no scene presence, so it has
    /// no serialized field anyone could drop a prefab into.
    ///
    /// Must live at Assets/Resources/BootAmbienceLibrary.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Who Cast That/Ambient Prop Library", fileName = "BootAmbienceLibrary")]
    public class AmbientPropLibrary : ScriptableObject
    {
        [Tooltip("Decorative only. These are stripped of colliders and scripts when spawned, so " +
                 "anything with behaviour attached will arrive as a plain mesh.")]
        [SerializeField] private GameObject[] props = new GameObject[0];

        [Tooltip("How many to drift at once. More than the prop count simply repeats them.")]
        [SerializeField] private int count = 12;

        [Header("Orbit")]
        [Tooltip("Metres from the orbit centre. Must clear the menu: at boot the panels reach " +
                 "about 2.0m from the player, so anything under that flies through the UI.")]
        [SerializeField] private float radius = 2.6f;

        [SerializeField] private float radiusJitter = 0.55f;

        [Tooltip("Vertical span the ring is spread across, centred on the menu's height.")]
        [SerializeField] private float heightSpread = 1.6f;

        [SerializeField] private float centreHeight = 1.35f;

        [Header("Motion")]
        [Tooltip("Degrees per second around the player. Individual props vary either side of this.")]
        [SerializeField] private float orbitDegreesPerSecond = 7f;

        [SerializeField] private float bobAmplitude = 0.16f;
        [SerializeField] private float bobDegreesPerSecond = 34f;
        [SerializeField] private float spinDegreesPerSecond = 26f;

        [Tooltip("Target on-screen size, as the diagonal of the prop's bounds in metres. Each prop " +
                 "is scaled to MEET this rather than being given a shared multiplier: the source " +
                 "prefabs differ by more than 20x, so one scale factor renders the hats correctly " +
                 "and the potions as 9mm specks.")]
        [SerializeField] private float targetSizeMetres = 0.34f;

        [Tooltip("Per-prop variation around the target size, so the ring has some depth to it.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float sizeJitter = 0.25f;

        public GameObject[] Props => props;
        public int Count => count;
        public float Radius => radius;
        public float RadiusJitter => radiusJitter;
        public float HeightSpread => heightSpread;
        public float CentreHeight => centreHeight;
        public float OrbitDegreesPerSecond => orbitDegreesPerSecond;
        public float BobAmplitude => bobAmplitude;
        public float BobDegreesPerSecond => bobDegreesPerSecond;
        public float SpinDegreesPerSecond => spinDegreesPerSecond;
        public float TargetSizeMetres => targetSizeMetres;
        public float SizeJitter => sizeJitter;
    }
}
