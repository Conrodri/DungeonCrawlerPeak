using UnityEngine;

// A patch of ground fire left by the Ligne de Feu spell (2026-09-19 request: "une trainee de feu
// pour 3 secondes... infligera le debuff brulure pour 5 secondes, se refresh tant que la cible
// est dans le feu"). Deliberately its own simple hazard rather than reusing FuelPuddle - that one
// damages everyone standing in it (an environmental hazard, player included) and has no lifetime
// of its own (stays lit until an explosion sets it off); this only ever burns enemies (the
// caster's own spell) and expires on a fixed timer.
public class FireTrail : MonoBehaviour
{
    public float lifetime = 3f;
    public float burnDuration = 5f;
    public int burnDamagePerTick = 2;
    public float burnTickInterval = 1f;
    public Sprite burnIcon;
    public string ignoreTag = "Player";

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!string.IsNullOrEmpty(ignoreTag) && other.CompareTag(ignoreTag)) return;
        if (other.GetComponent<Health>() == null) return;

        BurnStatus.Apply(other.gameObject, burnDuration, burnDamagePerTick, burnTickInterval, burnIcon);
    }
}
