using UnityEngine;

// Floor hazard spawned at runtime by BossController's Cerbere "bave" attack (see SpitSlobber) -
// built in code like ItemPickup.SpawnAt/Corpse.SpawnAt rather than a prefab. Slows anything with a
// PlayerController standing in it (see PlayerController.ApplySlow) and despawns on its own.
[RequireComponent(typeof(CircleCollider2D))]
public class SlowPuddle : MonoBehaviour
{
    float slowMultiplier;
    float slowDuration;

    public static GameObject SpawnAt(Vector2 position, Sprite sprite, float slowMultiplier, float slowDuration, float lifetime)
    {
        GameObject go = new GameObject("Flaque de bave", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(SlowPuddle));
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

        Destroy(go, lifetime);
        return go;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null) player.ApplySlow(slowMultiplier, slowDuration);
    }
}
