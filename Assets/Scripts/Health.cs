using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public int maxHealth = 3;
    public int currentHealth;
    // Chance to fully negate an incoming hit before it's applied - 0 by default, so this has no
    // effect on enemies; only the player's PlayerStats currently drives it, via Dexterity.
    public float dodgeChance;
    // Set/cleared by PlayerController's dodge roll (see rollDuration) - a fully separate mechanic
    // from dodgeChance's Dexterity-based coin flip, this always blocks the hit while active.
    public bool IsInvulnerable { get; private set; }

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnDodged;

    bool isDead;
    // Null for enemies/destructibles - only the player carries a PlayerLimbs. Cached once instead
    // of a GetComponent call on every hit.
    PlayerLimbs limbs;

    void Awake()
    {
        currentHealth = maxHealth;
        limbs = GetComponent<PlayerLimbs>();
    }

    public void SetInvulnerable(bool value)
    {
        IsInvulnerable = value;
    }

    // Guaranteed lethal hit that bypasses invulnerability and dodge - for unavoidable hazards like
    // a collapsing floor, where no defensive stat should be able to save the player.
    public void Kill()
    {
        if (isDead) return;
        currentHealth = 0;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        isDead = true;
        OnDeath?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // Entry point for a directed enemy/boss attack (contact or projectile) as opposed to an
    // environmental hazard (bomb, fuel puddle, floor trap, hole) - routes through PlayerLimbs for
    // hit-location targeting + armor mitigation when this Health belongs to the player, otherwise
    // behaves exactly like TakeDamage. attackerType is null for bosses/enemy projectiles, which
    // have no EnemyType and so roll a fully random body part (see PlayerLimbs.RollTarget).
    public void TakeDamageFromEnemy(int amount, EnemyType? attackerType)
    {
        if (limbs != null) amount = limbs.MitigateHit(attackerType, amount);
        TakeDamage(amount);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || isDead || IsInvulnerable) return;

        if (dodgeChance > 0f && UnityEngine.Random.value < dodgeChance)
        {
            OnDodged?.Invoke();
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        Debug.Log(name + " took " + amount + " damage (" + currentHealth + "/" + maxHealth + ")");
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth == 0)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }
}
