using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// Puts a hat on a networked player's head, matched to the colour they already replicate.
    ///
    /// Attached at runtime by <see cref="PlayerHatInstaller"/> rather than added to the avatar
    /// prefab: that prefab is in VRMPAssets and is not ours to edit. A plain MonoBehaviour is
    /// enough because nothing here needs replicating — the colour is already on the wire, and
    /// every client independently resolves the same colour to the same hat.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHat : MonoBehaviour
    {
        private const string LibraryResourceName = "PlayerHatLibrary";
        private const int IgnoreRaycastLayer = 2;

        private PlayerHatLibrary library;
        private XRINetworkPlayer player;
        private GameObject worn;
        private GameObject wornSource;

        private void Awake()
        {
            player = GetComponent<XRINetworkPlayer>();
            library = Resources.Load<PlayerHatLibrary>(LibraryResourceName);

            if (player == null || library == null || library.Hats.Length == 0)
            {
                enabled = false;
                return;
            }

            player.onColorUpdated += Apply;
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.onColorUpdated -= Apply;
            }
        }

        private void Start()
        {
            // onColorUpdated only fires on a CHANGE. A player whose colour was already settled
            // before this component existed would never get a hat without this first call.
            Apply(player.playerColor);
        }

        private void Apply(Color colour)
        {
            if (library == null || player == null || player.head == null)
            {
                return;
            }

            GameObject source = library.Nearest(colour);
            if (source == null)
            {
                return;
            }

            // Rebuilding an identical hat every time the colour ticks would throw away the
            // instance mid-frame and flicker.
            if (worn != null && wornSource == source)
            {
                return;
            }

            if (worn != null)
            {
                Destroy(worn);
            }

            wornSource = source;
            worn = Instantiate(source);
            worn.name = "Hat";

            // Parented to the avatar's head, which XRINetworkPlayer drives from the head origin
            // every frame, so the hat tracks head movement without any code here.
            worn.transform.SetParent(player.head, false);
            worn.transform.localRotation = Quaternion.identity;

            Sanitise(worn);
            FitSize(worn, library.WidthMetres);

            // Seat the hat by its BASE rather than by its pivot. Where a hat prefab's origin sits
            // is not something this code can know -- base, centre and crown are all plausible and
            // they differ between the five hats -- so the offset is measured from the scaled
            // bounds instead of assumed. Without this a hat with a centred pivot sinks into the
            // skull while one pivoted at the crown floats above it.
            worn.transform.localPosition = new Vector3(0f, 0f, library.ForwardOffset);

            float baseBelowPivot = 0f;
            Renderer[] renderers = worn.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                baseBelowPivot = worn.transform.position.y - bounds.min.y;
            }

            worn.transform.localPosition =
                new Vector3(0f, library.HeightOffset + baseBelowPivot, library.ForwardOffset);
        }

        /// <summary>
        /// A hat is scenery. Anything grabbable or solid on a player's head is a way to interfere
        /// with them, and a collider riding the head is a collider that can shove the table.
        /// Done on the instance; the hat prefabs themselves are untouched.
        /// </summary>
        private static void Sanitise(GameObject instance)
        {
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

            Transform[] all = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.layer = IgnoreRaycastLayer;
            }
        }

        /// <summary>
        /// Scale the hat to a target width measured from its own bounds. The boot-screen props
        /// taught this one: the hat prefabs are not authored at a shared scale, so a fixed
        /// multiplier fits one of them and mis-sizes the rest.
        /// </summary>
        private static void FitSize(GameObject instance, float targetWidth)
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

            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            if (width <= 0.0001f)
            {
                return;
            }

            instance.transform.localScale = Vector3.one * (targetWidth / width);
        }
    }
}
