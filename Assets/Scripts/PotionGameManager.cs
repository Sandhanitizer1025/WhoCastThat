using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PotionGameManager : MonoBehaviour
{
    // A simple structural mapping to pair enums with their respective unique prefabs
    [System.Serializable]
    public struct PotionPrefabMapping
    {
        public PotionType type;
        public GameObject prefab;
    }

    [Header("VR Scene Setup")]
    public List<PotionPrefabMapping> potionPrefabs; // Assign all 9 unique prefabs here!
    public Transform[] rackSlots; 
    public TextMeshProUGUI uiText; 

    private List<PotionType> deck = new List<PotionType>();
    private List<GameObject> activeHand = new List<GameObject>();

    void Start()
    {
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
        Debug.Log($"[DEBUG] Attempting to spawn {type} at slot {slotIndex}");
        if (slotIndex >= rackSlots.Length) return;

        // Find the correct unique prefab for this potion type
        GameObject prefabToSpawn = GetPrefabForType(type);
        
        if (prefabToSpawn == null)
        {
            Debug.LogError($"Missing prefab mapping for Potion Type: {type}");
            return;
        }

        GameObject newPotion = Instantiate(prefabToSpawn, rackSlots[slotIndex].position, rackSlots[slotIndex].rotation);
        
        // Safety check: Ensure the prefab has the Potion component attached
        Potion potionScript = newPotion.GetComponent<Potion>();
        if (potionScript == null)
        {
            potionScript = newPotion.AddComponent<Potion>();
        }
        potionScript.type = type; 

        activeHand.Add(newPotion);
    }

    // Helper method to look up the correct prefab from your list
    GameObject GetPrefabForType(PotionType type)
    {
        foreach (var mapping in potionPrefabs)
        {
            if (mapping.type == type)
            {
                return mapping.prefab;
            }
        }
        return null;
    }

    public void PlayPotion(Potion potion)
    {
        string message = "";

        switch (potion.type)
        {
            case PotionType.Hex: message = "<color=#663300>Hex</color>: Must play 2 turns!"; break;
            case PotionType.Tribute: message = "<color=yellow>Tribute</color>: Target must give you a card."; break;
            case PotionType.Dispel: message = "<color=red>Dispel</color>: Stopped the last action!"; break;
            case PotionType.Foresight: message = "<color=pink>Foresight</color>: Viewing top 3 potions secretly..."; break;
            case PotionType.Warp: message = "<color=grey>Warp</color>: Shuffled the cauldron!"; ShuffleDeck(); break;
            case PotionType.Phase: message = "<color=green>Phase</color>: Turn ended safely."; break;
            case PotionType.Reflection: message = "<color=blue>Reflection</color>: Copied the last cast spell!"; break;
            case PotionType.Counterspell: message = "<color=cyan>Counterspell</color>: Saved from a curse!"; break;
            case PotionType.Curse: message = "<color=purple>Curse</color>: YOU EXPLODED!"; break;
        }

        uiText.text = message;
        activeHand.Remove(potion.gameObject);
        Destroy(potion.gameObject, 0.2f); 
    }

    public void DrawPotionFromPot(Vector3 spawnPosition, Quaternion spawnRotation)
    {
    // 1. Safety check: Is the deck empty?
    if (deck.Count == 0)
    {
        uiText.text = "The cauldron is empty! No more potions left.";
        return;
    }

    // 2. Pull the top card (since it's already shuffled, this is completely random!)
    PotionType drawnType = deck[0];
    deck.RemoveAt(0); // Remove it so it can't be drawn again

    // 3. Find the correct prefab for this potion type
    GameObject prefabToSpawn = GetPrefabForType(drawnType);
    if (prefabToSpawn == null)
    {
        Debug.LogError($"Missing prefab mapping for drawn type: {drawnType}");
        return;
    }

    // 4. Spawn it directly at the player's hand controller position
    GameObject newPotion = Instantiate(prefabToSpawn, spawnPosition, spawnRotation);
    
    // Ensure the Potion script component is set up right
    Potion potionScript = newPotion.GetComponent<Potion>();
    if (potionScript == null) potionScript = newPotion.AddComponent<Potion>();
    potionScript.type = drawnType;

    // 5. Update the UI to show they took their turn and what they got
    string turnMessage = "";
    if (drawnType == PotionType.Curse)
    {
        turnMessage = "<color=purple>CRITICAL!</color> You drew a Curse! Quickly cast a Counterspell or explode!";
    }
    else
    {
        turnMessage = $"You drew a <color=yellow>{drawnType}</color>! Your turn ends.";
    }
    
    uiText.text = turnMessage;
}
}