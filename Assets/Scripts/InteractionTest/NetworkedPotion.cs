using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

        [Header("Label")]
        [Tooltip("Height above the potion's centre where the hover tooltip / draw reveal floats.")]
        [SerializeField] private float labelHeight = 0.11f;

        [Header("Return to rack")]
        [Tooltip("Grace period after release before the potion snaps home. Must outlast a throw " +
                 "across the table, or a potion thrown at the play zone is yanked back mid-flight " +
                 "and the cast never happens.")]
        [SerializeField] private float returnGraceSeconds = 1.5f;

        [Tooltip("Seconds for a dropped potion to fly back to its rack slot.")]
        [SerializeField] private float returnFlightSeconds = 0.35f;

        [Header("Castable highlight")]
        [Tooltip("Glow a potion the local player could legally cast right now — including out " +
                 "of turn, so a Dispel or Reflection announces itself during someone else's turn.")]
        [SerializeField] private bool showCastableGlow = true;

        [Tooltip("Colour of the castable glow.")]
        [SerializeField] private Color castableGlowColor = new(0.35f, 1f, 0.45f, 0.85f);

        [Tooltip("Diameter of the glow pad at the tube's base, in metres. Kept well under the " +
                 "0.083 m rack slot pitch so neighbouring pads stay visually separate.")]
        [SerializeField] private float glowRingDiameter = 0.048f;

        [Tooltip("How far up from the tube's base the pad sits, in metres. Must clear the game " +
                 "manager's slotInsertionDepth (0.045) so the pad is not buried inside the rack.")]
        [SerializeField] private float glowRingRise = 0.046f;

        [Tooltip("Seconds between castable re-checks. The answer only changes on turn/curse events.")]
        [SerializeField] private float glowCheckInterval = 0.15f;

        // Fake-liquid shadergraph colour properties.
        private static readonly int SideColourId = Shader.PropertyToID("_SideColour");
        private static readonly int TopColourId = Shader.PropertyToID("_TopColour");
        private MaterialPropertyBlock tintBlock;

        private Rigidbody body;
        private XRGrabInteractable grab;
        private PotionLabel label;
        private Coroutine returnRoutine;
        private bool hovered;

        private Renderer glowRenderer;
        private bool glowVisible;
        private float nextGlowCheck;

        // Owner-write so the authority can set the type when it spawns the potion.
        private readonly NetworkVariable<int> networkedType = new(
            (int)PotionType.Counterspell,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        // Where this potion belongs. Replicated because the player who grabs the potion takes
        // ownership of it, and it is that player's client which flies it home on a bad drop.
        private readonly NetworkVariable<Vector3> homePosition = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Quaternion> homeRotation = new(
            Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> homeSet = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Which seat's hand this potion belongs to. This has to be replicated: RackSeat below
        // is authority-only bookkeeping and reads -1 on every other client, so it cannot tell
        // a client whether the potion it is pointing at is its own.
        private readonly NetworkVariable<int> ownerSeat = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Seat whose hand this potion belongs to, or -1 if unassigned.</summary>
        public int OwnerSeat => ownerSeat.Value;

        /// <summary>
        /// How many potions the LOCAL player is holding right now. Grab events only fire on
        /// the client doing the grabbing, so this is naturally per-client. <see cref="StirZone"/>
        /// reads it to refuse a dip while a potion is in hand: reaching over the pot to drop a
        /// potion in the middle of the table would otherwise cast and draw in one motion.
        /// </summary>
        public static int LocallyHeldCount { get; private set; }

        // Guards the counter against double-counting, and against leaking a count if the
        // potion is despawned mid-grab (a cast despawns the potion while it is still held).
        private bool countedAsHeld;

        /// <summary>The spell this potion represents.</summary>
        public PotionType Type => (PotionType)networkedType.Value;

        /// <summary>Raised on every client whenever the type is (re)applied.</summary>
        public event Action<PotionType> TypeApplied;

        /// <summary>
        /// True once this potion has been submitted to the play zone and is awaiting the
        /// authority's verdict. Stops the zone re-submitting it every frame, and stops the
        /// magnet yanking it home while the cast is still being judged.
        /// </summary>
        public bool CastSubmitted { get; set; }

        // ---- Authority-side rack bookkeeping ----
        // Only the authority manages racks, so these stay plain (unreplicated) fields.

        /// <summary>Seat whose rack this potion is sitting in, or -1 if not racked.</summary>
        public int RackSeat { get; set; } = -1;

        /// <summary>Slot index within that rack, or -1 if not racked.</summary>
        public int RackSlot { get; set; } = -1;

        /// <summary>Authority-only: set the potion's type right after spawning it.</summary>
        public void SetType(PotionType type)
        {
            if (HasAuthority)
            {
                networkedType.Value = (int)type;
            }
        }

        /// <summary>Authority-only: record which seat's hand this potion belongs to.</summary>
        public void SetOwnerSeat(int seat)
        {
            if (HasAuthority)
            {
                ownerSeat.Value = seat;
            }
        }

        /// <summary>
        /// Is this potion in the local player's own hand? Used to keep a player from reading
        /// an opponent's potions — hover fires on the pointing player's client regardless of
        /// whose rack the potion is sitting in.
        /// </summary>
        // Kept in one place so the counter can never drift: every path that stops the local
        // player holding this potion (release, despawn, disable) routes through here.
        private void SetHeldByLocalPlayer(bool held)
        {
            if (held == countedAsHeld)
            {
                return;
            }

            countedAsHeld = held;
            LocallyHeldCount = Mathf.Max(0, LocallyHeldCount + (held ? 1 : -1));
        }

        // Re-checked on a slow tick rather than every frame: the answer only changes when the
        // turn, a curse, or the interrupt window changes, and this runs on every potion on the
        // table. The stagger from each potion's own spawn time spreads the cost out.
        private void Update()
        {
            if (!showCastableGlow || Time.time < nextGlowCheck)
            {
                return;
            }

            nextGlowCheck = Time.time + Mathf.Max(0.02f, glowCheckInterval);

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            bool castable = game != null
                            && BelongsToLocalPlayer()
                            && !CastSubmitted
                            && game.CanLocalPlayerCastNow(Type);

            SetGlow(castable);
        }

        private void SetGlow(bool on)
        {
            if (on == glowVisible)
            {
                return;
            }

            glowVisible = on;

            if (on && glowRenderer == null)
            {
                BuildGlow();
            }

            if (glowRenderer != null)
            {
                glowRenderer.gameObject.SetActive(on);
            }
        }

        /// <summary>
        /// A lit pad on the rack surface at the foot of the tube, echoing the ring PlayZone draws
        /// on the table. Built as its own unlit object rather than driving emission on the liquid:
        /// that material is a bespoke shadergraph (_SideColour/_TopColour) with no emission channel
        /// to push, so a property block would silently do nothing.
        ///
        /// It deliberately does NOT wrap the tube. A shell around the glass — solid or inverted-hull
        /// — was measured against all nine liquid colours and destroys the colour coding that
        /// identifies a potion: Counterspell (mint) and Phase (green) all but vanish into a green
        /// shell, and every other type reads washed out. The tube is transparent and writes no
        /// depth, so a surrounding hull shows through the glass instead of hugging the silhouette;
        /// no thickness makes that read as an outline. Marking the potion from outside keeps the
        /// liquid untouched. Swap for real art later, but keep the cue off the glass.
        /// </summary>
        private void BuildGlow()
        {
            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

            Collider shellCollider = shell.GetComponent<Collider>();
            if (shellCollider != null)
            {
                Destroy(shellCollider); // must never block a grab or the XR ray
            }

            shell.name = "CastableGlow";
            shell.transform.SetParent(transform, false);
            shell.transform.localRotation = Quaternion.identity;

            // Height comes from the collider so the pad keeps sitting at the tube's foot if the
            // tube is ever re-scaled. The collider lives on a child, not the root.
            float height = 0.1205f;
            if (GetComponentInChildren<BoxCollider>() is BoxCollider box)
            {
                height = box.size.y;
            }

            shell.transform.localPosition = new Vector3(0f, (-height * 0.5f) + glowRingRise, 0f);

            // The cylinder primitive is 1 unit across and 2 tall, hence the halved Y for a slab.
            shell.transform.localScale = new Vector3(glowRingDiameter, 0.0004f, glowRingDiameter);

            var renderer = shell.GetComponent<Renderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                unlit = Shader.Find("Unlit/Color");
            }

            if (unlit != null)
            {
                var mat = new Material(unlit);

                // _Surface/_Blend alone do NOT make a URP material transparent. Those are editor-
                // facing toggles that the material inspector turns into render state via
                // ValidateMaterial; a material built in code never runs that, so it keeps the
                // opaque One/Zero blend and the colour's alpha is ignored entirely. The glow
                // rendered as a solid slab because of this. Set the blend state explicitly.
                mat.SetFloat("_Surface", 1f); // transparent
                mat.SetFloat("_Blend", 0f);   // alpha blend
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.color = castableGlowColor;
                renderer.sharedMaterial = mat;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            glowRenderer = renderer;
        }

        private bool BelongsToLocalPlayer()
        {
            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null)
            {
                return false;
            }

            int localSeat = game.LocalSeatIndex;
            return localSeat >= 0 && ownerSeat.Value == localSeat;
        }

        /// <summary>Authority-only: record the rack slot this potion returns to when dropped.</summary>
        public void SetHome(Vector3 position, Quaternion rotation)
        {
            if (!HasAuthority)
            {
                return;
            }

            homePosition.Value = position;
            homeRotation.Value = rotation;
            homeSet.Value = true;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
        }

        public override void OnNetworkSpawn()
        {
            networkedType.OnValueChanged += OnTypeChanged;

            if (grab != null)
            {
                // A racked potion is frozen (see SetRacked). Grabbing it must always hand it
                // back to physics, otherwise XRGrabInteractable restores the kinematic flag it
                // cached on the first grab and the potion hangs in mid-air when released.
                grab.selectEntered.AddListener(OnGrabbed);
                grab.selectExited.AddListener(OnReleased);

                // Hover fires only for the LOCAL player's interactors, so the tooltip is only
                // ever seen by whoever is pointing. That alone does NOT make it private
                // though — pointing at an opponent's rack still fires it — so OnHoverEntered
                // additionally checks the potion is one of yours.
                grab.hoverEntered.AddListener(OnHoverEntered);
                grab.hoverExited.AddListener(OnHoverExited);
            }

            ApplyVisual();
        }

        public override void OnNetworkDespawn()
        {
            // A cast despawns the potion while it is still in the player's hand, so the held
            // count has to be released here as well as on a normal drop.
            SetHeldByLocalPlayer(false);

            networkedType.OnValueChanged -= OnTypeChanged;

            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnGrabbed);
                grab.selectExited.RemoveListener(OnReleased);
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
            }

            // Let the authority free the rack slot this potion was occupying.
            if (NetworkedSpellGame.Instance != null)
            {
                NetworkedSpellGame.Instance.NotifyPotionDespawned(this);
            }
        }

        // ---- Label ----

        private PotionLabel Label()
        {
            if (label == null)
            {
                label = PotionLabel.Create(transform, labelHeight);
            }
            return label;
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            // Your own potions only. Hover fires on whichever client is pointing, so without
            // this a player could sweep a ray across an opponent's rack and read their hand.
            if (!BelongsToLocalPlayer())
            {
                return;
            }

            hovered = true;
            Label().Show(PotionInfo.Tooltip(Type));
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            hovered = false;
            Label().Hide();
        }

        /// <summary>
        /// Show everyone what was just brewed. Called by the authority while the potion hangs
        /// over the cauldron, before it floats to the rack.
        ///
        /// The type is passed explicitly rather than read from <see cref="Type"/>: the RPC and
        /// the type's NetworkVariable update are separate messages with no guaranteed ordering,
        /// so a client could otherwise render the reveal for the previous/default type.
        /// </summary>
        [Rpc(SendTo.Everyone)]
        public void RevealRpc(int potionType, float seconds)
        {
            Label().ShowFor(PotionInfo.DrawBanner((PotionType)potionType), seconds);
        }

        // ---- Grab / release ----

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            SetHeldByLocalPlayer(true);
            CastSubmitted = false;
            if (returnRoutine != null)
            {
                StopCoroutine(returnRoutine);
                returnRoutine = null;
            }
            SetRacked(false);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            SetHeldByLocalPlayer(false);
            SetRacked(false);

            if (returnRoutine != null)
            {
                StopCoroutine(returnRoutine);
            }
            returnRoutine = StartCoroutine(ReturnHomeUnlessPlayed());
        }

        /// <summary>
        /// Authority-only: the cast was rejected (wrong turn, nothing to answer), so send the
        /// potion back rather than destroying it. Routed to the owner because that is the
        /// client currently driving the potion's transform.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void ReturnHomeRpc()
        {
            CastSubmitted = false;
            if (returnRoutine != null)
            {
                StopCoroutine(returnRoutine);
            }
            returnRoutine = StartCoroutine(FlyHome());
        }

        // A potion dropped anywhere other than the play zone belongs back in its rack. Without
        // this, a fumbled grab throws it onto the floor and the player has silently lost a card
        // they can never retrieve (racks are out of reach once seated).
        private IEnumerator ReturnHomeUnlessPlayed()
        {
            float elapsed = 0f;
            while (elapsed < returnGraceSeconds)
            {
                elapsed += Time.deltaTime;
                if (!IsSpawned)
                {
                    yield break;
                }
                yield return null;
            }

            // Being cast, or already judged and awaiting despawn — leave it alone.
            if (CastSubmitted)
            {
                yield break;
            }
            if (PlayZone.Instance != null && PlayZone.Instance.Contains(this))
            {
                yield break;
            }

            yield return FlyHome();
        }

        private IEnumerator FlyHome()
        {
            if (!homeSet.Value)
            {
                yield break; // never seated (e.g. spawned loose) — nothing to return to
            }

            Vector3 from = transform.position;
            Quaternion fromRot = transform.rotation;
            Vector3 to = homePosition.Value;
            Quaternion toRot = homeRotation.Value;

            if (body != null)
            {
                // Zero the velocities BEFORE going kinematic: a kinematic body rejects velocity
                // writes and Unity logs an error for each one, which spammed the console on
                // every magnet return. Same order as SetRacked below.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, returnFlightSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float eased = u * u * (3f - 2f * u); // smoothstep

                transform.position = Vector3.Lerp(from, to, eased);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, eased);
                if (body != null)
                {
                    body.position = transform.position;
                }
                yield return null;
            }

            transform.position = to;
            transform.rotation = toRot;
            if (body != null)
            {
                body.position = to;
            }

            SetRacked(true);
            returnRoutine = null;
        }

        /// <summary>
        /// Freeze a potion that is seated in a rack slot.
        ///
        /// The rack's slots are deep wells (~0.18 m) and the tube is only ~0.12 m tall, so
        /// a potion resting on the well floor is swallowed completely out of sight. Instead
        /// the game seats it high in the well — protruding above the rim where it can be
        /// seen and grabbed — and freezes it there. This also stops racked potions being
        /// knocked over or vibrating against their neighbours.
        /// </summary>
        public void SetRacked(bool racked)
        {
            if (body == null)
            {
                return;
            }

            if (racked)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }
            else
            {
                if (body.isKinematic)
                {
                    body.isKinematic = false;
                    // Kinematic position-writes leave implicit velocity behind; clearing it
                    // stops the potion being flung the instant physics takes back over.
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = true;

                // RackSeat/RackSlot deliberately survive a grab: the slot stays reserved
                // while the player is holding the potion, and is only released when the
                // potion actually despawns (i.e. it was cast). Clearing it here would leak
                // the slot and slowly shrink the player's usable rack.
            }
        }

        /// <summary>
        /// Ask this potion's owner to despawn it. Used when the authority needs to remove a
        /// potion it does not own (e.g. a potion a player has grabbed).
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void DespawnRpc()
        {
            NetworkObject netObj = NetworkObject;
            if (netObj != null && netObj.IsSpawned && netObj.IsOwner)
            {
                netObj.Despawn();
            }
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

            // Keep an open tooltip in step if the type replicates late.
            if (hovered && label != null)
            {
                label.Show(PotionInfo.Tooltip(Type));
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
