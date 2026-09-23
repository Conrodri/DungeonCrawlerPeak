using UnityEngine;

// Generic damage-over-time debuff any Health-bearing target can carry - mirrors BurnStatus.cs
// exactly (same shape: self-installing, refreshes on re-apply instead of stacking). 2026-09-23
// request: "retire les degats de contact... ou des degats de contact si le monstre est enflamme
// ou veneneux" - EnemyController.TryDamage/BossController.TryContactDamage check for this (and
// BurnStatus) to keep touching a hazardous monster's body dangerous even with idle contact damage
// otherwise removed. Nothing applies this to a monster yet (no poisonous-body mechanic exists
// today, only the ground-hazard SlowPuddle) - it's the same symmetric twin BurnStatus already is,
// ready for whatever eventually grants it (a poisonous species, a player debuff spell, etc).
public class PoisonStatus : MonoBehaviour
{
    public float tickInterval = 1f;
    public int damagePerTick = 2;
    public float duration = 5f;

    Health health;
    StatusIconDisplay statusIcons;
    float endTime;
    float lastTickTime;
    const string PoisonIconKey = "Poisoned";

    public static void Apply(GameObject target, float duration, int damagePerTick, float tickInterval, Sprite icon)
    {
        PoisonStatus poison = target.GetComponent<PoisonStatus>();
        if (poison == null) poison = target.AddComponent<PoisonStatus>();
        poison.duration = duration;
        poison.damagePerTick = damagePerTick;
        poison.tickInterval = tickInterval;
        poison.Refresh(icon);
    }

    void Awake()
    {
        health = GetComponent<Health>();
        statusIcons = GetComponent<StatusIconDisplay>();
    }

    void Refresh(Sprite icon)
    {
        endTime = Time.time + duration;
        if (statusIcons != null) statusIcons.ShowIcon(PoisonIconKey, icon, duration);
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
