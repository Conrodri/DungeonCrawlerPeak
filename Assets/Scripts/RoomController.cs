using System;
using System.Collections.Generic;
using UnityEngine;

// Owns one monster room's enemy roster and door locks. Doors stay closed while any of this
// room's enemies are alive, and reopen once they're all dead. Leaving and re-entering the room
// resets it back to its original roster - unless a door was bombed open, which stays open for
// good (its blocker is simply destroyed, so it's skipped on every future lock/unlock pass).
public class RoomController : MonoBehaviour
{
    [Serializable]
    public struct EnemySpawn
    {
        public Vector2 localOffset;
        public bool isElite;
    }

    public Vector2Int gridPos;
    public Vector2 roomOrigin;
    public Vector2 roomSize;
    public RoomCameraController roomCamera;
    public Transform player;
    public Sprite enemySprite;
    public Sprite eliteSprite;
    public EnemySpawn[] recipe;
    public List<GameObject> doorBlockers = new List<GameObject>();
    // This room's own door triggers (the ones sitting inside it) - switched solid while locked
    // (see DoorTrigger.SetLocked) to block LEAVING, with no visible barrier needed. Entering a
    // locked room is never blocked this way - only leaving it before it's cleared.
    public List<DoorTrigger> exitTriggers = new List<DoorTrigger>();

    public bool IsLocked { get; private set; }
    public bool IsCleared { get; private set; }

    // Fired once, the moment this room's original monsters are all dead - lets the minimap (and
    // anything else) react even if the player has already left the room.
    public event Action<Vector2Int> OnRoomCleared;

    readonly List<EnemyController> liveEnemies = new List<EnemyController>();
    bool hasBeenEnteredBefore;

    void Start()
    {
        SpawnEnemies();
        UpdateDoors();
        if (roomCamera != null) roomCamera.OnRoomEntered += HandleRoomEntered;
    }

    void OnDestroy()
    {
        if (roomCamera != null) roomCamera.OnRoomEntered -= HandleRoomEntered;
    }

    void HandleRoomEntered(Vector2Int enteredGridPos)
    {
        if (enteredGridPos != gridPos) return;
        if (IsCleared) return; // a cleared room's monsters never come back

        if (!hasBeenEnteredBefore)
        {
            hasBeenEnteredBefore = true;
            return; // first arrival - the initial roster is already freshly spawned
        }

        foreach (EnemyController enemy in liveEnemies)
        {
            if (enemy != null) enemy.Despawn();
        }
        liveEnemies.Clear();
        SpawnEnemies();
        UpdateDoors();
    }

    void SpawnEnemies()
    {
        foreach (EnemySpawn spawn in recipe)
        {
            GameObject enemy = new GameObject(spawn.isElite ? "EliteEnemy" : "Enemy",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(EnemyController));
            enemy.transform.SetParent(transform);
            enemy.transform.position = roomOrigin + spawn.localOffset;

            SpriteRenderer renderer = enemy.GetComponent<SpriteRenderer>();
            renderer.sprite = spawn.isElite ? eliteSprite : enemySprite;
            renderer.sortingOrder = 0;
            if (spawn.isElite) enemy.transform.localScale = Vector3.one * 1.4f;

            Rigidbody2D body = enemy.GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            enemy.GetComponent<CircleCollider2D>().radius = 0.4f;

            Health health = enemy.GetComponent<Health>();
            health.maxHealth = spawn.isElite ? 4 : 2;
            health.currentHealth = health.maxHealth;

            EnemyController controller = enemy.GetComponent<EnemyController>();
            controller.isElite = spawn.isElite;
            // Damage is in half-heart units on the player's Health: a normal hit costs 0.5
            // heart (1), an elite hit costs a full heart (2).
            controller.contactDamage = spawn.isElite ? 2 : 1;
            controller.SetTarget(player);
            controller.SetRoomBounds(new Rect(roomOrigin, roomSize));
            controller.OnDied += () => HandleEnemyDied(controller);

            liveEnemies.Add(controller);
        }
    }

    void HandleEnemyDied(EnemyController enemy)
    {
        liveEnemies.Remove(enemy);
        UpdateDoors();
    }

    // Safety net: if an enemy is ever destroyed without its OnDied notification reaching us
    // (defensive against ordering/edge cases), a stale null still counts as "gone" here so the
    // door doesn't stay locked over a room that is actually already clear.
    void Update()
    {
        if (liveEnemies.RemoveAll(e => e == null) > 0) UpdateDoors();
    }

    void UpdateDoors()
    {
        IsLocked = liveEnemies.Count > 0;
        foreach (GameObject blocker in doorBlockers)
        {
            if (blocker != null) blocker.SetActive(IsLocked);
        }
        foreach (DoorTrigger trigger in exitTriggers)
        {
            if (trigger != null) trigger.SetLocked(IsLocked);
        }

        if (!IsLocked && !IsCleared)
        {
            IsCleared = true;
            OnRoomCleared?.Invoke(gridPos);
        }
    }
}
