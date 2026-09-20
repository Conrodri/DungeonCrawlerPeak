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
    // Flat reduction on every hit - 0 by default. Currently only set by DungeonGenerator on a boss
    // rolling the Blinde modifier (see BossModifier). Never lets a hit through for less than 1
    // damage, so a heavily-armored boss can't stall a fight out entirely. Not applied on the
    // player's own hits below - PlayerLimbs armor is the player-side equivalent, and nothing sets
    // this field on the player today.
    public int flatDamageReduction;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnDodged;
    // Fired once a hit actually connects (post-dodge/invulnerable, whether the target is the
    // player or a plain-pool enemy) - ONLY when the caller supplies where the hit came from (see
    // TakeDamage/TakeDamageFromEnemy's fromPosition). Lets PlayerController/EnemyController react
    // with a knockback/stagger without Health needing to know either of those types exist.
    public event Action<Vector2> OnDamagedFrom;

    bool isDead;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    // NOT cached in Awake on purpose - a real bug caught via eval on the live Editor: Health is
    // listed BEFORE PlayerLimbs in DungeonGenerator's player GameObject constructor, and Unity
    // calls Awake() synchronously per-AddComponent as each type in that list is attached, so a
    // GetComponent<PlayerLimbs>() from inside Health.Awake() ran before PlayerLimbs existed on the
    // object yet and silently cached null forever. A plain on-demand GetComponent per hit/heal
    // (never a hot path) sidesteps the whole class of ordering bugs. ILimbs (2026-09-20) covers
    // both PlayerLimbs (player, armor-aware) and EnemyLimbs (monsters, see RoomController.
    // SpawnEnemies) - a plain Health with neither (bosses, destructibles, the tutorial's own
    // simplified player) falls through to the flat pool math below exactly as before.
    ILimbs Limbs => GetComponent<ILimbs>();

    public void SetInvulnerable(bool value)
    {
        IsInvulnerable = value;
    }

    // Guaranteed lethal hit that bypasses invulnerability and dodge - for unavoidable hazards like
    // a collapsing floor, where no defensive stat should be able to save the player.
    public void Kill()
    {
        if (isDead) return;
        ILimbs limbs = Limbs;
        if (limbs != null)
        {
            limbs.KillAll(); // zeroes every limb - syncs this Health's totals + fires OnDeath itself
            return;
        }
        currentHealth = 0;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        isDead = true;
        OnDeath?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || isDead) return;
        ILimbs limbs = Limbs;
        if (limbs != null)
        {
            // Distributes the heal across non-broken limbs and resyncs this Health's totals (see
            // PlayerLimbs/EnemyLimbs.HealNonBroken/SyncHealth) - this IS the full heal, not a
            // pre-step before the plain pool math below.
            limbs.HealNonBroken(amount);
            return;
        }
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // Entry point for a directed enemy/boss attack (contact or projectile), letting a specific
    // AttackSource bias which BodyPart gets targeted (see PlayerLimbs.RollTarget). A hazard that
    // has its own defined zone (e.g. FloorTrap's BearTrap/CollapsingCeiling) passes it to
    // TakeDamage's own optional parameter instead of using this overload, which is for mob attacks.
    // Returns whether the hit actually connected (false if dodged/invulnerable/already dead) - see
    // BossController.TryContactDamage, which must not award Vampirique lifesteal on a hit that
    // never landed.
    public bool TakeDamageFromEnemy(int amount, AttackSource source, Vector2? fromPosition = null) => ApplyDamage(amount, source, fromPosition);

    public bool TakeDamage(int amount, AttackSource source = AttackSource.Random, Vector2? fromPosition = null) => ApplyDamage(amount, source, fromPosition);

    bool ApplyDamage(int amount, AttackSource source, Vector2? fromPosition = null)
    {
        if (amount <= 0 || isDead || IsInvulnerable) return false;

        if (flatDamageReduction > 0) amount = Mathf.Max(1, amount - flatDamageReduction);

        if (dodgeChance > 0f && UnityEngine.Random.value < dodgeChance)
        {
            OnDodged?.Invoke();
            return false;
        }

        if (fromPosition.HasValue) OnDamagedFrom?.Invoke(fromPosition.Value);

        ILimbs limbs = Limbs;
        if (limbs != null)
        {
            // Applies the rolled limb's own damage (+ armor mitigation for the player, see
            // PlayerLimbs.MitigateHit) and resyncs this Health's totals in one call (also fires
            // OnDeath once currentHealth reaches 0) - this IS the full damage application whenever
            // an ILimbs is present (player or monster); a plain Health with neither (bosses,
            // destructibles) falls through to the flat pool math below exactly as before.
            limbs.MitigateHit(source, amount);
            return true;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        Debug.Log(name + " took " + amount + " damage (" + currentHealth + "/" + maxHealth + ")");
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth == 0)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
        return true;
    }

    // Called by PlayerLimbs after any per-limb HP change (hit, heal, repair, Constitution change) -
    // keeps this Health's pool truthfully in sync with the real source of truth (the sum of all
    // limb pools) so every other system (HUD, death screen, save) can keep reading plain
    // currentHealth/maxHealth/OnHealthChanged without knowing PlayerLimbs exists. Bypasses dodge/
    // invulnerable/flatDamageReduction on purpose - those already applied once, per-hit, before
    // PlayerLimbs ever changed; this is a totals resync, not a new damage/heal event of its own.
    public void SetFromLimbs(int current, int max)
    {
        maxHealth = max;
        currentHealth = Mathf.Clamp(current, 0, max);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth == 0 && !isDead)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }
}
