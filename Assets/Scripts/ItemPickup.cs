using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    public string itemId;
    public int amount = 1;

    const int TrapDamage = 2;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        ItemDefinition definition = ItemDatabase.Get(itemId);
        // A weapon is always worth a look before it's in your inventory - explicit request, "si
        // nous trouvons une epee... pourra etre inspectee avant d'etre recuperee".
        bool needsInspection = definition != null && (definition.Weight > 0 || definition.IsCursed || definition.IsTrap || definition.IsWeapon);
        if (needsInspection)
        {
            if (ItemInspectManager.Instance != null) ItemInspectManager.Instance.SetNearbyItem(this);
            return;
        }

        TryPickup(other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (ItemInspectManager.Instance != null) ItemInspectManager.Instance.ClearNearbyItem(this);
    }

    // Called directly for a plain item touched on contact, or by ItemInspectManager once the
    // player has inspected a weight/curse/trap item and chosen to pick it up anyway.
    public void TryPickup(Collider2D player)
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null) return;

        ItemDefinition definition = ItemDatabase.Get(itemId);

        if (definition != null && definition.IsTrap)
        {
            Health health = player.GetComponent<Health>();
            if (health != null) health.TakeDamage(TrapDamage);
            Destroy(gameObject);
            return;
        }

        if (definition != null && definition.Weight > 0)
        {
            PlayerStats stats = player.GetComponent<PlayerStats>();
            if (stats == null || stats.force < definition.Weight) return; // too heavy - stays on the ground
        }

        inventory.Add(itemId, amount);

        if (definition != null && definition.IsCursed)
        {
            inventory.ApplyCurse(itemId);
            if (definition.HasCursedWeapon)
            {
                PlayerController controller = player.GetComponent<PlayerController>();
                if (controller != null) controller.ForceEquipWeapon(definition.CursedWeaponType);
            }
        }

        Destroy(gameObject);
    }

    public string ItemId => itemId;

    // Runtime-safe factory (unlike DungeonBootstrap's editor-only sprite loading) - used by loot
    // drops from a dying enemy or broken decor, which happen during actual play.
    public static GameObject SpawnAt(Vector2 position, string itemId, int amount)
    {
        GameObject go = new GameObject(itemId + "Pickup", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(ItemPickup));
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.5f;

        ItemDefinition definition = ItemDatabase.Get(itemId);
        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = definition != null ? definition.Icon : null;
        renderer.sortingOrder = 0;

        CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.4f;

        ItemPickup pickup = go.GetComponent<ItemPickup>();
        pickup.itemId = itemId;
        pickup.amount = amount;
        return go;
    }
}
