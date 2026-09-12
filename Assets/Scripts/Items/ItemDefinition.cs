using UnityEngine;

public class ItemDefinition
{
    public string Id;
    public string DisplayName;
    public ItemCategory Category;
    public int MaxStack;
    public Sprite Icon;
    public string Description;
    // 0 = no restriction; otherwise the player's PlayerStats.force must be at least this to pick it up.
    public int Weight;
    public bool IsCursed;
    // No Nullable<WeaponType> here on purpose - see ItemCatalog.Entry for why.
    public bool HasCursedWeapon;
    public PlayerController.WeaponType CursedWeaponType;
    public bool IsTrap;
}
