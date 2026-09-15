using UnityEngine;

// Floor hazard spawned at runtime by BossController - originally just Cerbere's "bave" attack
// (see SpitSlobber) but also reused by Anaconda's venom bite (see DropPoisonPuddle) with the slow
// dialed out and a damage tick dialed in instead, since both are the same "stand in this, suffer"
// shape. Built in code like ItemPickup.SpawnAt/Corpse.SpawnAt rather than a prefab.
[RequireComponent(typeof(CircleCollider2D))]
public class SlowPuddle : MonoBehaviour
{
    float slowMultiplier;
    float slowDuration;
    int damagePerTick;
    float tickInterval;
    float lastTickTime = -999f;

    // damagePerTick/tickInterval default to 0/1 (no damage tick) so the original Cerbere
    // slobber call site - a pure slow, no poison - doesn't need to change.
    public static GameObject SpawnAt(Vector2 position, Sprite sprite, float slowMultiplier, float slowDuration, float lifetime, int damagePerTick = 0, float tickInterval = 1f)
    {
        GameObject go = new GameObject("Flaque", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(SlowPuddle));
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1;

        CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 1.1f;

        SlowPuddle puddle = go.GetComponent<SlowPuddle>();
        puddle.slowMultiplier = slowMultiplier;
        puddle.slowDuration = slowDuration;
        puddle.damagePerTick = damagePerTick;
        puddle.tickInterval = tickInterval;

        Destroy(go, lifetime);
        return go;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        if (slowMultiplier < 1f) player.ApplySlow(slowMultiplier, slowDuration);

        if (damagePerTick > 0 && Time.time - lastTickTime >= tickInterval)
        {
            lastTickTime = Time.time;
            Health health = other.GetComponent<Health>();
            if (health != null) health.TakeDamageFromEnemy(damagePerTick, AttackSource.Random);
        }
    }
}
