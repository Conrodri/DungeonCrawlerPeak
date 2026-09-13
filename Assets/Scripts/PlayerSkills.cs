using System;
using UnityEngine;

public enum SkillType { Sprint, Melee, Ranged, Roll }

// "Plus tu utilises quelque chose, plus il monte en niveau" - each skill levels up purely from
// being used (see PlayerController's AddUsage calls at each action's own point of use), capped at
// MaxLevel, and grants an escalating bonus package: a flat per-level bonus, plus two bigger unlocks
// at level 5 and level 10 (mirrors the example given verbatim for Sprint - the other 3 skills
// follow the same shape since none of their specifics were dictated).
public class PlayerSkills : MonoBehaviour
{
    public const int MaxLevel = 10;
    static readonly int SkillCount = Enum.GetValues(typeof(SkillType)).Length;

    readonly int[] levels = new int[SkillCount];
    readonly float[] progress = new float[SkillCount];

    public event Action<SkillType, int> OnSkillLeveledUp;

    public int GetLevel(SkillType type) => levels[(int)type];

    // Usage points needed to go from `level` to `level + 1` - a flat ramp (10, 20, 30, ...) so
    // later levels take longer, same shape as PlayerStats' XP curve.
    static float ThresholdFor(int level) => 10f * (level + 1);

    public void AddUsage(SkillType type, float amount)
    {
        if (amount <= 0f) return;
        int i = (int)type;
        if (levels[i] >= MaxLevel) return;

        progress[i] += amount;
        while (levels[i] < MaxLevel && progress[i] >= ThresholdFor(levels[i]))
        {
            progress[i] -= ThresholdFor(levels[i]);
            levels[i]++;
            OnSkillLeveledUp?.Invoke(type, levels[i]);
        }
    }

    // --- Sprint: +5%/level move speed while sprinting; level 5 cuts its stamina cost; level 10
    // lets the player attack while sprinting (currently blocked outright - see PlayerController). ---
    public float SprintSpeedBonus => GetLevel(SkillType.Sprint) * 0.05f;
    public float SprintStaminaCostMultiplier => GetLevel(SkillType.Sprint) >= 5 ? 0.7f : 1f;
    public bool CanAttackWhileSprinting => GetLevel(SkillType.Sprint) >= 10;

    // --- Melee: +5%/level damage; level 5 attacks faster; level 10 adds a flat crit chance. ---
    public float MeleeDamageBonus => GetLevel(SkillType.Melee) * 0.05f;
    public float MeleeCooldownMultiplier => GetLevel(SkillType.Melee) >= 5 ? 0.8f : 1f;
    public float MeleeCritChance => GetLevel(SkillType.Melee) >= 10 ? 0.2f : 0f;

    // --- Ranged (staff + every thrown item, bombs included): +5%/level damage; level 5 speeds up
    // projectiles; level 10 extends their range. ---
    public float RangedDamageBonus => GetLevel(SkillType.Ranged) * 0.05f;
    public float RangedSpeedMultiplier => GetLevel(SkillType.Ranged) >= 5 ? 1.25f : 1f;
    public float RangedRangeMultiplier => GetLevel(SkillType.Ranged) >= 10 ? 1.5f : 1f;

    // --- Roll: +5%/level roll speed; level 5 cuts its stamina cost; level 10 cuts its cooldown. ---
    public float RollSpeedBonus => GetLevel(SkillType.Roll) * 0.05f;
    public float RollStaminaCostMultiplier => GetLevel(SkillType.Roll) >= 5 ? 0.7f : 1f;
    public float RollCooldownMultiplier => GetLevel(SkillType.Roll) >= 10 ? 0.6f : 1f;
}
