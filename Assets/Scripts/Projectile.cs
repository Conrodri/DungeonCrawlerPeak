using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    public int damage = 1;
    public float speed = 8f;
    public float lifetime = 3f;

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Fires in a fixed straight line: no homing or curving. Later items that bend trajectories
    // will hook in here.
    public void Launch(Vector2 direction)
    {
        rb.linearVelocity = direction.normalized * speed;
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag("Player")) return;

        Health health = collision.collider.GetComponent<Health>();
        if (health != null) health.TakeDamage(damage);

        Destroy(gameObject);
    }
}
