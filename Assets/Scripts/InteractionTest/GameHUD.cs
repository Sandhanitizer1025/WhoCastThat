using TMPro;
using UnityEngine;
using XRMultiplayer;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// World-space HUD above the table. Shows two things:
    ///   1. the shared announcement (what just happened, same for everyone), and
    ///   2. a prompt written for THIS player — whether to cast, draw, answer a spell, or wait.
    ///
    /// The prompt is derived locally from replicated state rather than being broadcast, so
    /// each player is told what *they* should do without leaking anything private.
    /// Purely presentational — teammates can restyle or replace it without touching
    /// gameplay code.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Tooltip("Text element that displays the current announcement / whose turn it is.")]
        [SerializeField] private TMP_Text announcementText;

        [Tooltip("Optional second text element for the local player's prompt. If unset, the prompt is appended to the announcement text.")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("Turn the HUD to face the local player each frame. Off = the canvas keeps its authored facing.")]
        [SerializeField] private bool billboardToLocalPlayer = true;

        private string announcement = "Waiting for the game to start...";
        private string lastComposed;
        private Camera billboardTarget;

        private void OnEnable()
        {
            NetworkedSpellGame.AnnouncementChanged += OnAnnouncementChanged;

            // Show current state immediately for late joiners.
            if (NetworkedSpellGame.Instance != null)
            {
                announcement = NetworkedSpellGame.Instance.CurrentAnnouncement;
            }
            Compose(true);
        }

        private void OnDisable()
        {
            NetworkedSpellGame.AnnouncementChanged -= OnAnnouncementChanged;
        }

        private void OnAnnouncementChanged(string text)
        {
            announcement = text;
            Compose(false);
        }

        // The prompt depends on turn/curse/interrupt state that changes without an
        // announcement firing, so it is recomposed every frame. Cheap, and the text is only
        // pushed to TMP when it actually changes.
        private void Update()
        {
            Compose(false);
        }

        // The HUD is one flat canvas at the centre of a round table, so a fixed facing is
        // readable from exactly one of the four seats and edge-on or mirrored from the rest.
        // Each client turns its own copy toward its own camera — the transform is never
        // networked, so this costs nothing and cannot desync.
        private void LateUpdate()
        {
            if (!billboardToLocalPlayer)
            {
                return;
            }

            if (billboardTarget == null)
            {
                billboardTarget = Camera.main;
                if (billboardTarget == null)
                {
                    return;
                }
            }

            Vector3 toCamera = transform.position - billboardTarget.transform.position;
            toCamera.y = 0f; // keep the HUD upright; only yaw toward the player
            if (toCamera.sqrMagnitude < 1e-6f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        private void Compose(bool force)
        {
            string prompt = LocalPrompt();

            if (promptText != null)
            {
                if (announcementText != null)
                {
                    announcementText.text = announcement;
                }
                promptText.text = prompt;
                return;
            }

            string combined = string.IsNullOrEmpty(prompt)
                ? announcement
                : $"{announcement}\n{prompt}";

            if (force || combined != lastComposed)
            {
                lastComposed = combined;
                if (announcementText != null)
                {
                    announcementText.text = combined;
                }
            }
        }

        private string LocalPrompt()
        {
            NetworkedSpellGame game = NetworkedSpellGame.Instance;
            if (game == null || !game.GameActive)
            {
                return LobbyPrompt();
            }

            // Most urgent first: you are about to be destroyed.
            if (game.IsLocalPlayerCursed)
            {
                return "<color=#FF6B6B><b>YOU ARE CURSED</b> — drop a Counterspell on the table centre!</color>";
            }

            // A spell is on the table and anyone holding an answer may play it, in or out of turn.
            if (game.InterruptWindowOpen && !game.IsLocalPlayersTurn)
            {
                return "<color=#9AD0FF>A spell is resolving — drop a <b>Dispel</b> or <b>Reflection</b> to answer it.</color>";
            }

            if (game.IsLocalPlayersTurn)
            {
                // Hand already in the pot: the only thing left to say is how to confirm.
                if (StirZone.LocalHandCanDraw)
                {
                    return "<color=#FFE066><b>PRESS THE TRIGGER</b> to draw from the cauldron. " +
                           "<i>Drawing ends your turn.</i></color>";
                }

                string turns = game.TurnsRemaining > 1
                    ? $" <color=#FFD27F>({game.TurnsRemaining} turns to survive)</color>"
                    : "";
                return $"<color=#B6F5A6><b>YOUR TURN</b>{turns} — drop a potion on the ring to cast, " +
                       "or put a hand in the cauldron to draw. <i>Drawing ends your turn.</i></color>";
            }

            return $"<color=#B9A9D9>Waiting for {game.PlayerLabel(game.CurrentTurnClientId)}...</color>";
        }

        // Before the match starts this HUD would otherwise be blank — which is exactly the
        // moment the host needs to read their room code out to everyone else. Join-by-code
        // from the magic mirror is unusable if the code is never displayed anywhere.
        private string LobbyPrompt()
        {
            if (XRINetworkGameManager.Connected == null || !XRINetworkGameManager.Connected.Value)
            {
                return "<color=#B9A9D9>Connecting to the arcane network...</color>";
            }

            string code = XRINetworkGameManager.ConnectedRoomCode;
            if (string.IsNullOrEmpty(code))
            {
                return "<color=#B9A9D9>Waiting for other mages to arrive...</color>";
            }

            return $"<color=#B6F5A6><b>ROOM CODE: {code}</b></color>\n" +
                   "<color=#B9A9D9>Waiting for other mages to arrive...</color>";
        }
    }
}
