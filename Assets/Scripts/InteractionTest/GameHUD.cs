using TMPro;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// World-space HUD that shows the shared game status (whose turn / last spell)
    /// to every player. Purely presentational — it subscribes to the game's static
    /// events, so teammates can restyle or replace it without touching gameplay code.
    /// Put this on a world-space Canvas above the table.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Tooltip("Text element that displays the current announcement / whose turn it is.")]
        [SerializeField] private TMP_Text announcementText;

        private void OnEnable()
        {
            NetworkedSpellGame.AnnouncementChanged += OnAnnouncementChanged;

            // Show current state immediately for late joiners.
            if (NetworkedSpellGame.Instance != null)
            {
                OnAnnouncementChanged(NetworkedSpellGame.Instance.CurrentAnnouncement);
            }
            else
            {
                OnAnnouncementChanged("Waiting for the game to start...");
            }
        }

        private void OnDisable()
        {
            NetworkedSpellGame.AnnouncementChanged -= OnAnnouncementChanged;
        }

        private void OnAnnouncementChanged(string text)
        {
            if (announcementText != null)
            {
                announcementText.text = text;
            }
        }
    }
}
