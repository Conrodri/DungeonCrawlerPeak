using System;
using UnityEngine;

// Distinct from EnemyController on purpose - a boss's attack pattern (charge + projectile
// volley + a rage phase) doesn't fit the generic "chase and contact-damage" model.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class BossController : MonoBehaviour
{
    public float moveSpeed = 1.5f;
    public int contactDamage = 2;
    public float contactCooldown = 1f;

    [Header("Charge")]
    public float chargeSpeed = 8f;
    public float chargeDuration = 0.5f;
    public float chargeCooldown = 4f;

    [Header("Volley")]
    public Sprite projectileSprite;
    public int volleyProjectileCount = 5;
    public int volleyDamage = 1;
    public float volleyProjectileSpeed = 6f;
    public float volleySpreadAngle = 45f;
    public float volleyCooldown = 3.5f;

    [Header("Rage")]
    // Fraction of max HP at which the boss permanently speeds up and attacks more often.
    public float rageHealthFraction = 0.5f;
    public float rageSpeedMultiplier = 1.5f;
    public float rageCooldownMultiplier = 0.6f;

    public event Action OnDied;

    Rigidbody2D rb;
    Health health;
    CircleCollider2D bodyCollider;
    Transform target;
    Rect? roomBounds;

    float lastContactTime = -999f;
    float lastChargeTime = -999f;
    float lastVolleyTime = -999f;
    bool charging;
    Vector2 chargeDirection;
    float chargeEndTime;
    bool enraged;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        bodyCollider = GetComponent<CircleCollider2D>();
        health.OnDeath += HandleDeath;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    public void SetTarget(Transform t) => target = t;
    public void SetRoomBounds(Rect bounds) => roomBounds = bounds;

    void FixedUpdate()
    {
        if (target == null) return;

        if (!enraged && health.currentHealth <= health.maxHealth * rageHealthFraction) enraged = true;
        float cooldownScale = enraged ? rageCooldownMultiplier : 1f;

        if (charging)
        {
            rb.linearVelocity = chargeDirection * chargeSpeed;
            if (Time.time >= chargeEndTime) charging = false;
            return;
        }

        Vector2 toTarget = (Vector2)target.position - rb.position;
        Vector2 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
        rb.linearVelocity = dir * moveSpeed * (enraged ? rageSpeedMultiplier : 1f);

        if (Time.time - lastChargeTime >= chargeCooldown * cooldownScale)
        {
            lastChargeTime = Time.time;
            StartCharge(dir);
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
        }
    }

    void LateUpdate()
    {
        if (!roomBounds.HasValue) return;

        Rect b = roomBounds.Value;
        float margin = 1f + bodyCollider.radius;
        Vector2 clamped = rb.position;
        clamped.x = Mathf.Clamp(clamped.x, b.xMin + margin, b.xMax - margin);
        clamped.y = Mathf.Clamp(clamped.y, b.yMin + margin, b.yMax - margin);
        if (clamped != rb.position) rb.position = clamped;
    }

    void StartCharge(Vector2 dir)
    {
        if (dir == Vector2.zero) return;
        charging = true;
        chargeDirection = dir;
        chargeEndTime = Time.time + chargeDuration;
    }

    void FireVolley()
    {
        if (target == null) return;
        Vector2 baseDir = ((Vector2)target.position - rb.position).normalized;

        for (int i = 0; i < volleyProjectileCount; i++)
        {
            float t = volleyProjectileCount > 1 ? i / (float)(volleyProjectileCount - 1) : 0.5f;
            float angle = Mathf.Lerp(-volleySpreadAngle / 2f, volleySpreadAngle / 2f, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * baseDir;
            LaunchProjectile(dir);
        }
    }

    void LaunchProjectile(Vector2 direction)
    {
        GameObject go = new GameObject("BossProjectile", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Projectile));
        go.transform.position = (Vector2)transform.position + direction * 0.6f;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = projectileSprite;
        renderer.sortingOrder = 0;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;

        CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
        collider.radius = 0.15f;
        if (bodyCollider != null) Physics2D.IgnoreCollision(collider, bodyCollider);

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.damage = volleyDamage;
        projectile.speed = volleyProjectileSpeed;
        projectile.maxDistance = 20f;
        projectile.ignoreTag = ""; // must be able to hit the player, unlike a player-thrown one
        projectile.Launch(direction);
    }

    void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    void TryContactDamage(Collider2D other)
    {
        if (Time.time - lastContactTime < contactCooldown) return;
        if (!other.CompareTag("Player")) return;

        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth == null) return;

        targetHealth.TakeDamage(contactDamage);
        lastContactTime = Time.time;
    }

    void HandleDeath()
    {
        Debug.Log(name + " (boss) died.");
        OnDied?.Invoke();
        Destroy(gameObject);
    }
}
