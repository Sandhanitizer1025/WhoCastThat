using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// A throwable potion for the interaction test scene. When released from a VR
    /// hand fast enough to count as a throw, it shatters on impact: spawns a burst
    /// of liquid droplets, plays an optional sound, and (optionally) destroys itself
    /// so the <see cref="PotionSpawner"/> can restock the table.
    /// Attach to a GameObject that also has a Rigidbody and an XRGrabInteractable.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ThrowablePotion : MonoBehaviour
    {
        [Header("Throw detection")]
        [Tooltip("Minimum release speed (m/s) for a release to count as a throw.")]
        [SerializeField] private float throwSpeedThreshold = 1.5f;

        [Header("Shatter feedback")]
        [Tooltip("Colour of the liquid droplets sprayed on impact.")]
        [SerializeField] private Color liquidColor = new Color(0.45f, 0.2f, 0.85f);

        [Tooltip("How many droplets to spray when the potion shatters.")]
        [SerializeField] private int dropletCount = 10;

        [Tooltip("Optional sound played at the impact point. Assign a shatter SFX later.")]
        [SerializeField] private AudioClip shatterSound;

        [Tooltip("If true, the potion is destroyed when it shatters (spawner restocks it).")]
        [SerializeField] private bool destroyOnShatter = true;

        private Rigidbody body;
        private XRGrabInteractable grabInteractable;
        private bool isThrown;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        // Picking the potion back up cancels any in-progress throw.
        private void OnGrabbed(SelectEnterEventArgs args)
        {
            isThrown = false;
        }

        // A release counts as a throw only if the potion left the hand with enough speed.
        private void OnReleased(SelectExitEventArgs args)
        {
            isThrown = body.linearVelocity.magnitude >= throwSpeedThreshold;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isThrown)
            {
                return;
            }

            isThrown = false;
            Shatter(collision.GetContact(0).point);
        }

        // Placeholder effect hook — later this is where a card's spell effect would
        // fire and (in multiplayer) be networked to the other players.
        private void Shatter(Vector3 point)
        {
            SpraySplash(point);

            if (shatterSound != null)
            {
                AudioSource.PlayClipAtPoint(shatterSound, point);
            }

            Debug.Log($"[ThrowablePotion] {name} shattered at {point}.", this);

            if (destroyOnShatter)
            {
                Destroy(gameObject);
            }
        }

        // Code-driven splash so no VFX asset is needed for the test scene: a handful
        // of small coloured spheres burst outward and clean themselves up.
        private void SpraySplash(Vector3 point)
        {
            for (int i = 0; i < dropletCount; i++)
            {
                GameObject droplet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                droplet.name = "PotionDroplet";
                droplet.transform.position = point;
                droplet.transform.localScale = Vector3.one * Random.Range(0.02f, 0.045f);

                Renderer dropletRenderer = droplet.GetComponent<Renderer>();
                dropletRenderer.material.color = liquidColor;

                Rigidbody dropletBody = droplet.AddComponent<Rigidbody>();
                dropletBody.mass = 0.02f;
                dropletBody.linearVelocity = Random.onUnitSphere * Random.Range(1.5f, 3f) + Vector3.up;

                Destroy(droplet, 1.5f);
            }
        }
    }
}
