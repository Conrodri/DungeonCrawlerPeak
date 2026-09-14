using System;
using UnityEngine;

// D&D-style stat block. Level starts at 1; each raw stat drives exactly one gameplay multiplier
// below. No stat-allocation-on-level-up flow yet (level only feeds ProficiencyBonus so far) - XP
// is earned from kills (see EnemyController/BossRoomController) purely to make level go up and be
// visible (ExperienceBarUI); a dialogue outcome can still change raw stats directly regardless.
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Stamina))]
public class PlayerStats : MonoBehaviour
{
    public int level = 1;
    public int experience;
    public int experienceToNextLevel = ExperienceBase;
    const int ExperienceBase = 10;
    const int ExperiencePerLevel = 5;
    // Granted per level-up (see AddExperience), spent one at a time on any stat in a Safe room -
    // see AttributeAllocationUI, opened from the Tavernier NPC.
    public int unspentAttributePoints;
    const int AttributePointsPerLevel = 2;

    // Total XP needed to go from level 1 to `level`, using the same curve as AddExperience/
    // experienceToNextLevel above. Used by DungeonGenerator to size a Region boss's XP reward so
    // it exactly bridges a floor's "full clear" breakpoint level - see RegionBossXpFor and
    // project_xp_monster_leveling_backlog memory for the 5->6 (floor 1), 10->11 (floor 2) spec.
    public static int CumulativeXpForLevel(int level)
    {
        int total = 0;
        for (int lv = 1; lv < level; lv++) total += ExperienceBase + (lv - 1) * ExperiencePerLevel;
        return total;
    }

    public event Action<int, int, int> OnExperienceChanged; // (experience, experienceToNextLevel, level)

    public int force = 1;
    public int dexterite = 1;
    public int intelligence = 1;
    public int vitesse = 1;
    public int constitution = 1;
    public int portee = 1;
    public int charisme = 1;
    public int endurance = 1;

    const int VitesseCap = 25;
    const float BaseStamina = 60f;
    const float StaminaPerEndurance = 8f;
    const float BaseStaminaRegen = 15f;
    const float StaminaRegenPerEndurance = 1.5f;

    public float PhysicalDamageMultiplier => 1f + force * 0.01f;
    public float MagicDamageMultiplier => 1f + intelligence * 0.01f;
    public float AttackSpeedMultiplier => 1f + dexterite * 0.01f;
    public float DodgeChance => dexterite * 0.01f;
    public float MoveSpeedMultiplier => 1f + Mathf.Min(vitesse, VitesseCap) * 0.02f;
    public float RangeMultiplier => 1f + portee * 0.02f;
    // Not consumed yet - no purchase flow exists in Shop rooms (marker-only so far).
    public float ShopPriceMultiplier => Mathf.Max(0f, 1f - charisme * 0.02f);

    // Official 5e proficiency bonus table - the piece that scales a dialogue check's total with
    // level, instead of a stat floor or a bigger die.
    public int ProficiencyBonus => level >= 17 ? 6 : level >= 13 ? 5 : level >= 9 ? 4 : level >= 5 ? 3 : 2;

    Health health;
    Stamina stamina;

    void Awake()
    {
        health = GetComponent<Health>();
        health.maxHealth += constitution; // 1 point = 1 half-heart unit, same scale as Health itself
        health.currentHealth = health.maxHealth;

        stamina = GetComponent<Stamina>();
        stamina.maxStamina = BaseStamina + endurance * StaminaPerEndurance;
        stamina.regenPerSecond = BaseStaminaRegen + endurance * StaminaRegenPerEndurance;
        stamina.currentStamina = stamina.maxStamina;
    }

    void Update()
    {
        health.dodgeChance = DodgeChance; // stays correct if a future feature changes dexterite
    }

    public int GetStat(StatType type)
    {
        switch (type)
        {
            case StatType.Force: return force;
            case StatType.Dexterite: return dexterite;
            case StatType.Intelligence: return intelligence;
            case StatType.Vitesse: return vitesse;
            case StatType.Constitution: return constitution;
            case StatType.Portee: return portee;
            case StatType.Charisme: return charisme;
            case StatType.Endurance: return endurance;
            default: return 0;
        }
    }

