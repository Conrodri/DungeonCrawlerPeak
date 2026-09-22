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
    // Orbe de Foudre support (2026-09-16 request) - 0 by default (no-op), so every other caster of
    // this same pooled Projectile (Staff, throwables, boss volleys) is unaffected. When set by
    // PlayerController.LaunchLightningOrb, OnCollisionEnter2D also damages every OTHER enemy within
    // chainRadius of the impact point, not just whatever this projectile directly hit - "rebondit
    // entre tous les ennemis a moins de 2 unites de l'impact".
    public float chainRadius = 0f;
    public Sprite chainBoltSprite;
    // Push tier for the player's ranged weapons (2026-09-22: Sling/Shuriken/Revolver/Bow) - see
    // Health.OnDamagedFrom. 1 (no-op) by default so every other caster (Staff, boss volleys) keeps
    // its current fixed push; explicitly reset by PlayerController.LaunchProjectile every launch,
    // same pool-hygiene reasoning as chainRadius/chainBoltSprite above.
    public float knockback = 1f;

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
        if (health != null) health.TakeDamageFromEnemy(damage, AttackSource.Random, transform.position, knockback);

        DestructibleObject destructible = collision.collider.GetComponent<DestructibleObject>();
        if (destructible != null) destructible.TryDamage(damage, attackerForce);

        if (chainRadius > 0f) ChainToNearbyEnemies(collision.collider);

        ProjectilePool.Release(gameObject);
    }

    // Only jumps to ENEMIES (EnemyController/BossController) - never the player, props or walls,
    // even though they can also sit inside chainRadius. The direct hit above already applied its
    // own damage/effects, so this only covers everyone ELSE in range.
    void ChainToNearbyEnemies(Collider2D directHit)
    {
        Vector2 impact = transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(impact, chainRadius);
        foreach (Collider2D hit in hits)
        {
            if (hit == directHit) continue;
            if (hit.GetComponent<EnemyController>() == null && hit.GetComponent<BossController>() == null) continue;

            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth == null) continue;
            targetHealth.TakeDamageFromEnemy(damage, AttackSource.Random, impact);
            SpawnBolt(impact, hit.transform.position);
        }
    }

    void SpawnBolt(Vector2 from, Vector2 to)
    {
        if (chainBoltSprite == null) return;

        GameObject fx = new GameObject("LightningBolt", typeof(SpriteRenderer), typeof(AttackVisual));
        fx.transform.position = (from + to) / 2f;
        float distance = Vector2.Distance(from, to);
        float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
        fx.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        fx.transform.localScale = new Vector3(Mathf.Max(distance, 0.01f), 0.15f, 1f);

        SpriteRenderer sr = fx.GetComponent<SpriteRenderer>();
        sr.sprite = chainBoltSprite;
        sr.sortingOrder = 1;
        fx.GetComponent<AttackVisual>().lifetime = 0.15f;
    }
}
