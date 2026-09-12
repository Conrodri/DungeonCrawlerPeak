using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public int maxHealth = 3;
    public int currentHealth;
    // Chance to fully negate an incoming hit before it's applied - 0 by default, so this has no
    // effect on enemies; only the player's PlayerStats currently drives it, via Dexterity.
    public float dodgeChance;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnDodged;

    bool isDead;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || isDead) return;

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
