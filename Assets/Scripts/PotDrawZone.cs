using UnityEngine;


public class PotDrawZone : MonoBehaviour
{
    public PotionGameManager gameManager;
    public float drawCooldown = 1.5f; 
    private float nextDrawTime = 0f;

    private void OnTriggerEnter(Collider other)
    {
        // 1. FIRST TEST: Is the physics engine working at all?
        Debug.Log($"[POT PHYSICS] Something entered the pot! Object Name: {other.name}, Tag: {other.tag}");

        // Cooldown check
        if (Time.time < nextDrawTime) 
        {
            Debug.Log("[POT PHYSICS] Draw ignored due to cooldown timer.");
            return;
        }

        // 2. SECOND TEST: Let's see what components are actually on this object
        var directInteractor = other.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>() ?? other.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
        var anyInteractor = other.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>() ?? other.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>();
        
        Debug.Log($"[POT COMPONENTS] Direct Interactor Found: {directInteractor != null}, Any Interactor Found: {anyInteractor != null}");

        // 3. BROAD TRIGGER: If it looks like a hand/controller OR has an interactor, let it pass
        bool isHand = other.name.ToLower().Contains("hand") || 
                      other.name.ToLower().Contains("controller") || 
                      other.tag == "GameController";

        if (anyInteractor != null || isHand)
        {
            Debug.Log("[POT SUCCESS] Hand verified! Calling GameManager to spawn potion...");
            nextDrawTime = Time.time + drawCooldown;
            gameManager.DrawPotionFromPot(other.transform.position, other.transform.rotation);
        }
        else
        {
            Debug.Log("[POT FAILURE] Object entered the pot but it wasn't recognized as a VR Hand.");
        }
    }
}