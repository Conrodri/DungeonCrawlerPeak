using UnityEngine;

// Floor decal, not an obstacle - ignites when an explosion happens on it (see ExplosionUtility)
// and burns anything standing in it for a few seconds. Does not itself re-ignite neighbors, to
// keep this first pass from cascading uncontrollably.
public class FuelPuddle : MonoBehaviour
{
    public int burnDamage = 1;
    public float burnTickInterval = 0.5f;
    public float burnDuration = 4f;
    public Color litColor = new Color(0.95f, 0.4f, 0.1f);

    SpriteRenderer sr;
    Color baseColor;
    bool burning;
    float lastTickTime = -999f;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr.color;
    }

    public void Ignite()
    {
        if (burning) return;
        burning = true;
        sr.color = litColor;
        Invoke(nameof(Extinguish), burnDuration);
    }

    void Extinguish()
    {
        burning = false;
        sr.color = baseColor;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!burning) return;
        if (Time.time - lastTickTime < burnTickInterval) return;

        Health health = other.GetComponent<Health>();
        if (health == null) return;

        health.TakeDamage(burnDamage);
        lastTickTime = Time.time;
    }
}
