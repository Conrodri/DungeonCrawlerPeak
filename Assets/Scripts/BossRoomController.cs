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

    // Lets a BossKill-locked Staircase (see DungeonGenerator.SetupStaircase) unlock the moment
    // this floor's boss dies, without the staircase needing to poll anything itself.
    public event Action OnBossDefeated;

    // A boss is worth far more than a regular kill - big enough to reliably push a level on its own.
    const int BossXpReward = 20;

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
        ItemPickup.SpawnAt(dropPos, ItemIds.CerberusCollar, 1);
        LootTable.TryDropLoot(dropPos);
        if (player != null) player.GetComponent<PlayerStats>()?.AddExperience(BossXpReward);

        if (victoryBanner != null) victoryBanner.ShowVictory(bossName + " est vaincu !");
        OnBossDefeated?.Invoke();
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
