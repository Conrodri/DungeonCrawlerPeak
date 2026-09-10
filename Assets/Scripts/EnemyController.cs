using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class EnemyController : MonoBehaviour
{
    public float moveSpeed = 2f;
    public int contactDamage = 1;
    public float contactCooldown = 1f;
    public bool isElite;

    // Fired right before the GameObject is destroyed, so a room can tell this enemy apart from
    // one that was simply despawned (e.g. on room reset).
    public event Action OnDied;

    Rigidbody2D rb;
    Health health;
    Transform target;
    float lastHitTime = -999f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        health.OnDeath += HandleDeath;
    }

    public void SetTarget(Transform t)
    {
        target = t;
    }

    void FixedUpdate()
    {
        if (target == null) return;

        Vector2 toTarget = (Vector2)target.position - rb.position;
        if (toTarget.sqrMagnitude > 0.0001f) toTarget.Normalize();
        rb.linearVelocity = toTarget * moveSpeed;
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
