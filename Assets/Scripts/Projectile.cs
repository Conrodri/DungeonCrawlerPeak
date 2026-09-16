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
    Vector2 startPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Fires in a fixed straight line: no homing or curving. Later items that bend trajectories
    // will hook in here.
    public void Launch(Vector2 direction)
    {
        startPos = transform.position;
        rb.linearVelocity = direction.normalized * speed;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (Vector2.Distance(startPos, transform.position) >= maxDistance) Destroy(gameObject);
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

        Destroy(gameObject);
    }
}
