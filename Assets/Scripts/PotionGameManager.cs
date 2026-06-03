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
    public Transform[] rackSlots; 
    public TextMeshProUGUI uiText; 

    private List<PotionType> deck = new List<PotionType>();
    private GameObject[] occupiedSlots; 

    // STATE MACHINE TRIGGER: Tracks if the player is currently under a curse threat
    private bool isCurseActive = false; 

    void Start()
    {
        occupiedSlots = new GameObject[rackSlots.Length];
        InitializeDeck();
        DealStartingHand();
    }

    void InitializeDeck()
    {
        // 5x Hex, 4x Tribute, 4x Dispel, 5x Foresight, 4x Warp, 4x Phase, 4x Reflection, 6x Counterspell, 4x Curse
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
        if (slotIndex >= rackSlots.Length) return;

        GameObject prefabToSpawn = GetPrefabForType(type);
        if (prefabToSpawn == null) return;

        GameObject newPotion = Instantiate(prefabToSpawn, rackSlots[slotIndex].position, rackSlots[slotIndex].rotation);
        
        Potion potionScript = newPotion.GetComponent<Potion>();
        if (potionScript == null) potionScript = newPotion.AddComponent<Potion>();
        potionScript.type = type; 

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

    public void DrawPotionFromPot(Vector3 handPosition, Quaternion handRotation)
    {
        // Block drawing a new card if you are currently handling an active curse!
        if (isCurseActive)
        {
            uiText.text = "<color=#b02727>DEFEND YOURSELF!</color> You must play a Counterspell before drawing!";
            return;
        }

        if (deck.Count == 0)
        {
            uiText.text = "The cauldron is empty!";
            return;
        }

        int targetSlotIndex = -1;
        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (occupiedSlots[i] == null)
            {
                targetSlotIndex = i;
                break;
            }
        }

        if (targetSlotIndex == -1)
        {
            uiText.text = "<color=#b02727>Hand Full!</color> Cast something first.";
            return;
        }

        PotionType drawnType = deck[0];
        deck.RemoveAt(0);

        SpawnPotionInRack(drawnType, targetSlotIndex);

        if (drawnType == PotionType.Curse)
        {
            uiText.text = "<color=#800080> CURSE DRAWN! </color>\nIt is on your rack! Place it in the playzone to confront it!";
        }
        else
        {
            uiText.text = $"Drew a <color=#d1ce21>{drawnType}</color>.";
        }
    }

    public void PlayPotion(Potion potion)
    {
        // Remove the potion from hand tracking immediately upon drop
        ClearSlotTracking(potion.gameObject);

        // ==========================================
        // SCENARIO A: PLAYER IS CURRENTLY CURSED
        // ==========================================
        if (isCurseActive)
        {
            if (potion.type == PotionType.Counterspell)
            {
                // SUCCESS: Player saved themselves!
                isCurseActive = false;
                uiText.text = "<color=#63e0d8> COUNTERSPELL CAST! </color>\n\nCurse neutralized! It has been safely shuffled back into the cauldron.";
                
                // Put the curse back in the deck and shuffle (per Exploding Kittens rules)
                deck.Add(PotionType.Curse);
                ShuffleDeck();
            }
            else
            {
                // FAILURE: They threw the wrong potion in a panic!
                uiText.text = $"<color=#b02727> BOOM! YOU EXPLODED! </color>\n\nYou tried to use {potion.type} instead of a Counterspell!";
            }

            Destroy(potion.gameObject, 0.2f);
            return; 
        }

        // ==========================================
        // SCENARIO B: NORMAL GAMEPLAY STATE
        // ==========================================
        string message = "";

        switch (potion.type)
        {
            case PotionType.Curse:
                // Activating the threat sequence!
                isCurseActive = true;
                message = "<color=#800080> CURSE ACTIVATED! </color>\n\nQuick! Place a <color=#63e0d8>Counterspell</color> potion in the center to survive!";
                break;

            case PotionType.Hex: message = "<color=#663300>Hex</color>: Must play 2 turns!"; break;
            case PotionType.Tribute: message = "<color=#d1ce21>Tribute</color>: Target must give you a card."; break;
            case PotionType.Dispel: message = "<color=#b02727>Dispel</color>: Stopped the last action!"; break;
            case PotionType.Foresight: message = "<color=#ff36dd>Foresight</color>: Viewing top 3 potions secretly..."; break;
            case PotionType.Warp: message = "<color=#3d3d3d>Warp</color>: Shuffled the cauldron!"; ShuffleDeck(); break;
            case PotionType.Phase: message = "<color=#3b6b41>Phase</color>: Turn ended safely."; break;
            case PotionType.Reflection: message = "<color=#4d81bd>Reflection</color>: Copied the last cast spell!"; break;
            case PotionType.Counterspell: message = "<color=#63e0d8>Counterspell</color>: Saved from a curse!"; break;
        }

        uiText.text = message;
        Destroy(potion.gameObject, 0.2f); 
    }

    void ClearSlotTracking(GameObject potionObj)
    {
        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (occupiedSlots[i] == potionObj)
            {
                occupiedSlots[i] = null;
                break;
            }
        }
    }
}