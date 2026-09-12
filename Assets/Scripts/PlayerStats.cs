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
}
