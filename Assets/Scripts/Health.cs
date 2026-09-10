using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public int maxHealth = 3;
    public int currentHealth;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;

    bool isDead;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || isDead) return;

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
