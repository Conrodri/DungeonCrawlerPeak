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
    public EliteModifier modifier;
    public int xpReward = 1;

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

    // Fired right before the GameObject is destroyed, so a room can tell this enemy apart from
    // one that was simply despawned (e.g. on room reset).
    public event Action OnDied;

    Rigidbody2D rb;
    Health health;
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
        bodyCollider = GetComponent<CircleCollider2D>();
        statusIcons = GetComponent<StatusIconDisplay>();
        statusIcons.height = 0.7f; // shorter reach than the player's default - enemies read smaller on screen
        health.OnDeath += HandleDeath;
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

    static readonly Collider2D[] SeparationBuffer = new Collider2D[8];

    void FixedUpdate()
    {
        if (target == null || Time.time < activeAtTime)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 toTarget = (Vector2)target.position - rb.position;
        if (toTarget.sqrMagnitude > 0.0001f) toTarget.Normalize();

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
        rb.linearVelocity = moveDir * moveSpeed;
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

        targetHealth.TakeDamage(contactDamage);
        lastHitTime = Time.time;
    }

    void HandleDeath()
    {
        Debug.Log(name + " died.");
        LootTable.TryDropLoot(transform.position);
        if (target != null) target.GetComponent<PlayerStats>()?.AddExperience(xpReward);
        OnDied?.Invoke();
        Destroy(gameObject);
    }

    // Used when a room despawns its enemies (reset on re-entry) rather than them dying in combat -
    // no death log, no OnDied notification, since the owning RoomController already knows.
    public void Despawn()
    {
        Destroy(gameObject);
    }
}
