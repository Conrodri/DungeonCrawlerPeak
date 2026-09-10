using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    public ItemType itemType;
    public int amount = 1;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerInventory inventory = other.GetComponent<PlayerInventory>();
        if (inventory == null) return;

        inventory.Add(itemType, amount);
        Destroy(gameObject);
    }
}
