using UnityEngine;

// D&D-style stat block. Level starts at 1; each raw stat drives exactly one gameplay multiplier
// below. Nothing here changes these values over time yet - no XP/level-up flow, no stat allocation
// UI. That arrives with the NPC dialogue/dice-roll feature, which will grant or penalize stats.
[RequireComponent(typeof(Health))]
public class PlayerStats : MonoBehaviour
{
    public int level = 1;
    public int force = 1;
    public int dexterite = 1;
    public int intelligence = 1;
    public int vitesse = 1;
    public int constitution = 1;
    public int portee = 1;
    public int charisme = 1;

    const int VitesseCap = 25;

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

    void Awake()
    {
        health = GetComponent<Health>();
        health.maxHealth += constitution; // 1 point = 1 half-heart unit, same scale as Health itself
        health.currentHealth = health.maxHealth;
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
        }
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
    }
}
