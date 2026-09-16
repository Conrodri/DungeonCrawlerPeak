using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    public int damage = 1;
    // Default lets a non-player source (e.g. a future enemy projectile) always pass a
    // DestructibleObject's force gate; PlayerController overrides this with the caster's Force.
    public int attackerForce = int.MaxValue;
    public float speed = 8f;
    public float maxDistance = 5f;
    public float lifetime = 5f; // safety net in case maxDistance/speed make this unreachable
    // Collisions with this tag are ignored - lets a projectile pass through its own caster.
    // Player-thrown projectiles ignore "Player" (default); a boss projectile sets this empty so
    // it actually hits the player.
    public string ignoreTag = "Player";

    Rigidbody2D rb;
    Collider2D ownCollider;
    Vector2 startPos;
    // Replaces the old Destroy(gameObject, lifetime) delayed-destroy call - a pooled instance
    // (see ProjectilePool) is deactivated and requeued instead of destroyed, so its own expiry
    // has to be polled here rather than scheduled through Unity's Destroy timer.
    float spawnTime;
    // Whichever caster's collider this instance is CURRENTLY ignore-paired against (see
    // IgnoreCollisionWith) - Physics2D.IgnoreCollision pairs survive a GameObject being
    // deactivated/reactivated (only destroying either collider clears them), so a pooled instance
    // reused by a different caster must explicitly un-ignore its previous pairing first or it
    // could keep silently passing through that old caster's body forever.
    Collider2D ignoredCaster;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ownCollider = GetComponent<Collider2D>();
    }

    // Every caster (PlayerController/BossController) should call this instead of
    // Physics2D.IgnoreCollision directly - see ignoredCaster above for why.
    public void IgnoreCollisionWith(Collider2D casterCollider)
    {
        if (ignoredCaster != null) Physics2D.IgnoreCollision(ownCollider, ignoredCaster, false);
        if (casterCollider != null) Physics2D.IgnoreCollision(ownCollider, casterCollider, true);
        ignoredCaster = casterCollider;
    }

    // Fires in a fixed straight line: no homing or curving. Later items that bend trajectories
    // will hook in here.
    public void Launch(Vector2 direction)
    {
        startPos = transform.position;
        spawnTime = Time.time;
        rb.linearVelocity = direction.normalized * speed;
    }

    void Update()
    {
        if (Vector2.Distance(startPos, transform.position) >= maxDistance || Time.time - spawnTime >= lifetime)
            ProjectilePool.Release(gameObject);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!string.IsNullOrEmpty(ignoreTag) && collision.collider.CompareTag(ignoreTag)) return;

        // TakeDamageFromEnemy with AttackSource.Random (projectiles aren't tied to a specific
        // source) - a no-op fallback to plain TakeDamage against a non-player target (no
        // PlayerLimbs there), and a random body-part roll + armor mitigation on the rare
        // projectile that actually lands on the player (a boss volley - see BossController).
        Health health = collision.collider.GetComponent<Health>();
        if (health != null) health.TakeDamageFromEnemy(damage, AttackSource.Random, transform.position);

        DestructibleObject destructible = collision.collider.GetComponent<DestructibleObject>();
        if (destructible != null) destructible.TryDamage(damage, attackerForce);

        ProjectilePool.Release(gameObject);
    }
}
