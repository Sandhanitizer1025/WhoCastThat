// ============================================================
//  PotInteraction.cs
//
//  Attach to your "pot" GameObject (which must have a Trigger
//  Collider — e.g. a sphere collider set to Is Trigger).
//
//  When the player's hand enters the pot:
//    1. A random TubeData is drawn from DeckManager
//    2. The matching physical test tube prefab spawns at the
//       hand position and is auto-grabbed by the XR interactor
//
//  Inspector setup:
//    • tubePrefabs[0..8]  — assign your 9 tube GameObjects
//                           IN ORDER matching TubeType enum:
//                           0=Hex(brown) 1=Tribute(yellow)
//                           2=Dispel(red) 3=Foresight(pink)
//                           4=Warp(grey) 5=Phase(green)
//                           6=Reflection(blue) 7=Counterspell(cyan)
//                           8=Curse(dark purple)
//    • handTag            — tag on your XR hand collider (default "PlayerHand")
//    • spawnOffset        — where tube appears relative to hand (default zero)
// ============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace WhocastThat
{
    [RequireComponent(typeof(Collider))]
    public class PotInteraction : MonoBehaviour
    {
        [Header("Tube Prefabs (index = TubeType)")]
        [SerializeField] private GameObject[] tubePrefabs = new GameObject[9];

        [Header("Settings")]
        [SerializeField] private string handTag        = "PlayerHand";
        [SerializeField] private Vector3 spawnOffset   = Vector3.zero;
        [SerializeField] private float   cooldownSeconds = 1.5f; // prevent rapid spam

        // ── State ─────────────────────────────────────────────
        private bool      onCooldown       = false;
        private GameObject heldTubeObject  = null; // the currently spawned physical tube

        // ── VFX (optional) ───────────────────────────────────
        [Header("Optional VFX")]
        [SerializeField] private ParticleSystem drawParticles;

        // ═════════════════════════════════════════════════════
        private void Awake()
        {
            // Make sure collider is a trigger
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        // ═════════════════════════════════════════════════════
        //  TRIGGER — hand enters pot
        // ═════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            if (onCooldown)           return;
            if (!other.CompareTag(handTag)) return;
            if (heldTubeObject != null)    return; // already holding one

            // Draw a random tube from the deck
            var tubeData = DeckManager.Instance.DrawRandom();
            if (tubeData == null) return;

            // Spawn the matching physical prefab
            SpawnTubeInHand(tubeData, other.transform);
        }

        // ═════════════════════════════════════════════════════
        //  SPAWN
        // ═════════════════════════════════════════════════════

        private void SpawnTubeInHand(TubeData tubeData, Transform handTransform)
        {
            int typeIndex = (int)tubeData.Type;
            if (typeIndex < 0 || typeIndex >= tubePrefabs.Length || tubePrefabs[typeIndex] == null)
            {
                Debug.LogError($"[Pot] No prefab assigned for TubeType {tubeData.Type} (index {typeIndex}). " +
                               "Assign all 9 prefabs in the Inspector.");
                return;
            }

            // Spawn at hand position
            Vector3 spawnPos = handTransform.position + handTransform.TransformDirection(spawnOffset);
            var go = Instantiate(tubePrefabs[typeIndex], spawnPos, handTransform.rotation);
            go.name = $"Tube_{tubeData.Type}";

            // Attach TubeObject data tag so TablePlayZone knows which ability to fire
            var tag = go.GetComponent<TubeObject>() ?? go.AddComponent<TubeObject>();
            tag.Data = tubeData;

            // Auto-grab: force the XR interactor on the hand to select the new tube
            var interactor   = handTransform.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor>()
                            ?? handTransform.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor>();
            var interactable = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable>();

            if (interactor != null && interactable != null)
            {
                // Small delay so the object's physics settles before grab
                StartCoroutine(AutoGrab(interactor, interactable));
            }
            else
            {
                Debug.LogWarning("[Pot] IXRSelectInteractor or IXRSelectInteractable not found. " +
                                 "Tube spawned but not auto-grabbed. Add XRGrabInteractable to tube prefabs.");
            }

            heldTubeObject = go;
            drawParticles?.Play();

            Debug.Log($"[Pot] Spawned {tubeData.Type} in player's hand.");
            StartCoroutine(Cooldown());
        }

        private IEnumerator AutoGrab(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor, UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
        {
            yield return new WaitForFixedUpdate();
            interactionManager ??= FindObjectOfType<XRInteractionManager>();
            interactionManager?.SelectEnter(interactor, interactable);
        }

        private XRInteractionManager interactionManager;

        private IEnumerator Cooldown()
        {
            onCooldown = true;
            yield return new WaitForSeconds(cooldownSeconds);
            onCooldown = false;
        }

        // ── Called by TablePlayZone after the tube is played ──
        public void ClearHeldTube() => heldTubeObject = null;
    }
}