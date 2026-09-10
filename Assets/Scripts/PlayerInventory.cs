using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public const int MaxGold = 100;
    public const int MaxThrowable = 10;

    public int gold;
    public int shuriken;
    public int caillou;
    public int baton;

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
    }
}
