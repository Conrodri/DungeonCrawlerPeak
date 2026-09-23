using UnityEngine;

public class ItemDefinition
{
    public string Id;
    public string DisplayName;
    public ItemCategory Category;
    public int MaxStack;
    public Sprite Icon;
    public string Description;
    // 1 (Commun) to 6 (Mythique) - every item has one, see ItemRarity for the name/color table.
    // Drives drop-weight in LootTable/CorpseLoot/Chest (rarer = less likely), purely informational
    // everywhere else (inspect panel, tooltip).
    public int Rarity = 1;
    // 0 = no restriction; otherwise the player's PlayerStats.force must be at least this to pick it up.
    public int Weight;
    public bool IsCursed;
    // No Nullable<WeaponType> here on purpose - see ItemCatalog.Entry for why.
    public bool HasCursedWeapon;
    public PlayerController.WeaponType CursedWeaponType;
    // A real, deliberately-equipped weapon (Sword/Staff, see EquipmentSlotType.Weapon) - distinct
    // from HasCursedWeapon/CursedWeaponType above, which forces itself on outright and bypasses the
    // equipment slot entirely (see PlayerController.ForceEquipWeapon). IsEquipment/EquipmentSlot
    // must also be set (EquipmentSlot = Weapon) for the normal drag-to-equip flow to accept it.
    public bool IsWeapon;
    public PlayerController.WeaponType Weapon;
    // Whether a plain click on this item in the inventory grid arms/throws it (see
    // InventorySlotUI.OnPointerClick) - used to be inferred from Category == Throwable, but
    // Category is now a pure UI filter bucket (2026-09-21, see ItemCategory), so this is now its
    // own explicit flag, same pattern as IsWeapon/IsEquipment above.
    public bool IsThrowable;
    public bool IsTrap;
    // 0 = not a potion; otherwise using it from the hotbar heals this much instead of throwing it.
    public int HealAmount;
    // Timed buffs (2026-09-21 request: Potion de Vitesse/Adrenaline) - 0 multiplier = no effect.
    // Applied together with HealAmount above if more than one is set (nothing does today, but
    // nothing stops a future potion combining them). See PlayerController.UsePotionItem.
    public float SpeedBuffMultiplier;
    public float SpeedBuffDuration;
    public float StaminaRegenBuffMultiplier;
    public float StaminaRegenBuffDuration;
    // Whether this item is a "potion" for click-to-use/UseItem purposes - any of the effects above.
    public bool IsPotion => HealAmount > 0 || SpeedBuffMultiplier > 0f || StaminaRegenBuffMultiplier > 0f;
    // Empty = not a spell tome; otherwise using it from the hotbar teaches this SpellIds entry
    // (PlayerController.LearnSpell) instead of throwing/consuming for a buff - see TomeFoudre etc.
    public string GrantsSpellId;
    public bool IsSpellTome => !string.IsNullOrEmpty(GrantsSpellId);
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
