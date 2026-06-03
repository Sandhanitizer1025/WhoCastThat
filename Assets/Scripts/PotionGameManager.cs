using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PotionGameManager : MonoBehaviour
{
    [System.Serializable]
    public struct PotionPrefabMapping
    {
        public PotionType type;
        public GameObject prefab;
    }

    [Header("VR Scene Setup")]
    public List<PotionPrefabMapping> potionPrefabs; 
    public Transform[] rackSlots; // Array of 5 slots
    public TextMeshProUGUI uiText; 

    private List<PotionType> deck = new List<PotionType>();
    
    // Tracks exactly what GameObject is sitting in which slot index (0 to 4)
    private GameObject[] occupiedSlots; 

    void Start()
    {
        // Initialize our tracking array to match the number of physical slots
        occupiedSlots = new GameObject[rackSlots.Length];

        InitializeDeck();
        DealStartingHand();
    }

    void InitializeDeck()
    {
        for (int i = 0; i < 5; i++) deck.Add(PotionType.Hex);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Tribute);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Dispel);
        for (int i = 0; i < 5; i++) deck.Add(PotionType.Foresight);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Warp);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Phase);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Reflection);
        for (int i = 0; i < 6; i++) deck.Add(PotionType.Counterspell);
        for (int i = 0; i < 4; i++) deck.Add(PotionType.Curse);

        ShuffleDeck();
    }

    void ShuffleDeck()
    {
        for (int i = 0; i < deck.Count; i++)
        {
            PotionType temp = deck[i];
            int randomIndex = Random.Range(i, deck.Count);
            deck[i] = deck[randomIndex];
            deck[randomIndex] = temp;
        }
    }

    void DealStartingHand()
    {
        // Setup initial 5 cards into the tracking array
        SpawnPotionInRack(PotionType.Counterspell, 0);

        for (int i = 1; i < 5; i++)
        {
            PotionType randomType = PullNonCurseFromDeck();
            SpawnPotionInRack(randomType, i);
        }
        
        uiText.text = "Your Turn! Cast a spell by placing a potion in the center.";
    }

    PotionType PullNonCurseFromDeck()
    {
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i] != PotionType.Curse)
            {
                PotionType pulled = deck[i];
                deck.RemoveAt(i);
                return pulled;
            }
        }
        return PotionType.Counterspell; 
    }

    void SpawnPotionInRack(PotionType type, int slotIndex)
    {
        if (slotIndex >= rackSlots.Length) return;

        GameObject prefabToSpawn = GetPrefabForType(type);
        if (prefabToSpawn == null) return;

        // Spawn directly at the rack slot's transform coordinates
        GameObject newPotion = Instantiate(prefabToSpawn, rackSlots[slotIndex].position, rackSlots[slotIndex].rotation);
        
        Potion potionScript = newPotion.GetComponent<Potion>();
        if (potionScript == null) potionScript = newPotion.AddComponent<Potion>();
        potionScript.type = type; 

        // Save this potion into our tracking system at the correct index
        occupiedSlots[slotIndex] = newPotion;
    }

    GameObject GetPrefabForType(PotionType type)
    {
        foreach (var mapping in potionPrefabs)
        {
            if (mapping.type == type) return mapping.prefab;
        }
        return null;
    }

    // This gets called by the PotDrawZone script when your hand enters the cauldron
    public void DrawPotionFromPot(Vector3 handPosition, Quaternion handRotation)
    {
        if (deck.Count == 0)
        {
            uiText.text = "The cauldron is empty!";
            return;
        }

        // 1. Find the first empty slot on the rack
        int targetSlotIndex = -1;
        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (occupiedSlots[i] == null) // Found an open space!
            {
                targetSlotIndex = i;
                break;
            }
        }

        // 2. If no slots are null, the player's hand/rack is full
        if (targetSlotIndex == -1)
        {
            uiText.text = "<color=red>Hand Full!</color> You cannot hold more than 5 potions.";
            return;
        }

        // 3. Pull from deck and spawn it directly into that open slot
        PotionType drawnType = deck[0];
        deck.RemoveAt(0);

        SpawnPotionInRack(drawnType, targetSlotIndex);

        // 4. Update UI
        if (drawnType == PotionType.Curse)
        {
            uiText.text = "<color=purple>CRITICAL!</color> You drew a Curse directly to your rack! Counter it!";
        }
        else
        {
            uiText.text = $"Drew a <color=yellow>{drawnType}</color>! Sent automatically to rack slot {targetSlotIndex + 1}.";
        }
    }

    public void PlayPotion(Potion potion)
    {
        string message = "";

        switch (potion.type)
        {
            case PotionType.Hex: message = "<color=#663300>Hex</color>: Must play 2 turns!"; break;
            case PotionType.Tribute: message = "<color=#d1ce21>Tribute</color>: Target must give you a card."; break;
            case PotionType.Dispel: message = "<color=#b02727>Dispel</color>: Stopped the last action!"; break;
            case PotionType.Foresight: message = "<color=#ff36dd>Foresight</color>: Viewing top 3 potions secretly..."; break;
            case PotionType.Warp: message = "<color=#3d3d3d>Warp</color>: Shuffled the cauldron!"; ShuffleDeck(); break;
            case PotionType.Phase: message = "<color=#3b6b41>Phase</color>: Turn ended safely."; break;
            case PotionType.Reflection: message = "<color=#4d81bd>Reflection</color>: Copied the last cast spell!"; break;
            case PotionType.Counterspell: message = "<color=#63e0d8>Counterspell</color>: Saved from a curse!"; break;
            case PotionType.Curse: message = "<color=#800080>Curse</color>: YOU EXPLODED!"; break;
        }

        uiText.text = message;

        // 1. Find which slot this potion came from and clear it out so it's marked empty
        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (occupiedSlots[i] == potion.gameObject)
            {
                occupiedSlots[i] = null; // This slot is now open for a future draw!
                break;
            }
        }

        // 2. Destroy the physical bottle
        Destroy(potion.gameObject, 0.2f); 
    }
}