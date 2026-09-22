using UnityEngine;

// The growing wind-up before a Fireball actually launches (2026-09-19 request: "fera grossir un
// projectile devant soi puis le lancera") - sits in front of the player, scaling up over
// chargeDuration, then converts into a real pooled Projectile and destroys itself. Purely a
// visual+timing wind-up (see PlayerController.TryCastFireball) - doesn't lock movement/aiming.
public class FireballCharge : MonoBehaviour
{
    public float chargeDuration = 0.5f;
    public float startScale = 0.35f;
    public float endScale = 1.1f;
    public Vector2 direction = Vector2.down;
    public float speed = 7f;
    public float maxDistance = 9f;
    public int damage = 5;
    public int attackerForce = int.MaxValue;
    public Collider2D ignoreCollider;
    public string ignoreTag = "Player";

    Transform followParent;
    Vector2 localOffset;
    float startTime;

    public void Begin(Transform parent, Vector2 offset)
    {
        followParent = parent;
        localOffset = offset;
        startTime = Time.time;
        transform.position = (Vector2)parent.position + offset;
        transform.localScale = Vector3.one * startScale;
    }

    void Update()
    {
        if (followParent != null) transform.position = (Vector2)followParent.position + localOffset;

        float t = Mathf.Clamp01((Time.time - startTime) / chargeDuration);
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, t);

        if (t >= 1f) Launch();
    }

    void Launch()
    {
        GameObject go = ProjectilePool.Get();
        go.transform.position = transform.position;

        SpriteRenderer sourceRenderer = GetComponent<SpriteRenderer>();
        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sourceRenderer.sprite;
        renderer.sortingOrder = 0;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;

        // Bigger than the default projectile radius (0.15) - it did just grow, after all.
        CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
        collider.radius = 0.28f;

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.IgnoreCollisionWith(ignoreCollider);
        projectile.damage = damage;
        projectile.attackerForce = attackerForce;
        projectile.speed = speed;
        projectile.maxDistance = maxDistance;
        projectile.ignoreTag = ignoreTag;
        projectile.chainRadius = 0f;
        projectile.chainBoltSprite = null;
        // Pool hygiene, same reasoning as chainRadius above - a pooled instance previously fired by
        // one of the player's ranged weapons (Sling/Shuriken/Revolver/Bow) could otherwise leak its
        // push tier onto this spell (2026-09-22).
        projectile.knockback = 1f;
        projectile.Launch(direction);

        Destroy(gameObject);
    }
}
