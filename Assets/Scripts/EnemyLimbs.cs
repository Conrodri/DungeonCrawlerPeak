using System;
using System.Collections.Generic;
using UnityEngine;

// The monster-side counterpart to PlayerLimbs (2026-09-20 request: "chaque monstre a le meme
// systeme de membre que le joueur"). HP is split across BodyParts using the exact same relative
// weights as the player's own body (PlayerLimbs.BaseMaxFor), renormalized over whichever parts
// this creature's EnemyLimbLayout gives it and scaled to its own total HP. No armor/equipment
// (only the player wears gear) and no weighted hit-zone bias by AttackSource (unlike PlayerLimbs.
// RollTarget - every incoming player attack is the same "kind" of hit, there's nothing to bias
// toward) - every hit rolls uniformly across this creature's own layout instead.
//
// Kept as its own small component rather than folded into PlayerLimbs: that component's
// dictionaries are populated via a field initializer specifically to dodge a real Awake-ordering
// bug (see its own comments), and it carries armor/equipment concerns no monster has - grafting a
// second, differently-shaped use case onto it risked that bug class for no real reuse win. Both
// implement ILimbs so Health.cs doesn't need to know which one it's talking to.
[RequireComponent(typeof(Health))]
public class EnemyLimbs : MonoBehaviour, ILimbs
{
    BodyPart[] layout = { BodyPart.Torso };
    readonly Dictionary<BodyPart, int> limbHealth = new Dictionary<BodyPart, int>();
    readonly Dictionary<BodyPart, int> limbMaxHealth = new Dictionary<BodyPart, int>();

    public event Action<BodyPart, int> OnHit;
    public event Action OnLimbsChanged;

    // Called once by RoomController.SpawnEnemies right after this enemy's flat Health.maxHealth is
    // finalized (elite HpUp modifier already applied) - splits that same total across `parts`.
    public void Configure(BodyPart[] parts, int totalHealth)
    {
        layout = parts != null && parts.Length > 0 ? parts : new[] { BodyPart.Torso };
        limbHealth.Clear();
        limbMaxHealth.Clear();

        int weightSum = 0;
        foreach (BodyPart part in layout) weightSum += PlayerLimbs.BaseMaxFor(part);

        int assigned = 0;
        for (int i = 0; i < layout.Length; i++)
        {
            BodyPart part = layout[i];
            int hp;
            if (i == layout.Length - 1)
            {
                hp = Mathf.Max(1, totalHealth - assigned); // last part soaks the rounding remainder
            }
            else
            {
                hp = Mathf.Max(1, Mathf.RoundToInt(totalHealth * (float)PlayerLimbs.BaseMaxFor(part) / weightSum));
                assigned += hp;
            }
            limbMaxHealth[part] = hp;
            limbHealth[part] = hp;
        }
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    public int GetMaxLimbHealth(BodyPart part) => limbMaxHealth.TryGetValue(part, out int max) ? max : 0;
    public int GetLimbHealth(BodyPart part) => limbHealth.TryGetValue(part, out int hp) ? hp : 0;

    public int TotalMaxHealth { get { int total = 0; foreach (int v in limbMaxHealth.Values) total += v; return total; } }
    public int TotalCurrentHealth { get { int total = 0; foreach (int v in limbHealth.Values) total += v; return total; } }

    // Whether this creature's own layout even has this part at all (a Larve has no ArmLeft to
    // break) - GetState below would otherwise read a part it never had as permanently Broken.
    public bool HasPart(BodyPart part) => limbMaxHealth.ContainsKey(part);
    public bool IsPartBroken(BodyPart part) => HasPart(part) && GetLimbHealth(part) <= 0;

    // 2026-09-20 follow-up ("point 2": give broken monster limbs a real effect, not just cosmetic
    // HP) - see EnemyController for how these feed movement/damage. Mirrors the player's own
    // leg->speed / arm(weapon hand)->damage split (PlayerLimbs' class comment) rather than
    // inventing a new mapping.
    public bool AnyLegBroken => IsPartBroken(BodyPart.LegLeft) || IsPartBroken(BodyPart.LegRight);
    public bool AnyArmBroken => IsPartBroken(BodyPart.ArmLeft) || IsPartBroken(BodyPart.ArmRight);

    public LimbState GetState(BodyPart part)
    {
        int hp = GetLimbHealth(part);
        int max = GetMaxLimbHealth(part);
        return hp <= 0 ? LimbState.Broken : hp < max ? LimbState.Damaged : LimbState.Healthy;
    }

    // `source` unused on purpose - see class comment.
    public int MitigateHit(AttackSource source, int amount)
    {
        BodyPart part = layout[UnityEngine.Random.Range(0, layout.Length)];
        limbHealth[part] = Mathf.Max(0, GetLimbHealth(part) - amount);
        OnHit?.Invoke(part, amount);
        SyncHealth();
        OnLimbsChanged?.Invoke();
        return amount;
    }

    public void HealNonBroken(int amount)
    {
        if (amount <= 0) return;
        for (int i = 0; i < amount; i++)
        {
            BodyPart? best = null;
            int bestDeficit = 0;
            foreach (BodyPart part in layout)
            {
                int hp = GetLimbHealth(part);
                if (hp <= 0) continue;
                int deficit = GetMaxLimbHealth(part) - hp;
                if (deficit > bestDeficit) { bestDeficit = deficit; best = part; }
            }
            if (best == null) break;
            limbHealth[best.Value] = GetLimbHealth(best.Value) + 1;
        }
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    public void KillAll()
    {
        foreach (BodyPart part in layout) limbHealth[part] = 0;
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    void SyncHealth()
    {
        Health health = GetComponent<Health>();
        if (health != null) health.SetFromLimbs(TotalCurrentHealth, TotalMaxHealth);
    }
}
