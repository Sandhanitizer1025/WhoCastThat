#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Scripted two-client casting test for MPPM. Editor-only and OFF unless the EditorPrefs
    /// switch below is set, so a normal play session never sees it.
    ///
    /// Why this exists rather than driving the game over MCP: the interrupt window is 3 seconds
    /// and an MCP round-trip is far longer, so a spell can never be answered from outside the
    /// process. Every wait here happens in-process, at the speed the game actually runs.
    ///
    /// It installs itself in EVERY process that has the switch on — main editor and clone alike —
    /// and each copy acts ONLY as its own player. That matters: RequestCast reads the acting
    /// player from the RPC sender, which a client cannot forge, so one process genuinely cannot
    /// puppet the other. Two cooperating drivers is the only way to script a real match.
    /// </summary>
    public class SpellCastAutoTest : MonoBehaviour
    {
        /// <summary>Shared by the main editor and every MPPM clone, which is what we want here.</summary>
        public const string EnabledKey = "WCT.SpellCastAutoTest";

        private const int MaxCasts = 6;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!UnityEditor.EditorPrefs.GetBool(EnabledKey, false))
            {
                return;
            }

            var go = new GameObject("SpellCastAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<SpellCastAutoTest>();
        }

        private static void L(string message)
        {
            Debug.Log($"[AutoTest] {message}");
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float deadline = Time.time + 120f;
            while (Time.time < deadline &&
                   (NetworkedSpellGame.Instance == null || !NetworkedSpellGame.Instance.GameActive))
            {
                yield return null;
            }

            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null || !game.GameActive)
            {
                L("ABORT: no active match within 120s.");
                yield break;
            }

            NetworkManager nm = NetworkManager.Singleton;
            L($"MATCH UP clientId={(nm != null ? nm.LocalClientId.ToString() : "?")} " +
              $"seat={game.LocalSeatIndex} authority={game.HasAuthority} " +
              $"currentSeat={game.CurrentSeatIndex}");

            // Wait for a SEAT and for potions to exist, not just for a fixed delay. A flat
            // 3 s wait let one client report "0 potions checked, 0 leaked" — a pass that had
            // examined nothing, which is worse than a failure because it looks like evidence.
            float seated = Time.time + 30f;
            while (Time.time < seated &&
                   (game.LocalSeatIndex < 0 ||
                    Object.FindObjectsByType<NetworkedPotion>(FindObjectsSortMode.None).Length == 0))
            {
                yield return null;
            }

            // Then let the deal finish arriving before judging what is on the table.
            yield return new WaitForSeconds(3f);

            if (game.LocalSeatIndex < 0)
            {
                L("CONCEALMENT SKIPPED: never got a seat, so there is nothing to check.");
            }
            else
            {
                ReportHandAndConcealment(game);
            }
            StartCoroutine(WatchInterruptWindow(game));

            int casts = 0;
            float stop = Time.time + 180f;

            while (casts < MaxCasts && game.GameActive && Time.time < stop)
            {
                // Answer someone else's spell if we are allowed to and actually hold the card.
                if (game.InterruptWindowOpen &&
                    game.LocalSeatIndex != game.CurrentSeatIndex &&
                    HoldsLocally(game, PotionType.Dispel))
                {
                    L($"ANSWERING with Dispel at {game.InterruptSecondsRemaining:0.00}s left");
                    game.RequestCast(PotionType.Dispel, ulong.MaxValue);
                    yield return new WaitForSeconds(1.5f);
                    continue;
                }

                if (game.LocalSeatIndex == game.CurrentSeatIndex && !game.InterruptWindowOpen)
                {
                    L($"MY TURN turnsRemaining={game.TurnsRemaining} " +
                      $"lastResolved={LastResolved(game)} " +
                      $"reflectionCastable={game.CanLocalPlayerCastNow(PotionType.Reflection)} " +
                      $"holdsDispel={HoldsLocally(game, PotionType.Dispel)}");

                    // Reflection when it is legal (proves it copies), Hex otherwise (proves the
                    // turnsRemaining + 2 sum and opens a window for the other client to answer).
                    PotionType cast = game.CanLocalPlayerCastNow(PotionType.Reflection) && casts > 0
                        ? PotionType.Reflection
                        : PotionType.Hex;

                    L($"CASTING {cast}");
                    game.RequestCast(cast, ulong.MaxValue);
                    casts++;

                    // Long enough to cover the interrupt window plus the resolve.
                    yield return new WaitForSeconds(7f);
                    L($"AFTER {cast}: currentSeat={game.CurrentSeatIndex} " +
                      $"turnsRemaining={game.TurnsRemaining} lastResolved={LastResolved(game)}");
                    continue;
                }

                yield return new WaitForSeconds(0.25f);
            }

            L($"DONE casts={casts} gameActive={game.GameActive} lastResolved={LastResolved(game)}");
        }

        // Samples the countdown while a window is open. This is the evidence that the replicated
        // deadline actually drains rather than just flipping a bool.
        private IEnumerator WatchInterruptWindow(NetworkedSpellGame game)
        {
            bool wasOpen = false;
            float nextSample = 0f;

            while (game != null)
            {
                bool open = game.InterruptWindowOpen;

                if (open && !wasOpen)
                {
                    L($"WINDOW OPENED secs={game.InterruptSecondsRemaining:0.00} " +
                      $"frac={game.InterruptWindowFraction:0.00}");
                    nextSample = Time.time;
                }
                else if (!open && wasOpen)
                {
                    L("WINDOW CLOSED");
                }

                if (open && Time.time >= nextSample)
                {
                    nextSample = Time.time + 0.5f;
                    L($"  window secs={game.InterruptSecondsRemaining:0.00} " +
                      $"frac={game.InterruptWindowFraction:0.00}");
                }

                wasOpen = open;
                yield return null;
            }
        }

        // Concealment check. This asserts on the RENDERED COLOUR, not just the GameObject name.
        // An earlier version checked only the name and reported a clean pass while every
        // opponent potion was still painted its true colour on screen: NetworkedPotion tints
        // the liquid and then PotionAura repaints the same renderer from the type profile. The
        // name is checked too, but the colour is what a player actually reads across the table.
        private void ReportHandAndConcealment(NetworkedSpellGame game)
        {
            NetworkedPotion[] potions =
                Object.FindObjectsByType<NetworkedPotion>(FindObjectsSortMode.None);

            int mine = 0;
            int others = 0;
            int leaked = 0;
            var hand = new System.Text.StringBuilder();

            foreach (NetworkedPotion p in potions)
            {
                bool isMine = game.LocalSeatIndex >= 0 && p.OwnerSeat == game.LocalSeatIndex;
                if (isMine)
                {
                    mine++;
                    if (hand.Length > 0)
                    {
                        hand.Append(", ");
                    }
                    hand.Append(p.Type);
                }
                else
                {
                    others++;
                    Color shown = LiquidColour(p);
                    bool nameOk = p.name.Contains("concealed");
                    bool colourOk = Close(shown, NetworkedPotion.ConcealedColour);

                    if (!nameOk || !colourOk)
                    {
                        leaked++;
                        L($"  LEAK: seat={p.OwnerSeat} name='{p.name}' nameOk={nameOk} " +
                          $"colourOk={colourOk} shown={shown} " +
                          $"typeColour={NetworkedPotion.ColorFor(p.Type)}");
                    }
                }
            }

            L($"CONCEALMENT seat={game.LocalSeatIndex} myPotions={mine} otherPotions={others} leaked={leaked}");
            L($"MY HAND: {hand}");
        }

        // The liquid renderer is private on NetworkedPotion, so find it the same way
        // ForesightDisplay does — by name convention on the visual child.
        private static Color LiquidColour(NetworkedPotion potion)
        {
            foreach (Renderer r in potion.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.gameObject.name.ToLower().Contains("liquid"))
                {
                    continue;
                }

                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                return block.GetColor(Shader.PropertyToID("_TopColour"));
            }
            return Color.clear;
        }

        private static bool Close(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f
                   && Mathf.Abs(a.g - b.g) < 0.02f
                   && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        private static bool HoldsLocally(NetworkedSpellGame game, PotionType type)
        {
            if (game.LocalSeatIndex < 0)
            {
                return false;
            }

            foreach (NetworkedPotion p in
                     Object.FindObjectsByType<NetworkedPotion>(FindObjectsSortMode.None))
            {
                if (p.OwnerSeat == game.LocalSeatIndex && p.Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        // Private replicated field: the whole point of the exclusion-list change is what does and
        // does not land here, so the test reads it directly rather than inferring it.
        private static string LastResolved(NetworkedSpellGame game)
        {
            FieldInfo f = typeof(NetworkedSpellGame).GetField(
                "lastResolvedSpell", BindingFlags.NonPublic | BindingFlags.Instance);

            if (f?.GetValue(game) is not NetworkVariable<int> nv)
            {
                return "?";
            }

            return nv.Value < 0 ? "none" : ((PotionType)nv.Value).ToString();
        }
    }
}
#endif
