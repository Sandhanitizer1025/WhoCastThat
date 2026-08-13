using System;
using UnityEngine;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// The hats a player can wear, each paired with the player colour it stands for.
    ///
    /// Hat choice rides the player colour that <c>XRINetworkPlayer</c> already replicates rather
    /// than adding a variable of its own. That is not a shortcut — Netcode requires every
    /// NetworkBehaviour to exist on the prefab at spawn time, and the avatar prefab lives in
    /// VRMPAssets, which is not ours to edit. Riding the existing replicated colour is the only
    /// route that survives both constraints.
    ///
    /// Must live at Assets/Resources/PlayerHatLibrary.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Who Cast That/Player Hat Library", fileName = "PlayerHatLibrary")]
    public class PlayerHatLibrary : ScriptableObject
    {
        [Serializable]
        public struct Hat
        {
            public GameObject prefab;

            [Tooltip("Shown on the lobby picker. Authored rather than derived from the prefab " +
                     "name, which would put 'Yello' in front of the player.")]
            public string label;

            [Tooltip("The player colour this hat represents. The nearest match wins, so these do " +
                     "not have to line up exactly with the template's swatches.")]
            public Color colour;
        }

        [SerializeField] private Hat[] hats = Array.Empty<Hat>();

        [Header("Fit")]
        [Tooltip("Metres from the avatar's head origin to the BASE of the hat. The head origin " +
                 "tracks the camera, i.e. eye level, so this needs to clear the top of the skull " +
                 "-- roughly 0.10m on an adult.")]
        [SerializeField] private float heightOffset = 0.10f;

        [Tooltip("Metres forward, to sit the brim over the face rather than centred on the neck.")]
        [SerializeField] private float forwardOffset = 0.01f;

        [Tooltip("Degrees to yaw the hat about the head's up axis before seating it. The hat mesh " +
                 "is authored facing AWAY from the head's forward -- its bounds centre sits 3.5mm " +
                 "behind its own pivot -- so at 0 the brim hangs down the back of the neck. 180 " +
                 "turns it to face the same way as the player.")]
        [SerializeField] private float yawDegrees = 180f;

        [Tooltip("Target hat width in metres. Applied by MEASURING each prefab rather than as a " +
                 "shared multiplier -- the hat prefabs are not authored at a common scale.")]
        [SerializeField] private float widthMetres = 0.30f;

        public Hat[] Hats => hats;
        public float HeightOffset => heightOffset;
        public float ForwardOffset => forwardOffset;
        public float WidthMetres => widthMetres;
        public float YawDegrees => yawDegrees;

        /// <summary>
        /// The hat's rotation relative to the head it sits on. Used by both the worn hat and the
        /// lobby preview, so the two cannot drift apart — the preview's whole purpose is to show
        /// what the player actually gets.
        /// </summary>
        public Quaternion FitRotation => Quaternion.Euler(0f, yawDegrees, 0f);

        /// <summary>
        /// The hat whose colour sits closest to <paramref name="colour"/>, or null if none are
        /// configured. Nearest-match rather than exact, so a colour the template picks that no hat
        /// was authored for still puts a hat on the player instead of leaving them bare-headed.
        /// </summary>
        public GameObject Nearest(Color colour)
        {
            GameObject best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < hats.Length; i++)
            {
                if (hats[i].prefab == null)
                {
                    continue;
                }

                float dr = hats[i].colour.r - colour.r;
                float dg = hats[i].colour.g - colour.g;
                float db = hats[i].colour.b - colour.b;
                float distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = hats[i].prefab;
                }
            }

            return best;
        }
    }
}
