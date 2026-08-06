using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Removes dead subscribers from the template's STATIC BindableVariables when a scene unloads.
    ///
    /// THE BUG THIS EXISTS FOR:
    /// XRINetworkGameManager.Connected is a static that outlives every scene, and two template
    /// components subscribe to it without ever really unsubscribing:
    ///
    ///   CharacterResetter   - subscribes in Awake, has no OnDisable/OnDestroy at all.
    ///   OfflinePlayerAvatar - "unsubscribes" in OnDisable with a NEWLY ALLOCATED lambda, and
    ///                         Unsubscribe matches on delegate identity, so it removes nothing.
    ///
    /// Both live on the XR rig, which every scene in this project contains. So the moment any
    /// scene unloads, the static keeps callbacks pointing at destroyed objects. The next scene's
    /// XRINetworkGameManager.Awake sets m_Connected.Value = false, that broadcast reaches a dead
    /// subscriber, and Awake dies with a MissingReferenceException -- BEFORE it reaches the
    /// authentication block. UnityServices is then never initialised, so creating or joining a
    /// room fails with a NullReferenceException deep inside SessionManager, which looks nothing
    /// like the actual cause.
    ///
    /// Only DEAD subscribers are removed: a delegate whose Target is a UnityEngine.Object that
    /// Unity reports as destroyed. Live subscribers in the incoming scene are untouched.
    /// </summary>
    public static class StaleSubscriptionScrubber
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(Scene scene)
        {
            int removed = ScrubAll();
            if (removed > 0)
            {
                Debug.Log($"[LobbyFlow] Dropped {removed} dead subscriber(s) left behind by '{scene.name}'.");
            }
        }

        /// <summary>
        /// Scrubs every static BindableVariable that survives a scene load.
        /// </summary>
        public static int ScrubAll()
        {
            int removed = 0;
            removed += Scrub(XRINetworkGameManager.Connected);
            removed += Scrub(XRINetworkGameManager.LocalPlayerName);
            removed += Scrub(XRINetworkGameManager.LocalPlayerColor);
            return removed;
        }

        /// <summary>
        /// Removes callbacks whose owning object has been destroyed.
        /// </summary>
        static int Scrub(object bindable)
        {
            if (bindable == null)
            {
                return 0;
            }

            FieldInfo field = FindEventField(bindable.GetType());
            if (field == null)
            {
                return 0;
            }

            Delegate current = field.GetValue(bindable) as Delegate;
            if (current == null)
            {
                return 0;
            }

            int removed = 0;
            Delegate[] subscribers = current.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                // A destroyed MonoBehaviour is not a real null reference -- it only compares
                // equal to null through Unity's overloaded operator, so the cast plus the
                // UnityEngine.Object comparison is what actually detects it.
                UnityEngine.Object owner = subscribers[i].Target as UnityEngine.Object;
                if (owner == null && subscribers[i].Target != null)
                {
                    current = Delegate.Remove(current, subscribers[i]);
                    removed++;
                }
            }

            if (removed > 0)
            {
                field.SetValue(bindable, current);
                DecrementBindingCount(bindable, removed);
            }

            return removed;
        }

        // The event lives on the generic base (BindableVariableBase<T>), not the concrete type.
        static FieldInfo FindEventField(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("valueUpdated", flags);
                if (f != null)
                {
                    return f;
                }
            }
            return null;
        }

        // Keeps BindingCount honest; SetValueWithoutNotify early-outs on it being zero.
        static void DecrementBindingCount(object bindable, int by)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            for (Type t = bindable.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("m_BindingCount", flags);
                if (f == null)
                {
                    continue;
                }
                int count = (int)f.GetValue(bindable);
                f.SetValue(bindable, Mathf.Max(0, count - by));
                return;
            }
        }
    }
}
