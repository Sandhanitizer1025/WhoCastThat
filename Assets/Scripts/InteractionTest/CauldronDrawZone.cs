using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Trigger volume on the cauldron. When the local player dips a hand/controller in,
    /// it asks the game to draw a potion. The authority validates it is that player's
    /// turn, so out-of-turn dips are harmless.
    ///
    /// Only the local rig's interactors trigger a draw — remote players' networked
    /// avatar hands have no XR interactor, so they are ignored automatically.
    /// Requires a trigger Collider on this GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CauldronDrawZone : MonoBehaviour
    {
        [Tooltip("Minimum seconds between draws from a single dip.")]
        [SerializeField] private float drawCooldown = 1.5f;

        private float nextDrawTime;

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time < nextDrawTime)
            {
                return;
            }

            // A local interactor identifies this as the local player's hand.
            if (other.GetComponentInParent<XRBaseInteractor>() == null)
            {
                return;
            }

            if (NetworkedSpellGame.Instance == null)
            {
                return;
            }

            nextDrawTime = Time.time + drawCooldown;
            NetworkedSpellGame.Instance.RequestDraw();
        }
    }
}
