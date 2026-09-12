using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Bomb : MonoBehaviour
{
    public int damage = 3;
    // Explosives bypass any DestructibleObject.requiredForce gate - they're "a tool built for it".
    public int attackerForce = int.MaxValue;
    public float explosionRadius = 2f;
    public float fuseTime = 1.2f;
    public Sprite explosionSprite;

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Launch(Vector2 direction, float speed, float travelDistance)
    {
        rb.linearVelocity = direction.normalized * speed;
        float travelTime = travelDistance / Mathf.Max(speed, 0.01f);
        Invoke(nameof(StopMoving), travelTime);
        Invoke(nameof(Explode), fuseTime);
    }

    void StopMoving()
    {
        rb.linearVelocity = Vector2.zero;
    }

    void Explode()
    {
        ExplosionUtility.Explode(transform.position, explosionRadius, damage, attackerForce, explosionSprite);
        Destroy(gameObject);
    }
}
