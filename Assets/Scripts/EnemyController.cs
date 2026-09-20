using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class EnemyController : MonoBehaviour
{
    public float moveSpeed = 2f;
    public int contactDamage = 1;
    public float contactCooldown = 1f;
    public bool isFlying;
    // Set by RoomController.SpawnEnemies right after creation - drives PlayerLimbs' hit-location
    // roll (see Health.TakeDamageFromEnemy) so a Zombie/ChauveSouris hit targets the right part.
    public EnemyType enemyType;
    // Rolled once at spawn from the current floor's range (see MonsterLeveling.RollLevel) - no
    // live leveling, this enemy instance never persists long enough to grow. Debug-only for now
    // (shown in the GameObject name, see RoomController.SpawnEnemies) - no dedicated UI.
    public int level = 1;
    public EliteModifier modifier;
    public int xpReward = 1;

    [Header("Dash Attack (tous les monstres sans projectile)")]
    // 2026-09-21 request: every regular melee monster (none of the 3 current species shoot
    // projectiles - only bosses do) now uses the same telegraphed attack pattern instead of just
    // walking into contact - "une fois a portee, un cast de 0.5 sec, dash sur quelques metres,
    // puis une fois le CD revenu, le coup se relance". Generalizes what used to be ChauveSouris-
    // only Rush (2026-09-14 spec) - the actual hit still lands through the existing contact-damage
    // path (OnCollision.../TryDamage below) once the dash carries this enemy into the player; this
    // block is purely the movement/telegraph state machine.
    public float dashRange = 4f;
    public float dashCastDuration = 0.5f;
    public float dashSpeed = 9f;
    public float dashDuration = 0.35f;
    public float dashCooldown = 3f;
    // Tints the sprite during the cast wind-up so the attack is actually telegraphed/dodgeable,
    // not just an invisible timer - reverts the instant the dash itself starts.
    static readonly Color DashCastTint = new Color(1f, 0.25f, 0.25f);

    enum DashState { None, Casting, Dashing }
    DashState dashState;
    float dashStateEndTime;
    Vector2 dashDirection;
    float lastDashTime = -999f;
    SpriteRenderer spriteRenderer;
    Color baseColor;

    const float BobAmplitude = 0.15f;
    const float BobSpeed = 4f;
    const float SpeedUpMultiplier = 1.6f;
    const float HpUpMultiplier = 2f;
    const float GlowScale = 1.8f;
    const string EliteIconKey = "Elite";
    // Grace period after the player is confirmed in the room (SetTarget, from RoomController.
    // ArmEnemies) before this enemy starts chasing - per feedback, mobs closing in the instant
    // the player steps through a door felt too fast/too close to react to. Deliberately armed on
    // room entry rather than on spawn: every enemy on the floor is created at floor-generation
    // time, long before the player ever reaches most rooms, so a spawn-time delay had already
    // elapsed by the time it mattered.
    const float ActivationDelay = 0.75f;
    // Chasing enemies otherwise beeline straight for the exact same point (the player) with no
    // awareness of each other, so a pack converges into a single overlapping stack right next to
    // whoever they're chasing - the room-entry fix stops them drifting together from far away, but
    // does nothing once they're all actually closing in at once. This pushes each enemy away from
    // any other enemy within SeparationRadius, blended with the chase direction.
    const float SeparationRadius = 1.4f;
    const float SeparationStrength = 1.8f;

    // Small knockback "pop" away from whoever landed the hit (2026-09-16 request, paired with the
    // player's own punch step - see PlayerController.MeleeAttack) - purely a movement flinch, no
    // cooldown like the player's own stagger (see [[project bugfix]] note there): a mob getting
    // chain-staggered by repeated hits reads as satisfying feedback, not as unfair loss of control,
    // since the player is the one causing it on purpose.
    const float StaggerDuration = 0.12f;
    const float StaggerSpeed = 5f;
    Vector2 staggerVelocity;
    float staggerEndTime = -999f;

    // 2026-09-20 request ("point 2"): give a broken monster limb a real gameplay effect instead of
    // just cosmetic HP - mirrors the player's own leg->speed / arm(weapon hand)->damage split (see
    // PlayerLimbs' class comment). A flying species has no legs in its layout at all (see
    // EnemyLimbLayout.Flyer) - its wings (ArmLeft/ArmRight) double as both its "legs" for movement
    // AND its "hands" for damage, so a broken wing hits both instead of just one.
    bool LegsImpaired => !isFlying && limbs != null && limbs.AnyLegBroken;
    bool WingsImpaired => isFlying && limbs != null && limbs.AnyArmBroken;
    bool HandsImpaired => limbs != null && limbs.AnyArmBroken;
    float EffectiveMoveSpeed => (LegsImpaired || WingsImpaired) ? moveSpeed * 0.5f : moveSpeed;
    int EffectiveContactDamage => HandsImpaired ? Mathf.Max(1, contactDamage / 2) : contactDamage;

    // Fired right before the GameObject is destroyed, so a room can tell this enemy apart from
    // one that was simply despawned (e.g. on room reset).
    public event Action OnDied;

    // Static, floor/room-independent signal for "any regular monster died in a real fight"
    // (2026-09-21 request) - used by QuestNpc's KillMonsters quest type. Only fired from
    // HandleDeath below, never from Despawn (a room reset/re-entry despawn isn't a kill).
    public static event Action OnAnyEnemyDied;

    Rigidbody2D rb;
    Health health;
    EnemyLimbs limbs;
    CircleCollider2D bodyCollider;
    StatusIconDisplay statusIcons;
    Transform target;
    float lastHitTime = -999f;
    Rect? roomBounds;
    float activeAtTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        limbs = GetComponent<EnemyLimbs>();
        bodyCollider = GetComponent<CircleCollider2D>();
        statusIcons = GetComponent<StatusIconDisplay>();
        statusIcons.height = 0.7f; // shorter reach than the player's default - enemies read smaller on screen
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseColor = spriteRenderer.color;
        health.OnDeath += HandleDeath;
        health.OnDamagedFrom += HandleDamagedFrom;
        // Extra tunneling guard: relentless FixedUpdate-driven velocity pressed against a
        // tilemap CompositeCollider2D can otherwise creep through a corner over many frames.
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    // Also (re-)arms the activation delay whenever a real target is given - see RoomController.
    // ArmEnemies/HandleRoomEntered, which is the only caller and only calls this once the player
    // is actually confirmed inside the room (null when they leave, to stop the enemy in place).
    public void SetTarget(Transform t)
    {
        target = t;
        if (t != null) activeAtTime = Time.time + ActivationDelay;
    }

    // Hard confinement to the spawning room, independent of wall/door-blocker collisions - an
    // enemy must never be able to reach the player from outside their current room.
    public void SetRoomBounds(Rect bounds)
    {
        roomBounds = bounds;
    }

    // Boosts this enemy's stats and adds a badge (above the head) + a glow (behind the body) so
    // the modifier reads at a glance, in addition to the stat change itself.
    public void ApplyModifier(EliteModifier mod, Sprite badgeIcon, Sprite glowSprite, Color glowColor)
    {
        modifier = mod;
        if (mod == EliteModifier.None) return;

        if (mod == EliteModifier.SpeedUp)
        {
            moveSpeed *= SpeedUpMultiplier;
        }
        else if (mod == EliteModifier.HpUp)
        {
            health.maxHealth = Mathf.RoundToInt(health.maxHealth * HpUpMultiplier);
            health.currentHealth = health.maxHealth;
        }

        if (glowSprite != null)
        {
            GameObject glowGO = new GameObject("Glow", typeof(SpriteRenderer));
            glowGO.transform.SetParent(transform, false);
            glowGO.transform.localScale = Vector3.one * GlowScale;
            SpriteRenderer glowRenderer = glowGO.GetComponent<SpriteRenderer>();
            glowRenderer.sprite = glowSprite;
            glowRenderer.color = glowColor;
            glowRenderer.sortingOrder = -1;
        }

        if (badgeIcon != null) statusIcons.ShowIcon(EliteIconKey, badgeIcon);
    }

    // Committed once started (no separation/steering blended in, same as the player's own roll) -
    // a straight line is the whole point, a curved "dash" would just look like a faster chase.
    // Aimed at wherever the target actually is NOW (cast just finished, not where they were when
    // the cast started) so a player who sidesteps mid-telegraph isn't guaranteed to still get hit.
    void BeginDash()
    {
        dashState = DashState.Dashing;
        Vector2 dir = target != null ? (Vector2)target.position - rb.position : Vector2.up;
        dashDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up;
        dashStateEndTime = Time.time + dashDuration;
        if (spriteRenderer != null) spriteRenderer.color = baseColor;
    }

    void HandleDamagedFrom(Vector2 fromPosition)
    {
        Vector2 away = rb.position - fromPosition;
        if (away.sqrMagnitude < 0.0001f) away = UnityEngine.Random.insideUnitCircle.normalized;
        staggerVelocity = away.normalized * StaggerSpeed;
        staggerEndTime = Time.time + StaggerDuration;
    }

    static readonly Collider2D[] SeparationBuffer = new Collider2D[8];

    void FixedUpdate()
    {
        if (Time.time < staggerEndTime)
        {
            rb.linearVelocity = staggerVelocity;
            return;
        }

        if (target == null || Time.time < activeAtTime)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (dashState == DashState.Casting)
        {
            rb.linearVelocity = Vector2.zero;
            if (Time.time >= dashStateEndTime) BeginDash();
            return;
        }

        if (dashState == DashState.Dashing)
        {
            rb.linearVelocity = dashDirection * (LegsImpaired ? dashSpeed * 0.5f : dashSpeed);
            if (Time.time >= dashStateEndTime)
            {
                dashState = DashState.None;
                lastDashTime = Time.time;
            }
            return;
        }

        Vector2 toTarget = (Vector2)target.position - rb.position;
        float distanceToTarget = toTarget.magnitude;
        if (toTarget.sqrMagnitude > 0.0001f) toTarget.Normalize();

        // Only WingsImpaired blocks the dash outright - flight is literally required for a flying
        // species' lunge. A broken leg used to do the same (real bug, 2026-09-21 report: "le
        // zombie du tuto fait une fois l'attaque et ne la relance JAMAIS") - a Grunt-layout Zombie
        // has 2 of its 4 parts as legs, each small enough for a single fist hit to break outright,
        // so this was permanently disabling its only attack after one unlucky early hit instead of
        // just slowing it down like the rest of the broken-leg penalty already does (see
        // EffectiveMoveSpeed, and the Dashing branch above, which now halves dash speed too).
        if (distanceToTarget <= dashRange && !WingsImpaired && Time.time - lastDashTime >= dashCooldown)
        {
            dashState = DashState.Casting;
            dashStateEndTime = Time.time + dashCastDuration;
            if (spriteRenderer != null) spriteRenderer.color = DashCastTint;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 separation = Vector2.zero;
        int hitCount = Physics2D.OverlapCircleNonAlloc(rb.position, SeparationRadius, SeparationBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = SeparationBuffer[i];
            if (hit == bodyCollider) continue;
            EnemyController other = hit.GetComponent<EnemyController>();
            if (other == null) continue;

            Vector2 away = rb.position - other.rb.position;
            float dist = away.magnitude;
            if (dist > 0.001f) separation += away / dist / dist; // stronger the closer they are
        }

        Vector2 moveDir = toTarget + separation * SeparationStrength;
        if (moveDir.sqrMagnitude > 0.0001f) moveDir.Normalize();
        rb.linearVelocity = moveDir * EffectiveMoveSpeed;
    }

    // Runs after Unity's physics step has already resolved this frame's collisions, so it catches
    // any drift a corner of the tilemap's composite collider let through - a clamp placed in
    // FixedUpdate instead would run BEFORE that resolution and miss exactly that drift.
    void LateUpdate()
    {
        if (!roomBounds.HasValue) return;

        Rect b = roomBounds.Value;
        float margin = 1f + bodyCollider.radius;
        Vector2 clamped = rb.position;
        clamped.x = Mathf.Clamp(clamped.x, b.xMin + margin, b.xMax - margin);
        clamped.y = Mathf.Clamp(clamped.y, b.yMin + margin, b.yMax - margin);
        if (clamped != rb.position) rb.position = clamped;

        // Purely visual hover: nudges the rendered position, not rb.position, so it never affects
        // physics/targeting - the next physics step resyncs the transform to rb.position anyway.
        if (isFlying) transform.position = rb.position + new Vector2(0f, Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    void TryDamage(Collider2D other)
    {
        if (Time.time - lastHitTime < contactCooldown) return;
        if (!other.CompareTag("Player")) return;

        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth == null) return;

        targetHealth.TakeDamageFromEnemy(EffectiveContactDamage, AttackSourceMapping.For(enemyType), rb.position);
        lastHitTime = Time.time;
    }

    void HandleDeath()
    {
        Debug.Log(name + " died.");
        // A corpse to examine (E) instead of loot silently auto-dropping - see Corpse.cs/
        // CorpseLoot.cs. Not guaranteed (CorpseLoot.Generate's 75% has-loot roll) - an ordinary
        // mob can still leave nothing behind, unlike a boss.
        PlayerInventory playerInventory = target != null ? target.GetComponent<PlayerInventory>() : null;
        var loot = CorpseLoot.Generate(isNpc: false);
        GameObject corpse = Corpse.SpawnAt(transform.position, "Cadavre de " + enemyType, loot, GetComponent<SpriteRenderer>().sprite, playerInventory);
        // Same parent as this enemy (its owning RoomController, itself under DungeonRoot) - without
        // this the corpse (like the old ItemPickup.SpawnAt ground drops it replaces) would survive
        // DungeonGenerator.Build()'s "destroy the old DungeonRoot" step and pile up across floors
        // forever, since nothing else ever destroys it.
        corpse.transform.SetParent(transform.parent);

        if (target != null) target.GetComponent<PlayerStats>()?.AddExperience(xpReward);
        OnDied?.Invoke();
        OnAnyEnemyDied?.Invoke();
        Destroy(gameObject);
    }

    // Used when a room despawns its enemies (reset on re-entry) rather than them dying in combat -
    // no death log, no OnDied notification, since the owning RoomController already knows.
    public void Despawn()
    {
        Destroy(gameObject);
    }
}
