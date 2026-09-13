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
    // 0 = not a potion; otherwise using it from the hotbar heals this much instead of throwing it.
    public int HealAmount;
    // No Nullable<EquipmentSlotType> here, same reason as CursedWeaponType above - a plain bool
    // flag next to a non-nullable default value survives Unity's serialization; Nullable doesn't.
    public bool IsEquipment;
    public EquipmentSlotType EquipmentSlot;
}
