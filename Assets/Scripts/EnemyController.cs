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

    const float BobAmplitude = 0.15f;
    const float BobSpeed = 4f;
    const float SpeedUpMultiplier = 1.6f;
    const float HpUpMultiplier = 2f;
    const float BadgeHeight = 0.7f;
    const float GlowScale = 1.8f;

    // Fired right before the GameObject is destroyed, so a room can tell this enemy apart from
    // one that was simply despawned (e.g. on room reset).
    public event Action OnDied;

    Rigidbody2D rb;
    Health health;
    CircleCollider2D bodyCollider;
    Transform target;
    float lastHitTime = -999f;
    Rect? roomBounds;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        bodyCollider = GetComponent<CircleCollider2D>();
        health.OnDeath += HandleDeath;
        // Extra tunneling guard: relentless FixedUpdate-driven velocity pressed against a
        // tilemap CompositeCollider2D can otherwise creep through a corner over many frames.
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    public void SetTarget(Transform t)
    {
        target = t;
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

        if (badgeIcon != null)
        {
            GameObject badgeGO = new GameObject("Badge", typeof(SpriteRenderer));
            badgeGO.transform.SetParent(transform, false);
            badgeGO.transform.localPosition = new Vector3(0f, BadgeHeight, 0f);
            badgeGO.transform.localScale = Vector3.one * 0.5f;
            SpriteRenderer badgeRenderer = badgeGO.GetComponent<SpriteRenderer>();
            badgeRenderer.sprite = badgeIcon;
            badgeRenderer.sortingOrder = 1;
        }
    }

    void FixedUpdate()
    {
        if (target == null) return;

        Vector2 toTarget = (Vector2)target.position - rb.position;
        if (toTarget.sqrMagnitude > 0.0001f) toTarget.Normalize();
        rb.linearVelocity = toTarget * moveSpeed;
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
