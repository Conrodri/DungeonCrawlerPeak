using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent, account-wide achievement tracking (2026-09-21 request). Unlike SaveManager's
// save.json (one active run, deleted on death/new game - see SaveManager.Delete), this survives
// across every run: its own achievements.json under Application.persistentDataPath. One singleton
// instance, recreated every floor alongside the rest of the HUD canvas (same convention as
// DialogueManager/VictoryBannerUI/RoomAnnouncementUI - see DungeonGenerator.Build, which parents
// the whole Canvas under DungeonRoot) - Awake()/OnDestroy() re-subscribe to the two
// floor-independent static kill events cleanly on every rebuild, and Load()/Save() keep the
// underlying progress itself alive across that same rebuild regardless.
public class AchievementManager : MonoBehaviour
{
    public static AchievementManager Instance { get; private set; }

    public AchievementToastUI toast;
    public AchievementVoice voice;

    readonly HashSet<string> unlocked = new HashSet<string>();
    int killCount;
    int goldCollected;

    static string SavePath => Path.Combine(Application.persistentDataPath, "achievements.json");

    [Serializable]
    class SaveData
    {
        public List<string> unlocked = new List<string>();
        public int killCount;
        public int goldCollected;
    }

    void Awake()
    {
        Instance = this;
        Load();
        EnemyController.OnAnyEnemyDied += HandleEnemyKilled;
        BossRoomController.OnAnyBossDefeated += HandleBossDefeated;
    }

    void OnDestroy()
    {
        EnemyController.OnAnyEnemyDied -= HandleEnemyKilled;
        BossRoomController.OnAnyBossDefeated -= HandleBossDefeated;
        if (Instance == this) Instance = null;
    }

    void HandleEnemyKilled()
    {
        killCount++;
        if (killCount == 1) Unlock("first_blood");
        if (killCount == 50) Unlock("hunter_50");
        if (killCount == 200) Unlock("hunter_200");
        Save();
    }

    void HandleBossDefeated(DungeonGenerator.BossTier tier)
    {
        switch (tier)
        {
            case DungeonGenerator.BossTier.Zone: Unlock("zone_slayer"); break;
            case DungeonGenerator.BossTier.Ville: Unlock("ville_slayer"); break;
            case DungeonGenerator.BossTier.Region: Unlock("region_slayer"); break;
        }
    }

    public void NotifyFloorReached(int floor)
    {
        if (floor >= 5) Unlock("floor_5");
        if (floor >= 10) Unlock("floor_10");
    }

    public void NotifyLevelReached(int level)
    {
        if (level >= 10) Unlock("level_10");
    }

    public void NotifyQuestCompleted()
    {
        Unlock("quest_done");
    }

    // Shared entry point for both the gold-total counter and the loot-rarity check - both are
    // driven by the same PlayerInventory.OnItemPickedUp event (see DungeonGenerator.Build).
    public void NotifyItemPickedUp(string itemId, int amount)
    {
        if (itemId == ItemIds.Gold)
        {
            goldCollected += amount;
            if (goldCollected >= 100) Unlock("rich");
            Save();
        }

        ItemDefinition definition = ItemDatabase.Get(itemId);
        // Rarity 5-6 (Legendaire/Mythique) - see ItemRarity.Max, nothing above that exists.
        if (definition != null && definition.Rarity >= ItemRarity.Max - 1) Unlock("legendary_find");
    }

    void Unlock(string id)
    {
        if (!unlocked.Add(id)) return; // already had it - nothing to announce again

        AchievementDefinition def = AchievementCatalog.Get(id);
        if (def == null) return;

        if (toast != null) toast.Enqueue(def.title, def.description);
        if (voice != null) voice.Play(id);
        Save();
    }

    void Load()
    {
        if (!File.Exists(SavePath)) return;
        try
        {
            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;
            unlocked.Clear();
            if (data.unlocked != null) foreach (string id in data.unlocked) unlocked.Add(id);
            killCount = data.killCount;
            goldCollected = data.goldCollected;
        }
        catch (Exception e)
        {
            Debug.LogWarning("AchievementManager: failed to load " + SavePath + " - " + e.Message);
        }
    }

    void Save()
    {
        SaveData data = new SaveData { unlocked = new List<string>(unlocked), killCount = killCount, goldCollected = goldCollected };
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
    }
}
