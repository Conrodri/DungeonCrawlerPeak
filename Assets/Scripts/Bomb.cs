using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Bomb : MonoBehaviour
{
    public int damage = 3;
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
        // Area damage hits everything with a Health component in range, including the player -
        // standing in your own blast is a real risk, same as in Isaac.
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
        foreach (Collider2D hit in hits)
        {
            Health health = hit.GetComponent<Health>();
            if (health != null) health.TakeDamage(damage);

            // Bombing a locked door blows it open for good: RoomController skips a destroyed
            // blocker forever after, even across room resets.
            DoorBlocker blocker = hit.GetComponent<DoorBlocker>();
            if (blocker != null) Destroy(blocker.gameObject);
        }

        if (explosionSprite != null)
        {
            GameObject fx = new GameObject("Explosion", typeof(SpriteRenderer), typeof(AttackVisual));
            fx.transform.position = transform.position;
            fx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            SpriteRenderer renderer = fx.GetComponent<SpriteRenderer>();
            renderer.sprite = explosionSprite;
            renderer.sortingOrder = 1;
            fx.GetComponent<AttackVisual>().lifetime = 0.2f;
        }

        Destroy(gameObject);
    }
}