    // Never drops a stat below 0. Constitution specifically also shrinks max HP by the same
    // amount, so a Constitution penalty behaves like the D&D convention of CON affecting HP.
    public void ApplyPenalty(StatType type, int amount)
    {
        if (amount <= 0) return;
        switch (type)
        {
            case StatType.Force: force = Mathf.Max(0, force - amount); break;
            case StatType.Dexterite: dexterite = Mathf.Max(0, dexterite - amount); break;
            case StatType.Intelligence: intelligence = Mathf.Max(0, intelligence - amount); break;
            case StatType.Vitesse: vitesse = Mathf.Max(0, vitesse - amount); break;
            case StatType.Constitution:
                int actualLoss = Mathf.Min(constitution, amount);
                constitution -= actualLoss;
                health.maxHealth = Mathf.Max(1, health.maxHealth - actualLoss);
                health.currentHealth = Mathf.Min(health.currentHealth, health.maxHealth);
                break;
            case StatType.Portee: portee = Mathf.Max(0, portee - amount); break;
            case StatType.Charisme: charisme = Mathf.Max(0, charisme - amount); break;
            case StatType.Endurance:
                int enduranceLoss = Mathf.Min(endurance, amount);
                endurance -= enduranceLoss;
                stamina.maxStamina = Mathf.Max(10f, stamina.maxStamina - enduranceLoss * StaminaPerEndurance);
                stamina.regenPerSecond = Mathf.Max(1f, stamina.regenPerSecond - enduranceLoss * StaminaRegenPerEndurance);
                stamina.currentStamina = Mathf.Min(stamina.currentStamina, stamina.maxStamina);
                break;
        }
    }

    // Symmetric counterpart to ApplyPenalty (adds instead of subtracts, same per-stat side effects
    // for Constitution/Endurance) - used to grant/revoke an equipped ring's +1 (see
    // PlayerEquipment.ApplyItemEffects/RemoveItemEffects) without duplicating the Constitution/
    // Endurance -> max HP/Stamina wiring a third time.
    public void ApplyBonus(StatType type, int amount)
    {
        if (amount <= 0) return;
        switch (type)
        {
            case StatType.Force: force += amount; break;
            case StatType.Dexterite: dexterite += amount; break;
            case StatType.Intelligence: intelligence += amount; break;
            case StatType.Vitesse: vitesse += amount; break;
            case StatType.Constitution:
                constitution += amount;
                health.maxHealth += amount;
                health.currentHealth += amount;
                break;
            case StatType.Portee: portee += amount; break;
            case StatType.Charisme: charisme += amount; break;
            case StatType.Endurance:
                endurance += amount;
                stamina.maxStamina += amount * StaminaPerEndurance;
                stamina.regenPerSecond += amount * StaminaRegenPerEndurance;
                stamina.currentStamina += amount * StaminaPerEndurance;
                break;
        }
    }

    // Called on every enemy/boss kill (see EnemyController.HandleDeath/BossRoomController.
    // HandleBossDied). Loops rather than a single add in case one big reward (a boss) clears
    // several levels at once.
    public void AddExperience(int amount)
    {
        if (amount <= 0) return;

        experience += amount;
        while (experience >= experienceToNextLevel)
        {
            experience -= experienceToNextLevel;
            level++;
            experienceToNextLevel = ExperienceBase + (level - 1) * ExperiencePerLevel;
            unspentAttributePoints += AttributePointsPerLevel;
        }
        OnExperienceChanged?.Invoke(experience, experienceToNextLevel, level);
    }

    public void ApplyCurse()
    {
        ApplyPenalty(StatType.Force, 1);
        ApplyPenalty(StatType.Dexterite, 1);
        ApplyPenalty(StatType.Intelligence, 1);
        ApplyPenalty(StatType.Vitesse, 1);
        ApplyPenalty(StatType.Constitution, 1);
        ApplyPenalty(StatType.Portee, 1);
        ApplyPenalty(StatType.Charisme, 1);
        ApplyPenalty(StatType.Endurance, 1);
    }
}
