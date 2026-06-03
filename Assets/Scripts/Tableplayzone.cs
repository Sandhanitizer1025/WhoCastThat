using UnityEngine;

public class TablePlayZone : MonoBehaviour
{
    public PotionGameManager gameManager;

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object entering the zone is a Potion
        Potion playedPotion = other.GetComponent<Potion>();
        
        if (playedPotion != null)
        {
            // Send the card data to the game manager to trigger UI and effects
            gameManager.PlayPotion(playedPotion);
        }
    }
}