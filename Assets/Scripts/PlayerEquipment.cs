using System;
using UnityEngine;

// One equipped item id per slot (see EquipmentSlotType), plus 5 ring slots per hand as the user
// specifically requested (most games give 1-2 - this project gives 5 each side). Equipping/
// unequipping always goes through PlayerInventory too (see InventorySlotUI.OnDrop) - this class
// only ever holds what's currently worn, never a copy that could drift out of sync with the
// inventory's own count.
public class PlayerEquipment : MonoBehaviour
{
    public string head;
    public string shoulders;
    public string gloves;
    public string boots;
    public string neck;
    public string belt;
    public string knees;
    public const int RingSlotsPerHand = 5;
    public string[] ringsLeft = new string[RingSlotsPerHand];
    public string[] ringsRight = new string[RingSlotsPerHand];

    // Current durability, parallel to the itemId fields above - flat named fields rather than a
    // Dictionary, same JsonUtility-friendly convention SaveData already uses (see its own comment).
    // Only meaningful while the matching slot's ItemDefinition.MaxDurability > 0 - always kept at
    // that item's max when freshly equipped (see Set), 0 has no other meaning here since an item
    // that hits 0 unequips itself instead of lingering (see DamageDurability).
    public int headDurability;
    public int shouldersDurability;
    public int glovesDurability;
    public int bootsDurability;
    public int neckDurability;
    public int beltDurability;
    public int kneesDurability;
    public int[] ringsLeftDurability = new int[RingSlotsPerHand];
    public int[] ringsRightDurability = new int[RingSlotsPerHand];
    // Set at generation time alongside the other Player components - needed to apply/revoke a
    // ring's stat bonus (see Set/ApplyItemEffects). Other equipment effects (anti-hole boots,
    // vision glasses) are just plain Get() checks at their point of use instead, since they're not
    // continuous stat deltas that need to be added/removed symmetrically.
    public PlayerStats stats;

    public event Action OnEquipmentChanged;
    // Fired when a slot's durability hits 0 and the item is destroyed/unequipped (see
    // DamageDurability) - carries the broken item's display name for a log/toast.
    public event Action<string> OnItemBroken;

    // A ring slot type accepts an item equipped as EITHER RingLeft or RingRight (a ring doesn't
    // care which hand), so this checks slot COMPATIBILITY rather than exact enum equality.
    public static bool IsCompatible(EquipmentSlotType itemSlot, EquipmentSlotType targetSlot)
    {
        bool itemIsRing = itemSlot == EquipmentSlotType.RingLeft || itemSlot == EquipmentSlotType.RingRight;
        bool targetIsRing = targetSlot == EquipmentSlotType.RingLeft || targetSlot == EquipmentSlotType.RingRight;
        return (itemIsRing && targetIsRing) || itemSlot == targetSlot;
    }

    public string Get(EquipmentSlotType slot, int ringIndex = 0) => slot switch
    {
        EquipmentSlotType.Head => head,
        EquipmentSlotType.Shoulders => shoulders,
        EquipmentSlotType.Gloves => gloves,
        EquipmentSlotType.Boots => boots,
        EquipmentSlotType.Neck => neck,
        EquipmentSlotType.Belt => belt,
        EquipmentSlotType.Knees => knees,
        EquipmentSlotType.RingLeft => ringsLeft[ringIndex],
        EquipmentSlotType.RingRight => ringsRight[ringIndex],
        _ => null,
    };

    public void Set(EquipmentSlotType slot, int ringIndex, string itemId)
    {
        string old = Get(slot, ringIndex);
        if (old == itemId) return;
        RemoveItemEffects(old);

        switch (slot)
        {
            case EquipmentSlotType.Head: head = itemId; break;
            case EquipmentSlotType.Shoulders: shoulders = itemId; break;
            case EquipmentSlotType.Gloves: gloves = itemId; break;
            case EquipmentSlotType.Boots: boots = itemId; break;
            case EquipmentSlotType.Neck: neck = itemId; break;
            case EquipmentSlotType.Belt: belt = itemId; break;
            case EquipmentSlotType.Knees: knees = itemId; break;
            case EquipmentSlotType.RingLeft: ringsLeft[ringIndex] = itemId; break;
            case EquipmentSlotType.RingRight: ringsRight[ringIndex] = itemId; break;
        }

        // A freshly worn item always starts at full durability - SaveManager.Apply restores a
        // worn-down value afterward by writing the durability fields directly, never through Set().
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        SetDurabilityRaw(slot, ringIndex, definition != null ? definition.MaxDurability : 0);

        ApplyItemEffects(itemId);
        OnEquipmentChanged?.Invoke();
    }

