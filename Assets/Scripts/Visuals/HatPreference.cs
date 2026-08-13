using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// Remembers the hat the player picked, for the whole session and beyond.
    ///
    /// The colour itself lives in <c>XRINetworkGameManager.LocalPlayerColor</c>, which is a static
    /// bindable and so survives a scene load on its own — but only until something resets it, and
    /// the network manager sets it from its own defaults when it initialises. Saving to PlayerPrefs
    /// and re-applying on every scene load is what makes the choice actually stick from the lobby
    /// through the tutorial and into a match.
    /// </summary>
    public static class HatPreference
    {
        private const string Key = "WhoCastThat.HatColour";

        /// <summary>Store the choice and apply it immediately.</summary>
        public static void Set(Color colour)
        {
            XRINetworkGameManager.LocalPlayerColor.Value = colour;
            PlayerPrefs.SetString(Key, ColorUtility.ToHtmlStringRGB(colour));
            PlayerPrefs.Save();
        }

        /// <summary>The saved choice, or null if the player has never picked one.</summary>
        public static bool TryGet(out Color colour)
        {
            colour = Color.white;

            string stored = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            return ColorUtility.TryParseHtmlString("#" + stored, out colour);
        }

        /// <summary>
        /// Push the saved choice back onto the live colour. Safe to call repeatedly — it is a
        /// no-op when nothing was ever chosen, so a player who has not visited the customise
        /// panel keeps whatever default the template gave them.
        /// </summary>
        public static void Reapply()
        {
            if (TryGet(out Color colour))
            {
                XRINetworkGameManager.LocalPlayerColor.Value = colour;
            }
        }
    }
}
