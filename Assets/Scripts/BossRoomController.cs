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
    // and applied to the boss in Start() - BossController.SetTarget/SetRoomBounds write to plain
    // private fields with no [SerializeField], so calling them from DungeonBootstrap (edit-time)
    // instead of here would silently reset to null on the next Play Mode scene reload.
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

    // Lets a BossKill-locked Staircase (see DungeonGenerator.SetupStaircase) unlock the moment
    // this floor's boss dies, without the staircase needing to poll anything itself.
    public event Action OnBossDefeated;

    bool defeated;
    bool healthBarBound;

    void Start()
    {
        UpdateDoors();
        if (boss != null)
        {
            boss.SetTarget(player);
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
        if (Array.IndexOf(memberCells, enteredGridPos) < 0 || healthBarBound || boss == null) return;
        healthBarBound = true;
        if (healthBar != null) healthBar.Bind(boss.GetComponent<Health>());
    }

    void HandleBossDied()
    {
        defeated = true;
        UpdateDoors();

        Vector2 dropPos = boss.transform.position;
        ItemPickup.SpawnAt(dropPos, ItemIds.CerberusCollar, 1);
        LootTable.TryDropLoot(dropPos);

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
