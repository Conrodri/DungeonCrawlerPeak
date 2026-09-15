using System.Collections.Generic;
using UnityEngine;

// What ends up inside a corpse when examined (see Corpse.cs) - replaces the old invisible
// LootTable.TryDropLoot-on-death. Explicit request after the user killed the Region boss and got
// nothing: a single silent 35% roll on top of the boss's own low family-trophy dropChance could
// easily whiff both, with no way to tell loot was even attempted. At least 75% of ordinary mobs
// now have SOMETHING (`guaranteed: false`); a boss (`guaranteed: true`) is never empty-handed.
public static class CorpseLoot
{
    const float HasLootChance = 0.75f;
    // On top of the one guaranteed-once-we-roll-loot item above - a corpse is occasionally a
    // genuinely good find, not just a coin or two every time.
    const float BonusEquipmentChance = 0.15f;

    static readonly string[] EquipmentPool =
    {
        ItemIds.IronHelmet, ItemIds.LeatherPauldrons, ItemIds.CombatGloves, ItemIds.WalkingBoots,
        ItemIds.SimpleNecklace, ItemIds.LeatherBelt, ItemIds.LeatherKneepads, ItemIds.SimpleRing,
    };

    static readonly string[] MaterialPool = { ItemIds.Wood, ItemIds.Metal, ItemIds.Stone };

    // isNpc swaps the "material" branch to Cloth instead of Wood/Metal/Stone (2026-09-14 spec:
    // "soit du tissu... pour les pnj"). guaranteed skips the 75% has-loot roll entirely - used for
    // bosses, where "you get nothing" should never happen.
    public static List<(string itemId, int amount)> Generate(bool isNpc, bool guaranteed = false)
    {
        var items = new List<(string, int)>();
        if (!guaranteed && Random.value > HasLootChance) return items;

        float roll = Random.value;
        if (roll < 0.5f)
        {
            items.Add((ItemIds.Gold, Random.Range(2, 6)));
        }
        else if (roll < 0.8f)
        {
            LootTable.PickRandomItem(out string itemId, out int amount);
            items.Add((itemId, amount));
        }
        else
        {
            string materialId = isNpc ? ItemIds.Cloth : MaterialPool[Random.Range(0, MaterialPool.Length)];
            items.Add((materialId, Random.Range(1, 3)));
        }

        if (Random.value < BonusEquipmentChance) items.Add((LootTable.PickWeighted(EquipmentPool), 1));

        return items;
    }
}
