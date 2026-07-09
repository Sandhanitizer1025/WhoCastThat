using System.Collections.Generic;
using UnityEngine;

namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Keeps a supply of throwable potions on the table for the interaction test
    /// scene. Spawns up to <see cref="maxActivePotions"/> and periodically restocks
    /// as potions are thrown and shatter (destroyed), so there is always something
    /// to grab and throw.
    /// Place this GameObject just above the table surface.
    /// </summary>
    public class PotionSpawner : MonoBehaviour
    {
        [Tooltip("Potion prefab to spawn (must have Rigidbody + XRGrabInteractable + ThrowablePotion).")]
        [SerializeField] private GameObject potionPrefab;

        [Tooltip("How many potions to keep available at once.")]
        [SerializeField] private int maxActivePotions = 3;

        [Tooltip("Horizontal spacing between spawned potions, in metres.")]
        [SerializeField] private float spawnSpacing = 0.3f;

        [Tooltip("How often (seconds) to check whether the supply needs restocking.")]
        [SerializeField] private float restockInterval = 1.5f;

        private readonly List<GameObject> spawnedPotions = new List<GameObject>();
        private float restockTimer;

        private void Start()
        {
            RestockToMax();
        }

        private void Update()
        {
            restockTimer += Time.deltaTime;
            if (restockTimer >= restockInterval)
            {
                restockTimer = 0f;
                RestockToMax();
            }
        }

        private void RestockToMax()
        {
            // Drop references to potions that have been destroyed (shattered).
            spawnedPotions.RemoveAll(potion => potion == null);

            while (spawnedPotions.Count < maxActivePotions)
            {
                SpawnPotion(spawnedPotions.Count);
            }
        }

        private void SpawnPotion(int slotIndex)
        {
            if (potionPrefab == null)
            {
                Debug.LogWarning("[PotionSpawner] No potion prefab assigned.", this);
                return;
            }

            // Lay the potions out in a centred row along this spawner's local X axis.
            float centredOffset = (slotIndex - (maxActivePotions - 1) * 0.5f) * spawnSpacing;
            Vector3 spawnPosition = transform.position + transform.right * centredOffset;

            GameObject potion = Instantiate(potionPrefab, spawnPosition, transform.rotation);
            spawnedPotions.Add(potion);
        }
    }
}
