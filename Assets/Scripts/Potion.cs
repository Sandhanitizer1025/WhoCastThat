using UnityEngine;

// Defines all the potion types in your game
public enum PotionType 
{ 
    Hex, Tribute, Dispel, Foresight, Warp, Phase, Reflection, Counterspell, Curse 
}

public class Potion : MonoBehaviour 
{
    public PotionType type;
}