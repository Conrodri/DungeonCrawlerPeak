using UnityEngine;

// Immune to normal attacks by design - only fire/a nearby explosion (see ExplosionUtility) sets it
// off, including a chain reaction from another barrel or an ignited FuelPuddle.
public class ExplosiveBarrel : MonoBehaviour
{
    public int damage = 3;
    public float explosionRadius = 2f;
    public Sprite explosionSprite;

    const float DetonateDelay = 0.15f;

    bool detonating;

    public void Detonate()
    {
        if (detonating) return;
        detonating = true;
        // Delayed rather than instant: avoids mutating colliders mid-iteration of the explosion
        // that triggered this one, and reads as a visible chain rather than one instant blast.
        Invoke(nameof(Explode), DetonateDelay);
    }

    void Explode()
    {
        ExplosionUtility.Explode(transform.position, explosionRadius, damage, int.MaxValue, explosionSprite);
        Destroy(gameObject);
    }
}
