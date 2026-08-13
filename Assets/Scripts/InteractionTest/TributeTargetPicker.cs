using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// "Who pays the tribute?" — a row of named boxes that appears in front of the caster. Look at
    /// one to highlight it, pull the trigger to choose.
    ///
    /// This replaced picking the victim by aiming at their rack. Aiming could not work: a potion
    /// only counts as cast once it lands in the ring at the centre of the table, so the hand that
    /// releases it is always pointing down at the table rather than at anybody. Gaze alone could,
    /// but it commits the moment you let go, with no way to see who you are about to rob or to
    /// change your mind. A picker shows the choice and confirms it — and reuses the trigger press
    /// the cauldron already teaches for drawing.
    ///
    /// Local presentation only, built from code, so it needs no scene wiring. It is only ever
    /// shown on the caster's own client.
    /// </summary>
    public class TributeTargetPicker : MonoBehaviour
    {
        private const float Distance = 0.85f;   // metres in front of the player
        private const float Spacing = 0.32f;    // clears the ~0.27 m label panel with a gap
        private const float EyeDrop = 0.08f;    // sit just below the sight line, not over faces

        private static readonly Color IdleColour = new(0.05f, 0.03f, 0.10f, 0.82f);
        private static readonly Color HighlightColour = new(0.16f, 0.42f, 0.22f, 0.94f);

        private readonly List<PotionLabel> boxes = new();
        private readonly List<GameObject> anchors = new();

        private ulong[] candidates;
        private Action<ulong> onPicked;
        private int highlighted = -1;
        private XRBaseInputInteractor[] localInteractors;

        /// <summary>True while a choice is on screen and waiting for a trigger press.</summary>
        public bool Active => candidates != null && candidates.Length > 0;

        /// <summary>Prompt shown by the HUD while the picker is up.</summary>
        public static string Prompt { get; private set; }

        /// <summary>
        /// Put a row of labelled boxes in front of the player and call <paramref name="picked"/>
        /// with the id of whichever one they look at and trigger.
        ///
        /// The ids are opaque to this component, so this serves any "choose one of these" moment,
        /// not only Tribute — Curse placement passes the placement index in the same slot. Pass a
        /// <paramref name="prompt"/> to say what the choice is; it defaults to the Tribute wording.
        /// </summary>
        public void Show(ulong[] ids, string[] names, Action<ulong> picked, string prompt = null)
        {
            Hide();

            if (ids == null || ids.Length == 0)
            {
                return;
            }

            Camera view = Camera.main;
            if (view == null)
            {
                // No camera means no way to place or aim the boxes; let the authority's timeout
                // resolve it rather than leaving the player staring at nothing.
                return;
            }

            candidates = ids;
            onPicked = picked;
            Prompt = string.IsNullOrEmpty(prompt)
                ? "POINT at a mage and PRESS THE TRIGGER to take their potion"
                : prompt;

            Vector3 forward = view.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
            // Cross(up, forward) already points to the player's right in Unity's left-handed
            // space; negating it laid the boxes out right-to-left and reversed the order.
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            Vector3 centre = view.transform.position + (forward * Distance) - (Vector3.up * EyeDrop);
            float start = -(ids.Length - 1) * 0.5f * Spacing;

            for (int i = 0; i < ids.Length; i++)
            {
                var anchor = new GameObject($"TributeTarget_{i}");
                anchor.transform.SetParent(transform, false);
                anchor.transform.position = centre + (right * (start + (i * Spacing)));

                PotionLabel box = PotionLabel.Create(anchor.transform, 0f);
                box.Show(i < names.Length ? names[i] : $"Player {ids[i]}");
                box.SetPanelColor(IdleColour);

                anchors.Add(anchor);
                boxes.Add(box);
            }
        }

        public void Hide()
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i] != null)
                {
                    Destroy(anchors[i]);
                }
            }

            anchors.Clear();
            boxes.Clear();
            candidates = null;
            onPicked = null;
            highlighted = -1;
            Prompt = null;
        }

        private void Update()
        {
            if (!Active)
            {
                return;
            }

            Camera view = Camera.main;
            if (view == null)
            {
                return;
            }

            UpdateHighlight(view);

            if (highlighted >= 0 && TriggerPressedThisFrame())
            {
                ulong chosen = candidates[highlighted];
                Action<ulong> callback = onPicked;
                Hide();
                callback?.Invoke(chosen);
            }
        }

        // Whichever box the player POINTS most directly at wins. Angle rather than a raycast: the
        // boxes carry no colliders (one here would block the XR ray and the grab the player needs
        // for everything else), so there is nothing for a ray to hit.
        //
        // The aiming ray is the controller's, not the head's. Gaze selection meant you could not
        // look at what you were about to pick without also moving the selection, and it fought
        // the pointing the rest of the game already teaches. The head is only the fallback, for
        // the editor and for hand tracking, where there is no controller ray to read.
        private void UpdateHighlight(Camera view)
        {
            Transform aim = AimingTransform();
            Vector3 origin = aim != null ? aim.position : view.transform.position;
            Vector3 forward = aim != null ? aim.forward : view.transform.forward;

            int best = -1;
            float bestAngle = float.MaxValue;

            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i] == null)
                {
                    continue;
                }

                Vector3 toBox = anchors[i].transform.position - origin;
                float angle = Vector3.Angle(forward, toBox);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = i;
                }
            }

            // A tighter cone than gaze used, because pointing is far more precise than looking
            // and the boxes sit only ~18 degrees apart from arm's length.
            if (bestAngle > (aim != null ? 20f : 35f))
            {
                best = -1;
            }

            if (best == highlighted)
            {
                return;
            }

            highlighted = best;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] != null)
                {
                    boxes[i].SetPanelColor(i == highlighted ? HighlightColour : IdleColour);
                }
            }
        }

        /// <summary>
        /// The controller the player is aiming with, or null to fall back to the head.
        ///
        /// Prefers a ray interactor's own ray origin, which is the visible pointer the player is
        /// lining up — the interactor's transform can sit at the wrist and points somewhere
        /// slightly different. A hand already holding something is skipped, so a potion in one
        /// hand does not steal the aim from the empty one.
        /// </summary>
        private Transform AimingTransform()
        {
            RefreshInteractors();

            Transform fallback = null;

            for (int i = 0; i < localInteractors.Length; i++)
            {
                XRBaseInputInteractor interactor = localInteractors[i];
                if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection)
                {
                    continue;
                }

                if (interactor is XRRayInteractor ray)
                {
                    Transform origin = ray.rayOriginTransform != null
                        ? ray.rayOriginTransform
                        : ray.transform;

                    // A tracked controller ray wins outright; anything else is only a fallback.
                    if (ray.isActiveAndEnabled)
                    {
                        return origin;
                    }
                }

                fallback ??= interactor.transform;
            }

            return fallback;
        }

        private void RefreshInteractors()
        {
            // Re-found when the cached set has gone stale: interactors are enabled and disabled
            // as the player switches between controllers and hand tracking, and a destroyed one
            // left in this array would silently stop the picker aiming at all.
            if (localInteractors == null || localInteractors.Length == 0)
            {
                localInteractors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None);
                return;
            }

            for (int i = 0; i < localInteractors.Length; i++)
            {
                if (localInteractors[i] == null)
                {
                    localInteractors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None);
                    return;
                }
            }
        }

        // Same approach as StirZone's draw prompt: read the activate (trigger) action off the
        // local interactors. Interactors with a selection are skipped, so this cannot fire from
        // the hand that is still holding something.
        private bool TriggerPressedThisFrame()
        {
            RefreshInteractors();

            for (int i = 0; i < localInteractors.Length; i++)
            {
                XRBaseInputInteractor interactor = localInteractors[i];
                if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection)
                {
                    continue;
                }

                if (interactor.activateInput != null && interactor.activateInput.ReadWasPerformedThisFrame())
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            Hide();
        }
    }
}
