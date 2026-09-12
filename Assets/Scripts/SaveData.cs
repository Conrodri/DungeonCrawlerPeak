using System;

// Flat, JsonUtility-friendly snapshot of everything a resumed run needs: the seed (so
// DungeonGenerator.Build(seed) recreates the exact same floor) plus the player's full state.
[Serializable]
public class SaveData
{
    public int seed;

    public InventorySlot[] slots;
    public string[] hotbarSlots;
    public string cursedItemId;

    public int level;
    public int force;
    public int dexterite;
    public int intelligence;
    public int vitesse;
    public int constitution;
    public int portee;
    public int charisme;

    public int maxHealth;
    public int currentHealth;

    public PlayerController.WeaponType currentWeapon;
    public bool weaponLocked;
}
