using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Animates the placeholder ladle. While the current player is stirring (the
    /// replicated <see cref="NetworkedSpellGame.IsStirring"/> flag), the ladle
    /// revolves around the pot; otherwise it eases back to its resting angle.
    ///
    /// Deterministic and driven only by replicated state, so all clients see the
    /// same stir motion without networking the ladle itself. Attach to the ladle
    /// pivot (an object whose local Y-rotation swings the bowl around the pot).
    /// Swap the child meshes for a real ladle model later — this logic is untouched.
    /// </summary>
    public class LadleStir : MonoBehaviour
    {
        [Tooltip("Object rotated around Y to revolve the ladle. Defaults to this transform.")]
        [SerializeField] private Transform ladlePivot;

        [Tooltip("Stir speed while the active player is stirring.")]
        [SerializeField] private float stirDegreesPerSecond = 220f;

        [Tooltip("How quickly the ladle eases back to rest when not stirring.")]
        [SerializeField] private float restReturnSpeed = 3f;

        [Tooltip("Resting Y angle (local) the ladle returns to.")]
        [SerializeField] private float restAngle = 0f;

        private float angle;
        private float baseX;
        private float baseZ;

        private void Start()
        {
            if (ladlePivot == null)
            {
                ladlePivot = transform;
            }
            Vector3 e = ladlePivot.localEulerAngles;
            baseX = e.x;
            baseZ = e.z;
            angle = e.y;
        }

        private void Update()
        {
            if (ladlePivot == null)
            {
                return;
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            bool stirring = game != null && game.IsStirring;

            if (stirring)
            {
                angle += stirDegreesPerSecond * Time.deltaTime;
            }
            else
            {
                float t = 1f - Mathf.Exp(-restReturnSpeed * Time.deltaTime);
                angle = Mathf.LerpAngle(angle, restAngle, t);
            }

            ladlePivot.localEulerAngles = new Vector3(baseX, angle, baseZ);
        }
    }
}
