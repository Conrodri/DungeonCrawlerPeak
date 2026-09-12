using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    readonly Dictionary<string, int> stacks = new Dictionary<string, int>();

    public event Action OnInventoryChanged;

    // Always consumes the pickup, even if the corresponding stack is already full.
    public void Add(string itemId, int amount)
    {
        ItemDefinition definition = ItemDatabase.Get(itemId);
        int maxStack = definition != null ? definition.MaxStack : int.MaxValue;
        stacks.TryGetValue(itemId, out int current);
        stacks[itemId] = Mathf.Min(maxStack, current + amount);
        Debug.Log("Picked up " + itemId + " - count:" + stacks[itemId]);
        OnInventoryChanged?.Invoke();
    }

    public int GetCount(string itemId)
    {
        stacks.TryGetValue(itemId, out int current);
        return current;
    }

    public bool TryConsume(string itemId)
    {
        if (!stacks.TryGetValue(itemId, out int current) || current <= 0) return false;
        stacks[itemId] = current - 1;
        OnInventoryChanged?.Invoke();
        return true;
    }
}
