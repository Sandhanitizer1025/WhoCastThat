using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Trigger volume in the centre of the table. When a potion the LOCAL player owns
    /// is DROPPED in, this submits that potion's spell to the authority (targeting the next
    /// player). The authority decides whether the cast is legal: if it is, the potion is
    /// despawned; if it is not, the potion is sent back to its rack slot.
    ///
    /// Casting is deliberately committed on release, not on entry. The potion prefab uses
    /// VelocityTracking, so a grabbed potion keeps a live (non-kinematic) rigidbody — casting
    /// on entry would fire the spell the instant a player merely waved a potion across the
    /// middle of the table, straight out of their hand. Watching the zone every frame also
    /// catches the normal case that a trigger event alone misses: letting go of a potion
    /// while your hand is already inside the zone raises no new OnTriggerEnter.
    ///
    /// The owner check ensures only one client submits the cast (and the owner has the
    /// network authority needed to move/despawn the potion in Distributed Authority).
    /// Requires a trigger Collider on this GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlayZone : MonoBehaviour
    {
        /// <summary>The zone in the scene, so a dropped potion can ask whether it landed here.</summary>
        public static PlayZone Instance { get; private set; }

        [Tooltip("Draw a ring on the table showing exactly where a potion must be dropped to cast.")]
        [SerializeField] private bool showMarker = true;

        [Tooltip("Ring colour when the local player is not holding a potion.")]
        [SerializeField] private Color idleColor = new(0.45f, 0.32f, 0.75f, 0.18f);

        [Tooltip("Ring colour while the local player is holding a potion — i.e. drop it here.")]
        [SerializeField] private Color armedColor = new(0.62f, 0.95f, 0.55f, 0.42f);

        private readonly HashSet<NetworkedPotion> inside = new();
        private readonly List<NetworkedPotion> scratch = new();

        private Renderer ringRenderer;
        private Renderer fillRenderer;
        private bool markerArmed;

        private void Awake()
        {
            Instance = this;

            if (showMarker)
            {
                BuildMarker();
            }
        }

        /// <summary>
        /// The zone is an invisible trigger, which makes casting a guessing game — a potion
        /// dropped just outside it silently flies back to the rack and reads as a bug. This
        /// draws a flat ring on the table top matching the trigger's real footprint, so the
        /// target is exactly what the collider tests. Built from code so no prefab wiring or
        /// art is needed; delete this method and drop in a designed decal to replace it.
        /// </summary>
        private void BuildMarker()
        {
            Bounds b = GetComponent<Collider>().bounds;
            float diameter = Mathf.Min(b.size.x, b.size.z);

            // Sits a hair above the trigger's own base, which rests on the table surface.
            var centre = new Vector3(b.center.x, b.min.y + 0.001f, b.center.z);

            ringRenderer = CreateDisc("PlayZoneRing", centre, diameter, idleColor);
            fillRenderer = CreateDisc("PlayZoneFill", centre + new Vector3(0f, 0.0005f, 0f),
                                      diameter * 0.86f, new Color(0f, 0f, 0f, 0f));
        }

        private Renderer CreateDisc(string name, Vector3 worldCentre, float diameter, Color colour)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

            Collider discCollider = disc.GetComponent<Collider>();
            if (discCollider != null)
            {
                Destroy(discCollider); // must never block a drop or the XR ray
            }

            disc.name = name;
            disc.transform.SetParent(transform, true);
            disc.transform.position = worldCentre;
            disc.transform.rotation = Quaternion.identity;

            // The cylinder primitive is 1 unit across and 2 units tall, and this object's own
            // transform is scaled, so convert the world size we want into local scale.
            Vector3 parentScale = transform.lossyScale;
            var world = new Vector3(diameter, 0.002f, diameter);
            disc.transform.localScale = new Vector3(
                world.x / Mathf.Max(0.0001f, parentScale.x),
                world.y / Mathf.Max(0.0001f, parentScale.y),
                world.z / Mathf.Max(0.0001f, parentScale.z));

            var renderer = disc.GetComponent<Renderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                unlit = Shader.Find("Unlit/Color");
            }

            if (unlit != null)
            {
                var mat = new Material(unlit);
                mat.SetFloat("_Surface", 1f); // transparent
                mat.SetFloat("_Blend", 0f);   // alpha blend
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.color = colour;
                renderer.sharedMaterial = mat;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // Light the ring up while the local player is actually carrying a potion, so the
        // target announces itself exactly when it matters and stays quiet otherwise.
        private void UpdateMarker()
        {
            if (ringRenderer == null)
            {
                return;
            }

            bool armed = NetworkedPotion.LocallyHeldCount > 0;
            if (armed == markerArmed)
            {
                return;
            }

            markerArmed = armed;
            ringRenderer.sharedMaterial.color = armed ? armedColor : idleColor;

            if (fillRenderer != null)
            {
                Color fill = armed ? armedColor : idleColor;
                fill.a = armed ? 0.14f : 0f;
                fillRenderer.sharedMaterial.color = fill;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Is this potion currently within the play zone?</summary>
        public bool Contains(NetworkedPotion potion)
        {
            return potion != null && inside.Contains(potion);
        }

        private void OnTriggerEnter(Collider other)
        {
            NetworkedPotion potion = other.GetComponentInParent<NetworkedPotion>();
            if (potion != null)
            {
                inside.Add(potion);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            NetworkedPotion potion = other.GetComponentInParent<NetworkedPotion>();
            if (potion != null)
            {
                inside.Remove(potion);
            }
        }

        private void Update()
        {
            UpdateMarker();

            if (inside.Count == 0 || NetworkedSpellGame.Instance == null)
            {
                return;
            }

            scratch.Clear();
            scratch.AddRange(inside);

            for (int i = 0; i < scratch.Count; i++)
            {
                NetworkedPotion potion = scratch[i];
                if (potion == null)
                {
                    inside.Remove(potion);
                    continue;
                }

                if (potion.CastSubmitted)
                {
                    continue; // already with the authority, waiting on its verdict
                }

                NetworkObject netObj = potion.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.IsSpawned || !netObj.IsOwner)
                {
                    continue; // only the potion's owner submits the cast
                }

                // Still held: a potion counts as played only once it has been let go.
                XRGrabInteractable grab = potion.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected)
                {
                    continue;
                }

                // Kinematic means the potion is either seated in a rack or mid-flight from
                // the cauldron (that path crosses this zone). Neither counts as a play.
                Rigidbody body = potion.GetComponent<Rigidbody>();
                if (body != null && body.isKinematic)
                {
                    continue;
                }

                potion.CastSubmitted = true;
                NetworkedSpellGame.Instance.RequestCastFromPotion(potion);
            }
        }
    }
}
