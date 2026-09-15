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
    // Flat damage reduction applied by PlayerLimbs when the body part this slot protects gets hit
    // (see PlayerLimbs.GetArmor). 0 = no protection (rings, utility gear like vision glasses).
    public int ArmorValue;
    // StatType.None = no bonus. Only meaningful on a ring (see PlayerEquipment.ApplyItemEffects) -
    // grants a flat +1 to this stat while equipped, -1 back on unequip.
    public StatType RingBonusStat;
    // 0 = infinite/untracked (potions, currency, rings, trophies). Otherwise the current-durability
    // pool lives on PlayerEquipment (armor, see DamageDurability) or PlayerController
    // (currentWeaponDurability, base Sword/Staff only) - never here, since this is the shared
    // definition every equipped instance points at.
    public int MaxDurability;
    // Drives which crafting material repairs this item (see RepairUI.MaterialItemId) and, for
    // Tissu, whether it can catch fire while worn (see PlayerEquipment.IgniteFlammable).
    public MaterialType Material;
}
