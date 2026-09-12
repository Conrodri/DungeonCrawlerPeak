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

    public static void Save(int seed, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller)
    {
        SaveData data = new SaveData
        {
            seed = seed,
            slots = inventory.GetAllSlots(),
            hotbarSlots = inventory.hotbarSlots,
            cursedItemId = inventory.CursedItemId,
            level = stats.level,
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
        };

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

    public static void Apply(SaveData data, PlayerInventory inventory, PlayerStats stats, Health health, Stamina stamina, PlayerController controller)
    {
        inventory.LoadState(data.slots, data.hotbarSlots, data.cursedItemId);

        stats.level = data.level;
        stats.force = data.force;
        stats.dexterite = data.dexterite;
        stats.intelligence = data.intelligence;
        stats.vitesse = data.vitesse;
        stats.constitution = data.constitution;
        stats.portee = data.portee;
        stats.charisme = data.charisme;
        stats.endurance = data.endurance;

        health.maxHealth = data.maxHealth;
        health.currentHealth = data.currentHealth;
        stamina.maxStamina = data.maxStamina;
        stamina.currentStamina = data.currentStamina;

        if (data.weaponLocked) controller.ForceEquipWeapon(data.currentWeapon);
        else controller.EquipWeapon(data.currentWeapon);
    }
}
