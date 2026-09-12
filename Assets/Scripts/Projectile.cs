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
        if (collision.collider.CompareTag("Player")) return;

        Health health = collision.collider.GetComponent<Health>();
        if (health != null) health.TakeDamage(damage);

        DestructibleObject destructible = collision.collider.GetComponent<DestructibleObject>();
        if (destructible != null) destructible.TryDamage(damage, attackerForce);

        Destroy(gameObject);
    }
}
