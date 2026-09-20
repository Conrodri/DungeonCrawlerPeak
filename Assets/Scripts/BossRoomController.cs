using System;
using System.Collections.Generic;
using UnityEngine;

// Unlike RoomController, a boss doesn't respawn on room re-entry - once dead, the room stays
// cleared for good. Doors are locked from the moment the floor loads (the boss already exists at
// generation time, there's no "spawn on first entry" step to gate on).
public class BossRoomController : MonoBehaviour
{
    public Vector2Int gridPos;
    // Every grid cell this room occupies - {gridPos} for an ordinary single-cell Boss room, or
    // every cell of a merged arena (see DungeonGenerator.BossArenaMergeChance). Same convention as
    // RoomController.memberCells.
    public Vector2Int[] memberCells = new Vector2Int[0];
    public BossController boss;
    // Set here (a plain reference/value assigned at generation time survives serialization fine)
    // and applied to the boss in Start()/HandleRoomEntered - BossController.SetTarget/
    // SetRoomBounds write to plain private fields with no [SerializeField], so calling them from
    // DungeonBootstrap (edit-time) instead of here would silently reset to null on the next Play
    // Mode scene reload.
    public Transform player;
    public Vector2 roomOrigin;
    public Vector2 roomSize;
    // Only shows the health bar once the player actually steps into this room - without this gate,
    // BossRoomController.Start() (which runs the moment the floor loads, wherever this room sits)
    // would bind the bar and reveal it at the top of the screen from the very first frame.
    public RoomCameraController roomCamera;
    public List<GameObject> doorBlockers = new List<GameObject>();
    // Same shared-list convention as RoomController.exitTriggers - a door trigger registers
    // itself here at generation time via DungeonBootstrap.SpawnDoorTrigger.
    public List<DoorTrigger> exitTriggers = new List<DoorTrigger>();
    public VictoryBannerUI victoryBanner;
    public BossHealthBarUI healthBar;
    public string bossName = "Cerbere";
    // Set at generation time (see DungeonGenerator.SetupBossRoom) when restoring a save whose
    // bossDefeatedThisFloor was already true - Start() destroys the boss immediately instead of
    // letting it fight again, so resuming a save never re-grants its loot/XP or replays the fight.
    public bool startDefeated;
    // Tier-specific reward (see DungeonGenerator.BossTierStatsFor) - a floor now has 3 bosses
    // (Zone/Ville/Region) instead of 1, each a different power level of the same biome family.
    public string dropItemId;
    public float dropChance;
    public int xpReward;
    // Set in DungeonGenerator.SetupBossRoom - which of the 3 power tiers this room's boss is, so
    // OnAnyBossDefeated below (and QuestNpc's boss-kill quests) can tell them apart.
    public DungeonGenerator.BossTier tier;

    // Lets a BossKill-locked Staircase (see DungeonGenerator.SetupStaircase) unlock the moment
    // this floor's boss dies, without the staircase needing to poll anything itself.
    public event Action OnBossDefeated;

    // Static, floor-independent signal for "a boss of this tier just died in a real fight" - used
    // by QuestNpc's KillZoneBoss/KillVilleBoss/KillRegionBoss quest types (2026-09-21 request) so
    // they don't need a reference to whichever specific BossRoomController this floor happens to
    // have for their tier. Deliberately only fired from HandleBossDied below, never from Start()'s
    // startDefeated/resumed-save branch - bossDefeatedThisFloor (see DungeonGenerator) only tracks
    // "some boss died this floor" as a single flag, not per-tier, so firing it there too could mark
    // an unrelated tier's quest complete. A quest asking for an already-dead tier on a resumed save
    // is a known, narrow edge case left alone rather than building full per-tier save tracking for it.
    public static event Action<DungeonGenerator.BossTier> OnAnyBossDefeated;

    bool defeated;
    bool healthBarBound;

    void Start()
    {
        if (startDefeated)
        {
            defeated = true;
            if (boss != null) Destroy(boss.gameObject);
            boss = null;
            UpdateDoors();
            // Still raised (see SetupStaircase's subscription happening synchronously during
            // Build(), well before this deferred Start() call) so a BossKill-locked staircase on a
            // resumed save correctly starts unlocked instead of waiting for a fight that already
            // happened last session.
            OnBossDefeated?.Invoke();
            return;
        }

        UpdateDoors();
        if (boss != null)
        {
            // Not SetTarget here - the boss stays put (no target) until the player is actually
            // confirmed in the room (HandleRoomEntered). Same reasoning as RoomController's
            // enemies: targeting the player's raw position from the moment the floor loads let
            // the boss chase/drift away from its spawn point long before the player arrived - on
            // a merged multi-cell arena (DungeonGenerator.BossArenaMergeChance) that arena can be
            // much bigger than the room the camera is currently showing, so a boss that already
            // wandered off could read as "no boss in this room" until the camera happened to
            // scroll to wherever it ended up.
            boss.SetRoomBounds(new Rect(roomOrigin, roomSize));
            boss.OnDied += HandleBossDied;
        }
        if (roomCamera != null) roomCamera.OnRoomEntered += HandleRoomEntered;
    }

    void OnDestroy()
    {
        if (roomCamera != null) roomCamera.OnRoomEntered -= HandleRoomEntered;
    }

    void HandleRoomEntered(Vector2Int enteredGridPos)
    {
        if (Array.IndexOf(memberCells, enteredGridPos) < 0 || boss == null) return;

        boss.SetTarget(player);

        if (!healthBarBound)
        {
            healthBarBound = true;
            if (healthBar != null) healthBar.Bind(boss.GetComponent<Health>());
        }
    }

    void HandleBossDied()
    {
        defeated = true;
        UpdateDoors();

        Vector2 dropPos = boss.transform.position;
        // A corpse to examine (E) instead of loot silently auto-dropping - see Corpse.cs/
        // CorpseLoot.cs. guaranteed: true - a boss is never empty-handed (this is what the
        // "killed the Region boss, got nothing" report was actually hitting: the family trophy's
        // own dropChance AND the old generic LootTable.TryDropLoot roll could both whiff at once).
        // The family trophy (if it rolls) still rides along in the same corpse rather than a
        // separate ground pickup.
        var loot = CorpseLoot.Generate(isNpc: false, guaranteed: true);
        if (!string.IsNullOrEmpty(dropItemId) && UnityEngine.Random.value < dropChance) loot.Add((dropItemId, 1));
        PlayerInventory playerInventory = player != null ? player.GetComponent<PlayerInventory>() : null;
        GameObject corpse = Corpse.SpawnAt(dropPos, "Depouille de " + bossName, loot, boss.GetComponent<SpriteRenderer>().sprite, playerInventory);
        // Same parent as the boss itself (DungeonRoot) - see the matching comment in
        // EnemyController.HandleDeath for why this matters (otherwise never cleaned up on Build()).
        corpse.transform.SetParent(boss.transform.parent);

        if (player != null) player.GetComponent<PlayerStats>()?.AddExperience(xpReward);

        if (victoryBanner != null) victoryBanner.ShowVictory(bossName + " est vaincu !");
        OnBossDefeated?.Invoke();
        OnAnyBossDefeated?.Invoke(tier);
    }

    void UpdateDoors()
    {
        bool locked = !defeated;
        foreach (GameObject blocker in doorBlockers)
        {
            if (blocker != null) blocker.SetActive(locked);
        }
        foreach (DoorTrigger trigger in exitTriggers)
        {
            if (trigger != null) trigger.SetLocked(locked);
        }
    }
}
