using UnityEngine;

namespace WhoCastThat.Flow
{
    /// <summary>
    /// What the player asked for at the magic mirror.
    /// </summary>
    public enum LobbyRequest
    {
        None,
        QuickPlay,
        Create,
        Join
    }

    /// <summary>
    /// Carries the player's lobby choice across the scene load into the game scene.
    ///
    /// WHY A STATIC AND NOT A LIVE SESSION:
    /// <see cref="XRMultiplayer.XRINetworkGameManager"/> is an ordinary scene object with no
    /// DontDestroyOnLoad, and its OnDestroy calls ShutDown() -> SessionManager.LeaveSession().
    /// So a session created in the lobby is torn down the instant the lobby scene unloads.
    /// Rather than fight that, the lobby stays OFFLINE: it records the intent here, loads the
    /// game scene, and the game scene makes the one and only connection.
    /// LeaveSession() is null-guarded, so the lobby's own manager unloading is a harmless no-op.
    /// </summary>
    public static class LobbyIntent
    {
        public static LobbyRequest Request { get; private set; }
        public static string RoomName { get; private set; }
        public static string RoomCode { get; private set; }

        public static bool HasPending
        {
            get { return Request != LobbyRequest.None; }
        }

        public static void SetQuickPlay()
        {
            Request = LobbyRequest.QuickPlay;
            RoomName = null;
            RoomCode = null;
        }

        public static void SetCreate(string roomName)
        {
            Request = LobbyRequest.Create;
            RoomName = roomName;
            RoomCode = null;
        }

        public static void SetJoin(string roomCode)
        {
            Request = LobbyRequest.Join;
            RoomName = null;
            RoomCode = roomCode;
        }

        /// <summary>
        /// Reads the pending request and clears it, so a later return to the lobby starts fresh
        /// and a scene reload cannot silently reconnect using a stale choice.
        /// </summary>
        public static LobbyRequest Consume(out string roomName, out string roomCode)
        {
            LobbyRequest request = Request;
            roomName = RoomName;
            roomCode = RoomCode;
            Clear();
            return request;
        }

        public static void Clear()
        {
            Request = LobbyRequest.None;
            RoomName = null;
            RoomCode = null;
        }

        // --- failure reporting back to the mirror -------------------------------------------

        /// <summary>
        /// Why the last attempt to reach a room failed, carried back to the lobby so the player
        /// is told what went wrong instead of just finding themselves at the mirror again.
        /// </summary>
        public static string LastError { get; private set; }

        public static void SetError(string reason)
        {
            LastError = reason;
        }

        public static string ConsumeError()
        {
            string reason = LastError;
            LastError = null;
            return reason;
        }

        // Written by FirebaseLoginManager on a successful login/sign-up in BootScene.
        const string LocalUsernameKey = "PlayerUsername";

        /// <summary>
        /// Room name built from the signed-in player's name, shared by Host and Quick Play.
        /// </summary>
        public static string SuggestedRoomName()
        {
            string saved = PlayerPrefs.GetString(LocalUsernameKey, "");
            return (string.IsNullOrEmpty(saved) ? "Mage" : saved) + "'s Room";
        }
    }
}
