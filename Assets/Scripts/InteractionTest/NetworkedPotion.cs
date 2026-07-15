using System;
using Unity.Netcode;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// A networked, grabbable potion "card". The authority assigns its
    /// <see cref="PotionType"/> when it spawns; every client reads that to show the
    /// right visual and to know which spell to cast when the potion is placed in the
    /// play zone.
    ///
    /// Modularity: the visual is intended to live on a child object (assign its
    /// renderer to <see cref="tintRenderer"/>, or swap the whole child mesh for a
    /// teammate's potion model) — the networking + type logic here stays untouched.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkedPotion : NetworkBehaviour
    {
        [Tooltip("The liquid renderer (fake-liquid shader) tinted per potion type via _SideColour/_TopColour.")]
        [SerializeField] private Renderer tintRenderer;

        // Fake-liquid shadergraph colour properties.
        private static readonly int SideColourId = Shader.PropertyToID("_SideColour");
        private static readonly int TopColourId = Shader.PropertyToID("_TopColour");
        private MaterialPropertyBlock tintBlock;

        // Owner-write so the authority can set the type when it spawns the potion.
        private readonly NetworkVariable<int> networkedType = new(
            (int)PotionType.Counterspell,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        /// <summary>The spell this potion represents.</summary>
        public PotionType Type => (PotionType)networkedType.Value;

        /// <summary>Raised on every client whenever the type is (re)applied.</summary>
        public event Action<PotionType> TypeApplied;

        /// <summary>Authority-only: set the potion's type right after spawning it.</summary>
        public void SetType(PotionType type)
        {
            if (HasAuthority)
            {
                networkedType.Value = (int)type;
            }
        }

        public override void OnNetworkSpawn()
        {
            networkedType.OnValueChanged += OnTypeChanged;
            ApplyVisual();
        }

        public override void OnNetworkDespawn()
        {
            networkedType.OnValueChanged -= OnTypeChanged;
        }

        private void OnTypeChanged(int previous, int current)
        {
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            name = $"Potion ({Type})";
            if (tintRenderer != null)
            {
                Color c = ColorFor(Type);
                Color side = new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 1f);
                tintBlock ??= new MaterialPropertyBlock();
                tintRenderer.GetPropertyBlock(tintBlock);
                tintBlock.SetColor(SideColourId, side);
                tintBlock.SetColor(TopColourId, c);
                tintRenderer.SetPropertyBlock(tintBlock);
            }
            TypeApplied?.Invoke(Type);
        }

        /// <summary>Placeholder colour per type — swap for real potion art later.</summary>
        public static Color ColorFor(PotionType type)
        {
            switch (type)
            {
                case PotionType.Hex: return new Color(0.4f, 0.2f, 0.05f);
                case PotionType.Tribute: return new Color(0.82f, 0.80f, 0.13f);
                case PotionType.Dispel: return new Color(0.69f, 0.15f, 0.15f);
                case PotionType.Foresight: return new Color(1f, 0.21f, 0.87f);
                case PotionType.Warp: return new Color(0.24f, 0.24f, 0.24f);
                case PotionType.Phase: return new Color(0.23f, 0.42f, 0.25f);
                case PotionType.Reflection: return new Color(0.30f, 0.51f, 0.74f);
                case PotionType.Counterspell: return new Color(0.39f, 0.88f, 0.85f);
                case PotionType.Curse: return new Color(0.5f, 0f, 0.5f);
                default: return Color.white;
            }
        }
    }
}
