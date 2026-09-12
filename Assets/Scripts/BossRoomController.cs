using System.Collections.Generic;
using UnityEngine;

// Unlike RoomController, a boss doesn't respawn on room re-entry - once dead, the room stays
// cleared for good. Doors are locked from the moment the floor loads (the boss already exists at
// generation time, there's no "spawn on first entry" step to gate on).
public class BossRoomController : MonoBehaviour
{
    public Vector2Int gridPos;
    public BossController boss;
    // Set here (a plain reference/value assigned at generation time survives serialization fine)
    // and applied to the boss in Start() - BossController.SetTarget/SetRoomBounds write to plain
    // private fields with no [SerializeField], so calling them from DungeonBootstrap (edit-time)
    // instead of here would silently reset to null on the next Play Mode scene reload.
    public Transform player;
    public Vector2 roomOrigin;
    public Vector2 roomSize;
    public List<GameObject> doorBlockers = new List<GameObject>();
    // Same shared-list convention as RoomController.exitTriggers - a door trigger registers
    // itself here at generation time via DungeonBootstrap.SpawnDoorTrigger.
    public List<DoorTrigger> exitTriggers = new List<DoorTrigger>();
    public VictoryBannerUI victoryBanner;
    public BossHealthBarUI healthBar;
    public string bossName = "Cerbere";

    bool defeated;

    void Start()
    {
        UpdateDoors();
        if (boss != null)
        {
            boss.SetTarget(player);
            boss.SetRoomBounds(new Rect(roomOrigin, roomSize));
            boss.OnDied += HandleBossDied;
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

        if (victoryBanner != null) victoryBanner.ShowVictory(bossName + " est vaincu !");
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
