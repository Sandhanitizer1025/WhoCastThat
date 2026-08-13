using System.Collections.Generic;
using UnityEngine;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// Hats and potions drifting in a slow ring around the player and the boot menu.
    ///
    /// Purely decorative, and aggressively made so: every spawned prop has its colliders,
    /// rigidbodies and scripts stripped and is moved to the Ignore Raycast layer. A boot screen
    /// whose scenery can be grabbed, knocked into the menu, or silently swallow a UI ray is worse
    /// than one with no scenery, and a leftover collider in front of a login button is the kind of
    /// bug that reads as "the button is broken".
    ///
    /// Installs itself from a runtime hook rather than living in the scene, like the audio
    /// director — the boot screen is a login screen and does not need a new serialized object.
    /// </summary>
    public class BootSceneAmbience : MonoBehaviour
    {
        private const string LibraryResourceName = "BootAmbienceLibrary";
        private const string BootSceneName = "BootScene";

        // Unity's built-in layer 2. Nothing physics-based, including UI blocking raycasts,
        // considers it.
        private const int IgnoreRaycastLayer = 2;

        private struct Prop
        {
            public Transform transform;
            public float angleDegrees;
            public float orbitSpeed;
            public float radius;
            public float baseHeight;
            public float bobPhase;
            public Vector3 spinAxis;
            public float spinSpeed;
        }

        private AmbientPropLibrary library;
        private Vector3 centre;
        private readonly List<Prop> props = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != BootSceneName)
            {
                return;
            }

            // Deliberately not DontDestroyOnLoad: this belongs to the boot screen and must not
            // follow the player into the lobby.
            new GameObject("BootSceneAmbience").AddComponent<BootSceneAmbience>();
        }

        private void Start()
        {
            library = Resources.Load<AmbientPropLibrary>(LibraryResourceName);
            if (library == null || library.Props == null || library.Props.Length == 0)
            {
                Debug.LogWarning($"[BootAmbience] No usable {LibraryResourceName} in Resources — " +
                                 "the boot screen will simply have no drifting props.");
                enabled = false;
                return;
            }

            // Centred on the player rather than on the menu, so the ring passes behind the player
            // as well as behind the panels and reads as 360 degrees rather than as a backdrop.
            Camera cam = Camera.main;
            Vector3 head = cam != null ? cam.transform.position : new Vector3(0f, 1.35f, 0f);
            centre = new Vector3(head.x, library.CentreHeight, head.z);

            Spawn();
        }

        private void Spawn()
        {
            int count = Mathf.Max(0, library.Count);
            GameObject[] sources = library.Props;

            for (int i = 0; i < count; i++)
            {
                GameObject source = sources[i % sources.Length];
                if (source == null)
                {
                    continue;
                }

                GameObject instance = Instantiate(source);
                instance.name = "Ambient_" + source.name;
                instance.transform.SetParent(transform, false);
                Sanitise(instance);

                // Spread evenly, then nudged, so the ring never looks like a clock face.
                float spread = 360f / Mathf.Max(1, count);
                float angle = i * spread + Random.Range(-spread * 0.25f, spread * 0.25f);

                var prop = new Prop
                {
                    transform = instance.transform,
                    angleDegrees = angle,
                    // Both directions, so the ring drifts rather than marches.
                    orbitSpeed = library.OrbitDegreesPerSecond * Random.Range(0.6f, 1.4f) *
                                 (Random.value < 0.5f ? -1f : 1f),
                    radius = library.Radius + Random.Range(-library.RadiusJitter, library.RadiusJitter),
                    baseHeight = library.CentreHeight +
                                 Random.Range(-library.HeightSpread * 0.5f, library.HeightSpread * 0.5f),
                    bobPhase = Random.Range(0f, 360f),
                    spinAxis = Random.onUnitSphere,
                    spinSpeed = library.SpinDegreesPerSecond * Random.Range(0.5f, 1.5f)
                };

                float jitter = 1f + Random.Range(-library.SizeJitter, library.SizeJitter);
                NormaliseSize(instance, library.TargetSizeMetres * jitter);
                instance.transform.rotation = Random.rotation;

                props.Add(prop);
            }

            Place(0f);
        }

        /// <summary>
        /// Scale a prop so its bounds measure roughly <paramref name="targetDiagonal"/> across.
        ///
        /// Measured rather than assumed. The source prefabs are authored at wildly different
        /// scales — a shared multiplier of 1.8 rendered the hats at 214mm and the potions at 9mm,
        /// which at 2.5m of orbit distance means the potions were not visible at all.
        /// </summary>
        private static void NormaliseSize(GameObject instance, float targetDiagonal)
        {
            instance.transform.localScale = Vector3.one;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float diagonal = bounds.size.magnitude;
            if (diagonal <= 0.0001f)
            {
                return; // degenerate mesh: leave it alone rather than scale by infinity
            }

            instance.transform.localScale = Vector3.one * (targetDiagonal / diagonal);
        }

        /// <summary>
        /// Strip everything that could interact. Done on the INSTANCE, never the prefab — these
        /// are the real game's hat and potion prefabs and must come through untouched.
        /// </summary>
        private static void Sanitise(GameObject instance)
        {
            // Scripts first: a NetworkObject or a potion behaviour waking up in a scene with no
            // game manager is a stream of null-reference errors, not a decoration.
            MonoBehaviour[] scripts = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < scripts.Length; i++)
            {
                if (scripts[i] != null)
                {
                    Destroy(scripts[i]);
                }
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Destroy(bodies[i]);
            }

            // Belt and braces: even if something above survives on a prefab variant, this layer
            // is ignored by every physics raycast, so it can never eat a menu click.
            Transform[] all = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.layer = IgnoreRaycastLayer;
            }
        }

        private void Update()
        {
            Place(Time.deltaTime);
        }

        private void Place(float deltaTime)
        {
            float time = Time.time;

            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.transform == null)
                {
                    continue;
                }

                prop.angleDegrees += prop.orbitSpeed * deltaTime;
                props[i] = prop;

                float radians = prop.angleDegrees * Mathf.Deg2Rad;
                float bob = Mathf.Sin((time * library.BobDegreesPerSecond + prop.bobPhase) * Mathf.Deg2Rad)
                            * library.BobAmplitude;

                prop.transform.position = new Vector3(
                    centre.x + Mathf.Cos(radians) * prop.radius,
                    prop.baseHeight + bob,
                    centre.z + Mathf.Sin(radians) * prop.radius);

                prop.transform.Rotate(prop.spinAxis, prop.spinSpeed * deltaTime, Space.Self);
            }
        }
    }
}
