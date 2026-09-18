using UnityEngine;

// Generic damage-over-time debuff any Health-bearing target can carry (2026-09-19 request: the
// Ligne de Feu spell "infligera le debuff brulure pour 5 secondes... se refresh tant que la
// cible est dans le feu"). Distinct from PlayerEquipment's own clothing-catches-fire mechanic,
// which only ever affects the player's own gear - this is a plain Health-tick debuff usable on
// any enemy. Self-installing: Apply() adds one if missing, or just refreshes the timer if not.
public class BurnStatus : MonoBehaviour
{
    public float tickInterval = 1f;
    public int damagePerTick = 2;
    public float duration = 5f;

    Health health;
    StatusIconDisplay statusIcons;
    float endTime;
    float lastTickTime;
    const string BurnIconKey = "Burning";

    public static void Apply(GameObject target, float duration, int damagePerTick, float tickInterval, Sprite icon)
    {
        BurnStatus burn = target.GetComponent<BurnStatus>();
        if (burn == null) burn = target.AddComponent<BurnStatus>();
        burn.duration = duration;
        burn.damagePerTick = damagePerTick;
        burn.tickInterval = tickInterval;
        burn.Refresh(icon);
    }

    void Awake()
    {
        health = GetComponent<Health>();
        statusIcons = GetComponent<StatusIconDisplay>();
    }

    void Refresh(Sprite icon)
    {
        endTime = Time.time + duration;
        // Doesn't reset lastTickTime to "now minus interval" - re-igniting an already-burning
        // target shouldn't force an extra tick on top of whatever's already ticking on schedule.
        if (statusIcons != null) statusIcons.ShowIcon(BurnIconKey, icon, duration);
    }

    void Update()
    {
        if (Time.time >= endTime)
        {
            Destroy(this);
            return;
        }

        if (Time.time - lastTickTime < tickInterval) return;
        lastTickTime = Time.time;
        if (health != null) health.TakeDamage(damagePerTick);
    }
}
