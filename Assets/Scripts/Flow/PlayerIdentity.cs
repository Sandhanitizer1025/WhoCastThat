using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// Carries the signed-in username from BootScene into the networked session.
    ///
    /// Everything downstream of this already exists in the template and needs no changes:
    /// <c>XRINetworkPlayer</c> copies <c>XRINetworkGameManager.LocalPlayerName</c> into a
    /// replicated NetworkVariable when it spawns and keeps it in step afterwards,
    /// <c>PlayerNameTag</c> renders that above the avatar's head, the player list reads it, and
    /// <c>NetworkedSpellGame.PlayerLabel</c> already prefers it over the generic "Player N".
    /// So the ONLY thing that was missing is somebody assigning it — until now every player
    /// travelled under the template's default, which is the literal string "Player".
    ///
    /// Applied just before connecting rather than once at startup, because the template's own
    /// OfflineMenu writes its default into the same variable when the lobby builds. Whoever
    /// writes last wins, and the value only actually matters at the moment XRINetworkPlayer
    /// spawns — which is after the connection.
    /// </summary>
    public static class PlayerIdentity
    {
        /// <summary>Written by FirebaseLoginManager on a successful login or sign-up.</summary>
        public const string LocalUsernameKey = "PlayerUsername";

        /// <summary>The signed-in username, or empty if this process never logged in.</summary>
        public static string SavedUsername => PlayerPrefs.GetString(LocalUsernameKey, "");

        /// <summary>
        /// Push the saved username into the template's networked name. Safe to call repeatedly.
        /// Returns true if it actually changed anything, which is only useful for logging.
        /// </summary>
        public static bool Apply()
        {
            string saved = SavedUsername;

            // No username means this process came straight into the game scene for rules testing
            // rather than through BootScene. Leaving the template default alone is right: making
            // up a name here would put a fake identity on the table.
            if (string.IsNullOrWhiteSpace(saved))
            {
                return false;
            }

            if (XRINetworkGameManager.LocalPlayerName == null ||
                XRINetworkGameManager.LocalPlayerName.Value == saved)
            {
                return false;
            }

            XRINetworkGameManager.LocalPlayerName.Value = saved;
            Debug.Log($"[LobbyFlow] Player name set to '{saved}' from the signed-in account.");
            return true;
        }
    }
}