    public int GetDurability(EquipmentSlotType slot, int ringIndex = 0) => slot switch
    {
        EquipmentSlotType.Head => headDurability,
        EquipmentSlotType.Shoulders => shouldersDurability,
        EquipmentSlotType.Gloves => glovesDurability,
        EquipmentSlotType.Boots => bootsDurability,
        EquipmentSlotType.Neck => neckDurability,
        EquipmentSlotType.Belt => beltDurability,
        EquipmentSlotType.Knees => kneesDurability,
        EquipmentSlotType.RingLeft => ringsLeftDurability[ringIndex],
        EquipmentSlotType.RingRight => ringsRightDurability[ringIndex],
        _ => 0,
    };

    public void SetDurabilityRaw(EquipmentSlotType slot, int ringIndex, int value)
    {
        switch (slot)
        {
            case EquipmentSlotType.Head: headDurability = value; break;
            case EquipmentSlotType.Shoulders: shouldersDurability = value; break;
            case EquipmentSlotType.Gloves: glovesDurability = value; break;
            case EquipmentSlotType.Boots: bootsDurability = value; break;
            case EquipmentSlotType.Neck: neckDurability = value; break;
            case EquipmentSlotType.Belt: beltDurability = value; break;
            case EquipmentSlotType.Knees: kneesDurability = value; break;
            case EquipmentSlotType.RingLeft: ringsLeftDurability[ringIndex] = value; break;
            case EquipmentSlotType.RingRight: ringsRightDurability[ringIndex] = value; break;
        }
    }

    // Wears down whatever's equipped in this slot by `amount` - a no-op on a slot that's empty or
    // whose item has no durability (rings, trophies). Breaks and unequips outright at 0 (see
    // PlayerLimbs.MitigateHit for armor, PlayerController.DamageWeaponDurability for weapons -
    // though the latter doesn't go through here, base Sword/Staff aren't real ItemDefinitions).
    public void DamageDurability(EquipmentSlotType slot, int ringIndex, int amount)
    {
        string itemId = Get(slot, ringIndex);
        if (string.IsNullOrEmpty(itemId)) return;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition == null || definition.MaxDurability <= 0) return;

        int current = Mathf.Max(0, GetDurability(slot, ringIndex) - amount);
        SetDurabilityRaw(slot, ringIndex, current);
        OnEquipmentChanged?.Invoke();

        if (current <= 0)
        {
            string brokenName = definition.DisplayName;
            Set(slot, ringIndex, null);
            OnItemBroken?.Invoke(brokenName);
        }
    }

    // Used by RepairUI - restores up to `amount`, clamped to the equipped item's own max (no-op if
    // empty or the item has no durability to restore).
    public void Repair(EquipmentSlotType slot, int ringIndex, int amount)
    {
        string itemId = Get(slot, ringIndex);
        if (string.IsNullOrEmpty(itemId)) return;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition == null || definition.MaxDurability <= 0) return;

        SetDurabilityRaw(slot, ringIndex, Mathf.Min(definition.MaxDurability, GetDurability(slot, ringIndex) + amount));
        OnEquipmentChanged?.Invoke();
    }

    void ApplyItemEffects(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || stats == null) return;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition != null && definition.RingBonusStat != StatType.None) stats.ApplyBonus(definition.RingBonusStat, 1);
    }

    void RemoveItemEffects(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || stats == null) return;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition != null && definition.RingBonusStat != StatType.None) stats.ApplyPenalty(definition.RingBonusStat, 1);
    }
}
