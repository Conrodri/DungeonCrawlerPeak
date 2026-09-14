using System;
using UnityEngine;

// Generic destructible decor (stone blocks today; future materials - barrels, debris - reuse this
// same component with their own maxHealth/requiredForce).
public class DestructibleObject : MonoBehaviour
{
    public int maxHealth = 3;
    public int currentHealth;
    // Minimum attacker Force needed for a hit to register at all - 0 means any attack works.
    // Explosives bypass this (see Bomb.attackerForce's default), representing "a tool built for it".
    public int requiredForce;
    // Dropped unconditionally on destruction, on top of the chance-based LootTable roll - the
    // crafting material a block/debris material yields (e.g. wood/metal/stone). Empty = none.
    public string guaranteedDropItemId;

    public event Action OnDestroyed;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    // Returns whether the hit actually applied - false if the attacker didn't meet requiredForce.
    public bool TryDamage(int amount, int attackerForce)
    {
        if (attackerForce < requiredForce || amount <= 0) return false;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (currentHealth == 0)
        {
            // Parented like this decor piece itself (under DungeonRoot) - see LootTable.TryDropLoot's
            // own comment for why an unparented pickup would otherwise leak across floors.
            if (!string.IsNullOrEmpty(guaranteedDropItemId)) ItemPickup.SpawnAt(transform.position, guaranteedDropItemId, 1).transform.SetParent(transform.parent);
            LootTable.TryDropLoot(transform.position, transform.parent);
            OnDestroyed?.Invoke();
            Destroy(gameObject);
        }
        return true;
    }
}
