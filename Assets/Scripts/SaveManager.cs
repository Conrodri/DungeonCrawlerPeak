using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Writes/reads a single save file under Application.persistentDataPath (never inside Assets/ -
// this is real player-facing save data, not project content). Only ever triggered by talking to
// an NPC in a Safe room (see DungeonGenerator.SpawnTavernNpc) - there is deliberately no
// auto-save and, for now, nothing calls Load()/Apply() automatically on launch: that hook belongs
// to the main menu ("Continuer"), a separate feature not built yet. Load()/Apply() are fully
// functional and tested directly in the meantime.
public static class SaveManager
{
    static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    public static bool HasSave() => File.Exists(SavePath);

    // Shared field-mapping between a to-disk Save and an in-memory carry-over across floors
    // (DungeonGenerator.Descend) - avoids duplicating this list twice.
    public static SaveData Capture(int seed, int floor, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller,
        PlayerEquipment equipment, IEnumerable<Vector2Int> clearedRooms = null, bool bossDefeated = false, PlayerLimbs limbs = null,
        IEnumerable<Biome> usedBossBiomesBeforeFloor = null)
    {
        return new SaveData
        {
            seed = seed,
            floor = floor,
            clearedRooms = clearedRooms != null ? new List<Vector2Int>(clearedRooms) : new List<Vector2Int>(),
            usedBossBiomesBeforeFloor = usedBossBiomesBeforeFloor != null ? new List<Biome>(usedBossBiomesBeforeFloor) : new List<Biome>(),
            bossDefeated = bossDefeated,
            playerPosition = controller.transform.position,
            slots = inventory.GetAllSlots(),
            hotbarSlots = inventory.hotbarSlots,
            cursedItemId = inventory.CursedItemId,
            level = stats.level,
            experience = stats.experience,
            unspentAttributePoints = stats.unspentAttributePoints,
            force = stats.force,
            dexterite = stats.dexterite,
            intelligence = stats.intelligence,
            vitesse = stats.vitesse,
            constitution = stats.constitution,
            portee = stats.portee,
            charisme = stats.charisme,
            endurance = stats.endurance,
            maxHealth = health.maxHealth,
            currentHealth = health.currentHealth,
            maxStamina = stamina.maxStamina,
            currentStamina = stamina.currentStamina,
            currentWeapon = controller.currentWeapon,
            weaponLocked = controller.WeaponLocked,
            weaponHand = controller.weaponHand,
            limbHealthHead = limbs != null ? limbs.GetLimbHealth(BodyPart.Head) : PlayerLimbs.BaseMaxFor(BodyPart.Head),
            limbHealthTorso = limbs != null ? limbs.GetLimbHealth(BodyPart.Torso) : PlayerLimbs.BaseMaxFor(BodyPart.Torso),
            limbHealthArmLeft = limbs != null ? limbs.GetLimbHealth(BodyPart.ArmLeft) : PlayerLimbs.BaseMaxFor(BodyPart.ArmLeft),
            limbHealthArmRight = limbs != null ? limbs.GetLimbHealth(BodyPart.ArmRight) : PlayerLimbs.BaseMaxFor(BodyPart.ArmRight),
            limbHealthLegLeft = limbs != null ? limbs.GetLimbHealth(BodyPart.LegLeft) : PlayerLimbs.BaseMaxFor(BodyPart.LegLeft),
            limbHealthLegRight = limbs != null ? limbs.GetLimbHealth(BodyPart.LegRight) : PlayerLimbs.BaseMaxFor(BodyPart.LegRight),
            equippedHead = equipment != null ? equipment.Get(EquipmentSlotType.Head) : null,
            equippedShoulders = equipment != null ? equipment.Get(EquipmentSlotType.Shoulders) : null,
            equippedGloves = equipment != null ? equipment.Get(EquipmentSlotType.Gloves) : null,
            equippedBoots = equipment != null ? equipment.Get(EquipmentSlotType.Boots) : null,
            equippedNeck = equipment != null ? equipment.Get(EquipmentSlotType.Neck) : null,
            equippedBelt = equipment != null ? equipment.Get(EquipmentSlotType.Belt) : null,
            equippedKnees = equipment != null ? equipment.Get(EquipmentSlotType.Knees) : null,
            equippedWeapon = equipment != null ? equipment.Get(EquipmentSlotType.Weapon) : null,
            equippedRingsLeft = equipment != null ? (string[])equipment.ringsLeft.Clone() : null,
            equippedRingsRight = equipment != null ? (string[])equipment.ringsRight.Clone() : null,
            durabilityHead = equipment != null ? equipment.GetDurability(EquipmentSlotType.Head) : 0,
            durabilityShoulders = equipment != null ? equipment.GetDurability(EquipmentSlotType.Shoulders) : 0,
            durabilityGloves = equipment != null ? equipment.GetDurability(EquipmentSlotType.Gloves) : 0,
            durabilityBoots = equipment != null ? equipment.GetDurability(EquipmentSlotType.Boots) : 0,
            durabilityNeck = equipment != null ? equipment.GetDurability(EquipmentSlotType.Neck) : 0,
            durabilityBelt = equipment != null ? equipment.GetDurability(EquipmentSlotType.Belt) : 0,
            durabilityKnees = equipment != null ? equipment.GetDurability(EquipmentSlotType.Knees) : 0,
            durabilityWeapon = equipment != null ? equipment.GetDurability(EquipmentSlotType.Weapon) : 0,
            durabilityRingsLeft = equipment != null ? (int[])equipment.ringsLeftDurability.Clone() : null,
            durabilityRingsRight = equipment != null ? (int[])equipment.ringsRightDurability.Clone() : null,
            currentWeaponDurability = controller.CurrentWeaponDurability,
        };
    }

    public static void Save(int seed, int floor, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller,
        PlayerEquipment equipment, IEnumerable<Vector2Int> clearedRooms = null, bool bossDefeated = false, PlayerLimbs limbs = null,
        IEnumerable<Biome> usedBossBiomesBeforeFloor = null)
    {
        SaveData data = Capture(seed, floor, inventory, stats, health, stamina, controller, equipment, clearedRooms, bossDefeated, limbs, usedBossBiomesBeforeFloor);
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
        Debug.Log("SaveManager: game saved to " + SavePath);
    }

    public static SaveData Load()
    {
        if (!HasSave()) return null;
        return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    public static void Apply(SaveData data, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller, PlayerEquipment equipment, PlayerLimbs limbs = null)
    {
        // Must run after DungeonGenerator.Build (the caller's job - Build is what creates this
        // very player/transform) but overrides Build's own fixed Start-room spawn point, which is
        // otherwise the only place player.transform.position ever gets set.
        controller.transform.position = data.playerPosition;

        inventory.LoadState(data.slots, data.hotbarSlots, data.cursedItemId);

        stats.level = data.level;
        stats.experience = data.experience;
        stats.unspentAttributePoints = data.unspentAttributePoints;
        stats.force = data.force;
        stats.dexterite = data.dexterite;
        stats.intelligence = data.intelligence;
        stats.vitesse = data.vitesse;
        stats.constitution = data.constitution;
        stats.portee = data.portee;
        stats.charisme = data.charisme;
        stats.endurance = data.endurance;

        // Fallback for a limbs == null caller only - if limbs is present (the normal case, always
        // present on the player), the SetConstitutionBonus/SetLimbHealth block below immediately
        // overwrites this with the true per-limb-derived totals (see PlayerLimbs.SyncHealth).
        health.maxHealth = data.maxHealth;
        health.currentHealth = data.currentHealth;
        stamina.maxStamina = data.maxStamina;
        stamina.currentStamina = data.currentStamina;

        if (data.weaponLocked)
        {
            controller.ForceEquipWeapon(data.currentWeapon);
            // Must run AFTER ForceEquipWeapon above - it resets currentWeaponDurability to full,
            // this overwrites it with the actually-saved (possibly worn-down) value.
            controller.SetCurrentWeaponDurability(data.currentWeaponDurability);
        }
        else
        {
            // Plain restore, not EquipWeaponItem - durability for this item lives on the equipment
            // side (SetDurabilityRaw just below, alongside the 7 armor slots), not reset to full
            // like a fresh equip would.
            controller.SetCurrentWeaponItem(data.equippedWeapon, data.currentWeapon);
        }
        controller.weaponHand = data.weaponHand;

        if (limbs != null)
        {
            // Must run BEFORE restoring individual limb HPs below - it resizes every limb's max
            // (base + this Constitution's bonus), which SetLimbHealth then clamps each restored
            // value against.
            limbs.SetConstitutionBonus(stats.constitution);
            limbs.SetLimbHealth(BodyPart.Head, data.limbHealthHead);
            limbs.SetLimbHealth(BodyPart.Torso, data.limbHealthTorso);
            limbs.SetLimbHealth(BodyPart.ArmLeft, data.limbHealthArmLeft);
            limbs.SetLimbHealth(BodyPart.ArmRight, data.limbHealthArmRight);
            limbs.SetLimbHealth(BodyPart.LegLeft, data.limbHealthLegLeft);
            limbs.SetLimbHealth(BodyPart.LegRight, data.limbHealthLegRight);
        }

        // Plain field restore, NOT PlayerEquipment.Set() - the saved stat values above (data.force
        // etc.) already include any equipped ring's bonus at the time it was captured (PlayerStats
        // has no separate "base" vs "bonus" - ApplyBonus mutates the stat directly), so replaying
        // Set()'s ApplyItemEffects here would add that bonus a SECOND time on every single
        // save/continue cycle while a stat ring stays equipped. Restoring is just "here's what was
        // worn", not "these were just put on".
        if (equipment != null)
        {
            equipment.SetRaw(EquipmentSlotType.Head, 0, data.equippedHead);
            equipment.SetRaw(EquipmentSlotType.Shoulders, 0, data.equippedShoulders);
            equipment.SetRaw(EquipmentSlotType.Gloves, 0, data.equippedGloves);
            equipment.SetRaw(EquipmentSlotType.Boots, 0, data.equippedBoots);
            equipment.SetRaw(EquipmentSlotType.Neck, 0, data.equippedNeck);
            equipment.SetRaw(EquipmentSlotType.Belt, 0, data.equippedBelt);
            equipment.SetRaw(EquipmentSlotType.Knees, 0, data.equippedKnees);
            equipment.SetRaw(EquipmentSlotType.Weapon, 0, data.equippedWeapon);
            equipment.ringsLeft = data.equippedRingsLeft != null
                ? (string[])data.equippedRingsLeft.Clone() : new string[PlayerEquipment.RingSlotsPerHand];
            equipment.ringsRight = data.equippedRingsRight != null
                ? (string[])data.equippedRingsRight.Clone() : new string[PlayerEquipment.RingSlotsPerHand];

            // Plain field restore too, same reasoning as the itemId fields just above - Set() would
            // reset every slot back to full durability instead of the saved (possibly worn-down) value.
            equipment.SetDurabilityRaw(EquipmentSlotType.Head, 0, data.durabilityHead);
            equipment.SetDurabilityRaw(EquipmentSlotType.Shoulders, 0, data.durabilityShoulders);
            equipment.SetDurabilityRaw(EquipmentSlotType.Gloves, 0, data.durabilityGloves);
            equipment.SetDurabilityRaw(EquipmentSlotType.Boots, 0, data.durabilityBoots);
            equipment.SetDurabilityRaw(EquipmentSlotType.Neck, 0, data.durabilityNeck);
            equipment.SetDurabilityRaw(EquipmentSlotType.Belt, 0, data.durabilityBelt);
            equipment.SetDurabilityRaw(EquipmentSlotType.Knees, 0, data.durabilityKnees);
            equipment.SetDurabilityRaw(EquipmentSlotType.Weapon, 0, data.durabilityWeapon);
            equipment.ringsLeftDurability = data.durabilityRingsLeft != null
                ? (int[])data.durabilityRingsLeft.Clone() : new int[PlayerEquipment.RingSlotsPerHand];
            equipment.ringsRightDurability = data.durabilityRingsRight != null
                ? (int[])data.durabilityRingsRight.Clone() : new int[PlayerEquipment.RingSlotsPerHand];
        }
    }
}
