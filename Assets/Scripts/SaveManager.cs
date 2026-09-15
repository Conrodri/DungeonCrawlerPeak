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
        PlayerEquipment equipment, IEnumerable<Vector2Int> clearedRooms = null, bool bossDefeated = false, PlayerLimbs limbs = null)
    {
        return new SaveData
        {
            seed = seed,
            floor = floor,
            clearedRooms = clearedRooms != null ? new List<Vector2Int>(clearedRooms) : new List<Vector2Int>(),
            bossDefeated = bossDefeated,
            slots = inventory.GetAllSlots(),
            hotbarSlots = inventory.hotbarSlots,
            cursedItemId = inventory.CursedItemId,
            level = stats.level,
            experience = stats.experience,
            experienceToNextLevel = stats.experienceToNextLevel,
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
            equippedHead = equipment != null ? equipment.head : null,
            equippedShoulders = equipment != null ? equipment.shoulders : null,
            equippedGloves = equipment != null ? equipment.gloves : null,
            equippedBoots = equipment != null ? equipment.boots : null,
            equippedNeck = equipment != null ? equipment.neck : null,
            equippedBelt = equipment != null ? equipment.belt : null,
            equippedKnees = equipment != null ? equipment.knees : null,
            equippedRingsLeft = equipment != null ? (string[])equipment.ringsLeft.Clone() : null,
            equippedRingsRight = equipment != null ? (string[])equipment.ringsRight.Clone() : null,
            durabilityHead = equipment != null ? equipment.headDurability : 0,
            durabilityShoulders = equipment != null ? equipment.shouldersDurability : 0,
            durabilityGloves = equipment != null ? equipment.glovesDurability : 0,
            durabilityBoots = equipment != null ? equipment.bootsDurability : 0,
            durabilityNeck = equipment != null ? equipment.neckDurability : 0,
            durabilityBelt = equipment != null ? equipment.beltDurability : 0,
            durabilityKnees = equipment != null ? equipment.kneesDurability : 0,
            durabilityRingsLeft = equipment != null ? (int[])equipment.ringsLeftDurability.Clone() : null,
            durabilityRingsRight = equipment != null ? (int[])equipment.ringsRightDurability.Clone() : null,
            currentWeaponDurability = controller.CurrentWeaponDurability,
        };
    }

    public static void Save(int seed, int floor, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller,
        PlayerEquipment equipment, IEnumerable<Vector2Int> clearedRooms = null, bool bossDefeated = false, PlayerLimbs limbs = null)
    {
        SaveData data = Capture(seed, floor, inventory, stats, health, stamina, controller, equipment, clearedRooms, bossDefeated, limbs);
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
        inventory.LoadState(data.slots, data.hotbarSlots, data.cursedItemId);

        stats.level = data.level;
        stats.experience = data.experience;
        stats.experienceToNextLevel = data.experienceToNextLevel > 0 ? data.experienceToNextLevel : stats.experienceToNextLevel;
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

        if (data.weaponLocked) controller.ForceEquipWeapon(data.currentWeapon);
        else controller.EquipWeapon(data.currentWeapon);
        controller.weaponHand = data.weaponHand;
        // Must run AFTER Equip/ForceEquipWeapon above - both reset currentWeaponDurability to
        // full, this overwrites it with the actually-saved (possibly worn-down) value.
        controller.SetCurrentWeaponDurability(data.currentWeaponDurability);

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
            equipment.head = data.equippedHead;
            equipment.shoulders = data.equippedShoulders;
            equipment.gloves = data.equippedGloves;
            equipment.boots = data.equippedBoots;
            equipment.neck = data.equippedNeck;
            equipment.belt = data.equippedBelt;
            equipment.knees = data.equippedKnees;
            equipment.ringsLeft = data.equippedRingsLeft != null
                ? (string[])data.equippedRingsLeft.Clone() : new string[PlayerEquipment.RingSlotsPerHand];
            equipment.ringsRight = data.equippedRingsRight != null
                ? (string[])data.equippedRingsRight.Clone() : new string[PlayerEquipment.RingSlotsPerHand];

            // Plain field restore too, same reasoning as the itemId fields just above - Set() would
            // reset every slot back to full durability instead of the saved (possibly worn-down) value.
            equipment.headDurability = data.durabilityHead;
            equipment.shouldersDurability = data.durabilityShoulders;
            equipment.glovesDurability = data.durabilityGloves;
            equipment.bootsDurability = data.durabilityBoots;
            equipment.neckDurability = data.durabilityNeck;
            equipment.beltDurability = data.durabilityBelt;
            equipment.kneesDurability = data.durabilityKnees;
            equipment.ringsLeftDurability = data.durabilityRingsLeft != null
                ? (int[])data.durabilityRingsLeft.Clone() : new int[PlayerEquipment.RingSlotsPerHand];
            equipment.ringsRightDurability = data.durabilityRingsRight != null
                ? (int[])data.durabilityRingsRight.Clone() : new int[PlayerEquipment.RingSlotsPerHand];
        }
    }
}
