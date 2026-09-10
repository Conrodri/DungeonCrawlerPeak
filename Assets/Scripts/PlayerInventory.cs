using System;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public const int MaxGold = 100;
    public const int MaxThrowable = 10;

    public int gold;
    public int shuriken;
    public int caillou;
    public int baton;

    public event Action OnInventoryChanged;

    // Always consumes the pickup, even if the corresponding stack is already full.
    public void Add(ItemType type, int amount)
    {
        switch (type)
        {
            case ItemType.Gold: gold = Mathf.Min(MaxGold, gold + amount); break;
            case ItemType.Shuriken: shuriken = Mathf.Min(MaxThrowable, shuriken + amount); break;
            case ItemType.Caillou: caillou = Mathf.Min(MaxThrowable, caillou + amount); break;
            case ItemType.Baton: baton = Mathf.Min(MaxThrowable, baton + amount); break;
        }
        Debug.Log("Picked up " + type + " - gold:" + gold + " shuriken:" + shuriken + " caillou:" + caillou + " baton:" + baton);
        OnInventoryChanged?.Invoke();
    }

    public int GetCount(ItemType type)
    {
        switch (type)
        {
            case ItemType.Gold: return gold;
            case ItemType.Shuriken: return shuriken;
            case ItemType.Caillou: return caillou;
            case ItemType.Baton: return baton;
            default: return 0;
        }
    }

    public bool TryConsume(ItemType type)
    {
        switch (type)
        {
            case ItemType.Shuriken: if (shuriken <= 0) return false; shuriken--; break;
            case ItemType.Caillou: if (caillou <= 0) return false; caillou--; break;
            case ItemType.Baton: if (baton <= 0) return false; baton--; break;
            default: return false;
        }
        OnInventoryChanged?.Invoke();
        return true;
    }
}
