using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public int slotCount = 20;
    public string[] hotbarSlots = new string[HotbarUI.SlotCount];

    readonly List<InventorySlot> slots = new List<InventorySlot>();
    string cursedItemId;

    public event Action OnInventoryChanged;

    public int SlotCount => slots.Count;

    void Awake()
    {
        for (int i = 0; i < slotCount; i++) slots.Add(new InventorySlot());
    }

    public InventorySlot GetSlot(int index) => slots[index];

    // Always consumes the pickup, even if every matching stack is already full - any amount that
    // still doesn't fit once every slot is full or occupied by another item is simply lost.
    public void Add(string itemId, int amount)
    {
        int maxStack = MaxStackFor(itemId);

        foreach (InventorySlot slot in slots)
        {
            if (amount <= 0) break;
            if (slot.itemId != itemId || slot.count >= maxStack) continue;
            int add = Mathf.Min(maxStack - slot.count, amount);
            slot.count += add;
            amount -= add;
        }

        foreach (InventorySlot slot in slots)
        {
            if (amount <= 0) break;
            if (!slot.IsEmpty) continue;
            int add = Mathf.Min(maxStack, amount);
            slot.itemId = itemId;
            slot.count = add;
            amount -= add;
        }

        Debug.Log("Picked up " + itemId + (amount > 0 ? " - inventory full, lost " + amount : ""));
        OnInventoryChanged?.Invoke();
    }

    public int GetCount(string itemId)
    {
        int total = 0;
        foreach (InventorySlot slot in slots)
        {
            if (slot.itemId == itemId) total += slot.count;
        }
        return total;
    }

    public bool TryConsume(string itemId)
    {
        if (itemId == cursedItemId) return false;

        foreach (InventorySlot slot in slots)
        {
            if (slot.itemId != itemId || slot.count <= 0) continue;
            slot.count--;
            if (slot.count <= 0) slot.itemId = null;
            OnInventoryChanged?.Invoke();
            return true;
        }
        return false;
    }

    // Same item in both slots: merges into one stack (leftover, if any, stays behind). Otherwise
    // swaps the two slots' contents outright.
    public void SwapSlots(int a, int b)
    {
        if (a == b) return;
        InventorySlot slotA = slots[a];
        InventorySlot slotB = slots[b];

        if (!slotA.IsEmpty && !slotB.IsEmpty && slotA.itemId == slotB.itemId)
        {
            int maxStack = MaxStackFor(slotA.itemId);
            int total = slotA.count + slotB.count;
            slotB.count = Mathf.Min(maxStack, total);
            slotA.count = total - slotB.count;
            if (slotA.count <= 0) slotA.itemId = null;
        }
        else
        {
            slots[a] = slotB;
            slots[b] = slotA;
        }
        OnInventoryChanged?.Invoke();
    }

    public void AssignHotbar(int hotbarIndex, string itemId)
    {
        hotbarSlots[hotbarIndex] = itemId;
        OnInventoryChanged?.Invoke();
    }

    // A cursed item can't be thrown/consumed away via TryConsume until this is lifted.
    public void ApplyCurse(string itemId)
    {
        cursedItemId = itemId;
    }

    public void RemoveCurse()
    {
        if (string.IsNullOrEmpty(cursedItemId)) return;
        string id = cursedItemId;
        cursedItemId = null;
        while (TryConsume(id)) { }
    }

    static int MaxStackFor(string itemId)
    {
        ItemDefinition definition = ItemDatabase.Get(itemId);
        return definition != null ? definition.MaxStack : int.MaxValue;
    }
}
