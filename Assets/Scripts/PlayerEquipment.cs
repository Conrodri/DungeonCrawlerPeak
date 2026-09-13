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
    // Set at generation time alongside the other Player components - needed to apply/revoke a
    // ring's stat bonus (see Set/ApplyItemEffects). Other equipment effects (anti-hole boots,
    // vision glasses) are just plain Get() checks at their point of use instead, since they're not
    // continuous stat deltas that need to be added/removed symmetrically.
    public PlayerStats stats;

    public event Action OnEquipmentChanged;

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

        ApplyItemEffects(itemId);
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
