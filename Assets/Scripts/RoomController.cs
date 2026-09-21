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
        public EnemyType type;
        public EliteModifier modifier;
    }

    [Serializable]
    public struct EnemyPresetEntry
    {
        public EnemyType type;
        public Sprite sprite;
        // No moveSpeed here anymore - MonsterLeveling.ApplyLevelStats now computes it entirely
        // from the species' Vitesse band (see MonsterLeveling.BaseSpeed), a flat per-preset number
        // would just be silently overwritten.
        public int maxHealth;
        public int contactDamage;
        public bool isFlying;
        public int xpReward;
        // Only set for a species with a ranged attack (Sorcier today - see
        // EnemyController.FireSpell) - null/unused for every melee species.
        public Sprite projectileSprite;
    }

    public Vector2Int gridPos;
    // Every grid cell this room occupies - just {gridPos} for an ordinary single-cell room, or
    // every cell of a merged multi-cell room (see DungeonGenerator.MergeMultiCellMonsterRooms).
    // Re-entering ANY of them should be treated as re-entering this same room.
    public Vector2Int[] memberCells = new Vector2Int[0];
    public Vector2 roomOrigin;
    public Vector2 roomSize;
    public RoomCameraController roomCamera;
    public Transform player;
    public EnemyPresetEntry[] presets;
    public Sprite speedUpBadge;
    public Sprite hpUpBadge;
    public Sprite glowSprite;
    public EnemySpawn[] recipe;
    public List<GameObject> doorBlockers = new List<GameObject>();
    // This room's own door triggers (the ones sitting inside it) - switched solid while locked
    // (see DoorTrigger.SetLocked) to block LEAVING, with no visible barrier needed. Entering a
    // locked room is never blocked this way - only leaving it before it's cleared.
    public List<DoorTrigger> exitTriggers = new List<DoorTrigger>();
    // Set at generation time (see DungeonGenerator.SetupMonsterRoom) when restoring a save whose
    // clearedRoomsThisFloor already includes one of memberCells - skips SpawnEnemies() entirely
    // instead of spawning a fresh roster and immediately despawning it, so a resumed save never
    // re-triggers loot/XP or a visible respawn-then-clear flicker.
    public bool startCleared;

    public bool IsLocked { get; private set; }
    public bool IsCleared { get; private set; }

    // Fired once, the moment this room's original monsters are all dead - lets the minimap (and
    // anything else) react even if the player has already left the room. Carries every cell this
    // room occupies (see memberCells) so a merged room's minimap footprint clears all at once.
    public event Action<Vector2Int[]> OnRoomCleared;

    readonly List<EnemyController> liveEnemies = new List<EnemyController>();
    bool hasBeenEnteredBefore;
    // Whether the player is currently inside this room (any of memberCells) - drives whether
    // enemies have a target at all. Without this, every enemy on the floor was targeting the
    // player's raw world position from the moment the floor loaded, regardless of which room the
    // player was actually in - clamped to its own room by SetRoomBounds, but still drifting
    // toward whichever wall faced the player the whole time they explored elsewhere, so a room
    // could already have its monsters waiting right at the door by the time it was opened.
    bool playerPresent;

    void Start()
    {
        if (startCleared)
        {
            // IsCleared is already true before UpdateDoors() runs, so its own
            // "just became cleared" branch never re-fires OnRoomCleared here - the minimap already
            // learned about this room directly (see DungeonGenerator's preClearedRoomGridPositions)
            // rather than through that event, which nothing has subscribed to yet this early anyway.
            IsCleared = true;
        }
        else
        {
            SpawnEnemies();
        }
        UpdateDoors();
        if (roomCamera != null)
        {
            roomCamera.OnRoomEntered += HandleRoomEntered;
        }
        else
        {
            // No room-transition tracking available (the tutorial's single hand-built room has no
            // RoomCameraController) - there's only one room, so the player is always in it.
            playerPresent = true;
            ArmEnemies();
        }
    }

    void OnDestroy()
    {
        if (roomCamera != null) roomCamera.OnRoomEntered -= HandleRoomEntered;
    }

    void HandleRoomEntered(Vector2Int enteredGridPos)
    {
        bool isMine = System.Array.IndexOf(memberCells, enteredGridPos) >= 0;

        if (!isMine)
        {
            // The player just entered some OTHER room - if they were in this one, they just left
            // it. Clear every live enemy's target so they stop chasing (and moving at all) the
            // instant the player is no longer around to see it.
            if (playerPresent)
            {
                playerPresent = false;
                foreach (EnemyController enemy in liveEnemies) if (enemy != null) enemy.SetTarget(null);
            }
            return;
        }

        // Captured BEFORE the assignment below: a merged multi-cell room (see memberCells) fires
        // OnRoomEntered again just from walking between two of its OWN cells, with isMine true
        // both times and the player never actually having left - without this, that internal seam
        // crossing fell through to the "returning after leaving" branch below and wiped/respawned
        // a full-health roster on top of a fight already in progress (real bug, found 2026-09-16).
        bool wasAlreadyPresent = playerPresent;
        playerPresent = true;
        if (IsCleared) return; // a cleared room's monsters never come back

        if (!hasBeenEnteredBefore)
        {
            hasBeenEnteredBefore = true;
            ArmEnemies(); // first arrival - roster already spawned untargeted, arm it now
            return;
        }

        if (wasAlreadyPresent) return; // still in this same room, just crossed an internal seam

        foreach (EnemyController enemy in liveEnemies)
        {
            if (enemy != null) enemy.Despawn();
        }
        liveEnemies.Clear();
        SpawnEnemies();
        UpdateDoors();
        ArmEnemies();
    }

    // Gives every currently-live enemy its target (and, via EnemyController.SetTarget, a fresh
    // activation delay) - called only once the player is actually confirmed inside the room.
    void ArmEnemies()
    {
        foreach (EnemyController enemy in liveEnemies) if (enemy != null) enemy.SetTarget(player);
    }

    void SpawnEnemies()
    {
        foreach (EnemySpawn spawn in recipe)
        {
            EnemyPresetEntry preset = FindPreset(spawn.type);
            int floor = Mathf.Max(1, DungeonGenerator.CurrentFloor);
            int level = MonsterLeveling.RollLevel(floor);

            GameObject enemy = new GameObject(spawn.type + " Niv." + level,
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(EnemyLimbs), typeof(StatusIconDisplay), typeof(EnemyController));
            // "les mobs volants passent au travers de tous les murs et objets bloquants" (2026-09-15
            // request) - see DungeonGenerator.BlockingLayer/FlyingLayer, IgnoreLayerCollision set up
            // once per Build(). A non-flying enemy stays on Default, unaffected.
            if (preset.isFlying) enemy.layer = DungeonGenerator.FlyingLayer;
            enemy.transform.SetParent(transform);
            enemy.transform.position = roomOrigin + spawn.localOffset;

            SpriteRenderer renderer = enemy.GetComponent<SpriteRenderer>();
            renderer.sprite = preset.sprite;
            renderer.sortingOrder = 0;
            // Some biome floors/walls are close in value to a mob's own tint - an outline reads
            // regardless of what's behind it (2026-09-16 request).
            enemy.AddComponent<SpriteOutline>();

            Rigidbody2D body = enemy.GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            enemy.GetComponent<CircleCollider2D>().radius = 0.4f;

            // Force/Vitesse scale with this instance's rolled level within its species' band (see
            // MonsterLeveling.ApplyLevelStats); Constitution instead scales with the FLOOR, not the
            // level, so depth always has a real HP floor regardless of how a monster's level rolled.
            float scaledMoveSpeed = 0f; // fully computed by ApplyLevelStats below, not read beforehand
            int scaledContactDamage = preset.contactDamage;
            MonsterLeveling.ApplyLevelStats(spawn.type, level, floor, ref scaledMoveSpeed, ref scaledContactDamage);

            Health health = enemy.GetComponent<Health>();
            health.maxHealth = preset.maxHealth + MonsterLeveling.ConstitutionBonusForFloor(floor);
            health.currentHealth = health.maxHealth;

            EnemyController controller = enemy.GetComponent<EnemyController>();
            controller.enemyType = spawn.type;
            controller.level = level;
            controller.moveSpeed = scaledMoveSpeed;
            controller.contactDamage = scaledContactDamage;
            controller.isFlying = preset.isFlying;
            controller.projectileSprite = preset.projectileSprite;
            // Floor-scaled (see DungeonGenerator.RegionBossXpFor) - a flat reward regardless of
            // floor couldn't keep pace with a per-floor XP budget that grows several times over
            // from one floor to the next.
            controller.xpReward = preset.xpReward * floor;
            // Untargeted until ArmEnemies() confirms the player is actually in the room - see
            // playerPresent above.
            controller.SetRoomBounds(new Rect(roomOrigin, roomSize));

            Sprite badge = spawn.modifier == EliteModifier.SpeedUp ? speedUpBadge
                : spawn.modifier == EliteModifier.HpUp ? hpUpBadge : null;
            Color glowColor = spawn.modifier == EliteModifier.SpeedUp ? Color.white
                : spawn.modifier == EliteModifier.HpUp ? Color.red : Color.clear;
            controller.ApplyModifier(spawn.modifier, badge, glowSprite, glowColor);

            // After ApplyModifier so a rolled HpUp elite (which multiplies health.maxHealth
            // directly) splits its POST-modifier total across limbs, not the pre-roll one -
            // "chaque monstre a le meme systeme de membre que le joueur" (2026-09-20 request).
            enemy.GetComponent<EnemyLimbs>().Configure(EnemyLimbLayout.For(spawn.type), health.maxHealth);

            controller.OnDied += () => HandleEnemyDied(controller);

            liveEnemies.Add(controller);
        }
    }

    EnemyPresetEntry FindPreset(EnemyType type)
    {
        foreach (EnemyPresetEntry preset in presets)
        {
            if (preset.type == type) return preset;
        }
        return presets.Length > 0 ? presets[0] : default;
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
            OnRoomCleared?.Invoke(memberCells);
        }
    }
}
