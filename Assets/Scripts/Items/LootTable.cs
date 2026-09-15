using UnityEngine;

// The common drop pool - monsters, broken decor and ground loot all draw from the same table.
// Special items (too-heavy / cursed / trapped) are deliberately not in here - they're placed
// directly as rare ground decor instead, see DungeonBootstrap.SpawnRoomDecor.
public static class LootTable
{
    const float DropChance = 0.35f;

    // Weight now comes from each item's own rarity (see ItemRarity.Weight) instead of a hand-tuned
    // number per entry - a common item drops more, a rare one less, without this table having to
    // duplicate a judgment call ItemDefinition.Rarity already makes.
    static readonly (string itemId, int amount)[] Pool =
    {
        (ItemIds.Gold, 3),
        (ItemIds.Shuriken, 2),
        (ItemIds.Caillou, 2),
        (ItemIds.Baton, 2),
        (ItemIds.Bomb, 1),
        (ItemIds.HealthPotion, 1),
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
        foreach (var entry in Pool) totalWeight += RarityWeight(entry.itemId);

        int roll = Random.Range(0, totalWeight);
        foreach (var entry in Pool)
        {
            int weight = RarityWeight(entry.itemId);
            if (roll < weight)
            {
                itemId = entry.itemId;
                amount = entry.amount;
                return;
            }
            roll -= weight;
        }

        itemId = Pool[0].itemId;
        amount = Pool[0].amount;
    }

    static int RarityWeight(string itemId)
    {
        ItemDefinition definition = ItemDatabase.Get(itemId);
        return ItemRarity.Weight(definition != null ? definition.Rarity : ItemRarity.Min);
    }

    // Shared by CorpseLoot/Chest so every rarity-weighted pick in the game uses the same formula.
    public static string PickWeighted(string[] pool)
    {
        int totalWeight = 0;
        foreach (string id in pool) totalWeight += RarityWeight(id);

        int roll = Random.Range(0, totalWeight);
        foreach (string id in pool)
        {
            int weight = RarityWeight(id);
            if (roll < weight) return id;
            roll -= weight;
        }
        return pool[0];
    }
}
