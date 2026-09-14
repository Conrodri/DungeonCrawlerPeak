using UnityEngine;

// The common drop pool - monsters, broken decor and ground loot all draw from the same table.
// Special items (too-heavy / cursed / trapped) are deliberately not in here - they're placed
// directly as rare ground decor instead, see DungeonBootstrap.SpawnRoomDecor.
public static class LootTable
{
    const float DropChance = 0.35f;

    static readonly (string itemId, int amount, int weight)[] Pool =
    {
        (ItemIds.Gold, 3, 3),
        (ItemIds.Shuriken, 2, 2),
        (ItemIds.Caillou, 2, 2),
        (ItemIds.Baton, 2, 2),
        (ItemIds.Bomb, 1, 1),
        (ItemIds.HealthPotion, 1, 2),
    };

    // `parent` should always be passed by a caller whose own GameObject lives under DungeonRoot
    // (every current caller does) - without it, the dropped pickup has no parent at all and
    // survives DungeonGenerator.Build()'s "destroy the old DungeonRoot" step, piling up across
    // floors forever instead of getting cleaned up with everything else from that floor.
    public static void TryDropLoot(Vector2 position, Transform parent = null)
    {
        if (Random.value > DropChance) return;

        PickRandomItem(out string itemId, out int amount);
        GameObject pickup = ItemPickup.SpawnAt(position, itemId, amount);
        if (parent != null) pickup.transform.SetParent(parent);
    }

    public static void PickRandomItem(out string itemId, out int amount)
    {
        int totalWeight = 0;
        foreach (var entry in Pool) totalWeight += entry.weight;

        int roll = Random.Range(0, totalWeight);
        foreach (var entry in Pool)
        {
            if (roll < entry.weight)
            {
                itemId = entry.itemId;
                amount = entry.amount;
                return;
            }
            roll -= entry.weight;
        }

        itemId = Pool[0].itemId;
        amount = Pool[0].amount;
    }
}
