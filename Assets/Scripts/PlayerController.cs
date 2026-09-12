using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerController : MonoBehaviour
{
    public enum WeaponType { Fist, Sword, Staff }

    public float moveSpeed = 5f;

    [Header("Sprint")]
    public float sprintSpeedMultiplier = 1.6f;
    public float sprintStaminaCostPerSecond = 25f;

    [Header("Dodge Roll")]
    // Also the invulnerability window, as requested - the roll IS the i-frame, not a separate timer.
    public float rollDuration = 0.2f;
    public float rollSpeed = 12f;
    // Not requested explicitly, but every other action in this class has a cooldown/cost of its
    // own (attacks, throws, sprint) - a totally free roll would trivialize damage avoidance and
    // waste the Endurance stat just built. Easy to zero out if unwanted.
    public float rollCooldown = 0.5f;
    public float rollStaminaCost = 15f;

    [Header("Weapon")]
    public WeaponType currentWeapon = WeaponType.Fist;
    public Sprite projectileSprite;
    public Sprite fistVisualSprite;
    public Sprite swordVisualSprite;

    [Header("Fist")]
    public int fistDamage = 1;
    public float fistRange = 1.0f;
    public float fistOffset = 0.8f;
    public float fistCooldown = 0.25f;

    [Header("Sword")]
    public int swordDamage = 2;
    public float swordRange = 0.7f;
    public float swordOffset = 0.9f;
    public float swordCooldown = 0.4f;

    [Header("Staff")]
    public int staffDamage = 1;
    public float staffCooldown = 0.5f;
    public float projectileSpeed = 8f;
    // The staff isn't hitscan/infinite range: it reaches three sword-lengths out.
    public float staffRangeMultiplier = 3f;

    [Header("Throwables")]
    public int throwDamage = 1;
    public float throwSpeed = 10f;
    public float throwCooldown = 0.3f;
    public float throwRangeMultiplier = 2f;

    [Header("Bomb")]
    public Sprite explosionSprite;
    public int bombDamage = 3;
    public float bombExplosionRadius = 2f;
    public float bombFuseTime = 1.2f;
    public float bombSpeed = 6f;

    // A weapon's total reach: how far from the player its hit area extends.
    float SwordReach => (swordOffset + swordRange) * stats.RangeMultiplier;
    float StaffMaxRange => SwordReach * staffRangeMultiplier;
    float ThrowMaxRange => SwordReach * throwRangeMultiplier;

    Rigidbody2D rb;
    Health health;
    Stamina stamina;
    PlayerInventory inventory;
    PlayerStats stats;
    CircleCollider2D bodyCollider;
    Vector2 moveInput;
    Vector2 aimDirection = Vector2.down;
    float lastAttackTime = -999f;
    float lastThrowTime = -999f;
    bool isDead;
    bool isSprinting;
    bool isRolling;
    float rollEndTime = -999f;
    float lastRollTime = -999f;
    Vector2 rollDirection;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // Same tunneling guard as EnemyController: without it, a player moving against a tilemap
        // CompositeCollider2D can occasionally skip clean through a thin wall in a single physics
        // step instead of colliding with it.
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        inventory = GetComponent<PlayerInventory>();
        stats = GetComponent<PlayerStats>();
        bodyCollider = GetComponent<CircleCollider2D>();
        health.OnDeath += HandleDeath;
    }

    void Update()
    {
        if (isDead)
        {
            moveInput = Vector2.zero;
            isSprinting = false;
            return;
        }

        // Committed to the roll until it finishes - FixedUpdate drives the movement and ends it on
        // its own timer, so this stays correct even if a dialogue/inventory screen opens mid-roll.
        if (isRolling) return;

        if (DialogueManager.IsOpen || InventoryUI.IsOpen)
        {
            moveInput = Vector2.zero;
            isSprinting = false;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            moveInput = Vector2.zero;
            isSprinting = false;
            return;
        }

        // Movement: WASD only (ZQSD on an AZERTY layout maps to the same physical keys).
        float x = 0f;
        float y = 0f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.aKey.isPressed) x -= 1f;
        if (kb.wKey.isPressed) y += 1f;
        if (kb.sKey.isPressed) y -= 1f;
        moveInput = new Vector2(x, y).normalized;

        // Sprint: held Shift while actually moving, gated on stamina - runs out mid-sprint and it
        // cuts off on its own instead of going negative.
        isSprinting = kb.leftShiftKey.isPressed && moveInput != Vector2.zero && stamina.currentStamina > 0f;
        if (isSprinting) stamina.Drain(sprintStaminaCostPerSecond * Time.deltaTime);

        if (kb.spaceKey.wasPressedThisFrame) TryRoll();
        if (isRolling) return; // just started rolling this frame - no attack/hotbar until it ends

        // Aiming: still tracked while sprinting (so the facing is already correct the instant
        // sprint stops), but attacking/hotbar are not - can't fight with your weapon out while
        // running, same as the roll's commitment above.
        Vector2 aim = Vector2.zero;
        if (kb.upArrowKey.isPressed) aim = Vector2.up;
        else if (kb.downArrowKey.isPressed) aim = Vector2.down;
        else if (kb.leftArrowKey.isPressed) aim = Vector2.left;
        else if (kb.rightArrowKey.isPressed) aim = Vector2.right;

        if (aim != Vector2.zero) aimDirection = aim;

        if (isSprinting) return;

        if (aim != Vector2.zero) TryAttack();

        // Hotbar: reads whatever the player actually assigned to each slot (drag & drop, feature
        // 2) instead of assuming the starting loadout.
        if (kb.digit1Key.wasPressedThisFrame) UseHotbarSlot(0);
        if (kb.digit2Key.wasPressedThisFrame) UseHotbarSlot(1);
        if (kb.digit3Key.wasPressedThisFrame) UseHotbarSlot(2);
        if (kb.digit4Key.wasPressedThisFrame) UseHotbarSlot(3);
    }

    void FixedUpdate()
    {
        // Drives the roll itself (and ends it) here rather than in Update, so it still finishes
        // and clears invulnerability even if Update starts bailing out early mid-roll (e.g. a
        // dialogue opens, or the player dies from something invulnerability doesn't block).
        if (isRolling)
        {
            rb.linearVelocity = isDead ? Vector2.zero : rollDirection * rollSpeed;
            if (Time.time >= rollEndTime) EndRoll();
            return;
        }

        float speedMultiplier = stats.MoveSpeedMultiplier * (isSprinting ? sprintSpeedMultiplier : 1f);
        rb.linearVelocity = isDead ? Vector2.zero : moveInput * moveSpeed * speedMultiplier;
    }

    void TryRoll()
    {
        if (Time.time - lastRollTime < rollCooldown) return;
        if (stamina.currentStamina < rollStaminaCost) return;

        // Rolls in the direction the player is currently moving; with no movement input, rolls
        // toward the last direction they aimed/faced (aimDirection is never Vector2.zero).
        rollDirection = moveInput != Vector2.zero ? moveInput : aimDirection;

        stamina.Drain(rollStaminaCost);
        lastRollTime = Time.time;
        rollEndTime = Time.time + rollDuration;
        isRolling = true;
        health.SetInvulnerable(true);
    }

    void EndRoll()
    {
        isRolling = false;
        health.SetInvulnerable(false);
    }

    bool weaponLocked;
    public bool WeaponLocked => weaponLocked;

    public void EquipWeapon(WeaponType weapon)
    {
        if (weaponLocked) return;
        currentWeapon = weapon;
        Debug.Log("Equipped " + weapon);
    }

    // A cursed weapon-item forces itself on and can't be swapped out until UnlockWeapon runs.
    public void ForceEquipWeapon(WeaponType weapon)
    {
        currentWeapon = weapon;
        weaponLocked = true;
        Debug.Log("Cursed weapon forced on: " + weapon);
    }

    public void UnlockWeapon()
    {
        weaponLocked = false;
    }

    void TryAttack()
    {
        // Attack speed only affects physical weapons (Fist/Sword) - no equivalent bonus was
        // requested for the Staff's magic cooldown.
        float cooldown = currentWeapon == WeaponType.Fist ? fistCooldown / stats.AttackSpeedMultiplier
            : currentWeapon == WeaponType.Sword ? swordCooldown / stats.AttackSpeedMultiplier
            : staffCooldown;

        if (Time.time - lastAttackTime < cooldown) return;
        lastAttackTime = Time.time;

        switch (currentWeapon)
        {
            case WeaponType.Fist:
                MeleeAttack(fistOffset * stats.RangeMultiplier, fistRange * stats.RangeMultiplier, ScaledPhysicalDamage(fistDamage), fistVisualSprite);
                break;
            case WeaponType.Sword:
                MeleeAttack(swordOffset * stats.RangeMultiplier, swordRange * stats.RangeMultiplier, ScaledPhysicalDamage(swordDamage), swordVisualSprite);
                break;
            case WeaponType.Staff:
                LaunchProjectile(projectileSprite, ScaledMagicDamage(staffDamage), projectileSpeed, StaffMaxRange);
                break;
        }
    }

    int ScaledPhysicalDamage(int baseDamage) => Mathf.RoundToInt(baseDamage * stats.PhysicalDamageMultiplier);
    int ScaledMagicDamage(int baseDamage) => Mathf.RoundToInt(baseDamage * stats.MagicDamageMultiplier);

    void UseHotbarSlot(int index)
    {
        string itemId = inventory.hotbarSlots[index];
        if (string.IsNullOrEmpty(itemId)) return;

        if (itemId == ItemIds.Bomb)
        {
            TryThrowBomb();
            return;
        }

        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition != null && definition.HealAmount > 0)
        {
            UsePotion(itemId, definition.HealAmount);
            return;
        }

        TryThrow(itemId);
    }

    void UsePotion(string itemId, int healAmount)
    {
        float cooldown = throwCooldown / stats.AttackSpeedMultiplier;
        if (Time.time - lastThrowTime < cooldown) return;
        if (!inventory.TryConsume(itemId)) return;

        lastThrowTime = Time.time;
        health.Heal(healAmount);
    }

    void TryThrow(string itemId)
    {
        float cooldown = throwCooldown / stats.AttackSpeedMultiplier;
        if (Time.time - lastThrowTime < cooldown) return;
        if (!inventory.TryConsume(itemId)) return;

        lastThrowTime = Time.time;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        LaunchProjectile(definition != null ? definition.Icon : null, ScaledPhysicalDamage(throwDamage), throwSpeed, ThrowMaxRange);
    }

    void TryThrowBomb()
    {
        float cooldown = throwCooldown / stats.AttackSpeedMultiplier;
        if (Time.time - lastThrowTime < cooldown) return;
        if (!inventory.TryConsume(ItemIds.Bomb)) return;

        lastThrowTime = Time.time;

        Vector2 direction = AimWithInertia();
        Vector2 spawnPos = (Vector2)transform.position + aimDirection * 0.6f;

        GameObject go = new GameObject("Bomb", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Bomb));
        go.transform.position = spawnPos;

        ItemDefinition bombDefinition = ItemDatabase.Get(ItemIds.Bomb);
        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = bombDefinition != null ? bombDefinition.Icon : null;
        renderer.sortingOrder = 0;

        Rigidbody2D bombBody = go.GetComponent<Rigidbody2D>();
        bombBody.gravityScale = 0f;

        CircleCollider2D bombCollider = go.GetComponent<CircleCollider2D>();
        bombCollider.radius = 0.2f;
        if (bodyCollider != null) Physics2D.IgnoreCollision(bombCollider, bodyCollider);

        Bomb bomb = go.GetComponent<Bomb>();
        bomb.damage = ScaledPhysicalDamage(bombDamage);
        bomb.explosionRadius = bombExplosionRadius;
        bomb.fuseTime = bombFuseTime;
        bomb.explosionSprite = explosionSprite;
        bomb.Launch(direction, bombSpeed, ThrowMaxRange);
    }

    // Base direction is the chosen cardinal aim, but the player's current movement blends in:
    // aiming right while walking up-right sends the shot right-and-up, not purely right.
    Vector2 AimWithInertia()
    {
        Vector2 direction = aimDirection + moveInput;
        return direction.sqrMagnitude < 0.01f ? aimDirection : direction.normalized;
    }

    void MeleeAttack(float offset, float range, int damage, Sprite visualSprite)
    {
        Vector2 origin = (Vector2)transform.position + aimDirection * offset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, range);
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) targetHealth.TakeDamage(damage);

            DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
            if (destructible != null) destructible.TryDamage(damage, stats.force);
        }

        SpawnAttackVisual(visualSprite, origin, range);
    }

    void SpawnAttackVisual(Sprite sprite, Vector2 position, float range)
    {
        if (sprite == null) return;

        GameObject go = new GameObject("AttackVisual", typeof(SpriteRenderer), typeof(AttackVisual));
        go.transform.position = position;
        go.transform.up = aimDirection;
        go.transform.localScale = Vector3.one * (range * 2f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
    }

    void LaunchProjectile(Sprite sprite, int damage, float speed, float maxDistance)
    {
        Vector2 direction = AimWithInertia();
        Vector2 spawnPos = (Vector2)transform.position + aimDirection * 0.6f;

        GameObject go = new GameObject("Projectile", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Projectile));
        go.transform.position = spawnPos;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        Rigidbody2D projectileBody = go.GetComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;

        CircleCollider2D projectileCollider = go.GetComponent<CircleCollider2D>();
        projectileCollider.radius = 0.15f;
        if (bodyCollider != null) Physics2D.IgnoreCollision(projectileCollider, bodyCollider);

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.damage = damage;
        projectile.speed = speed;
        projectile.maxDistance = maxDistance;
        projectile.attackerForce = stats.force;
        projectile.Launch(direction);
    }

    void HandleDeath()
    {
        isDead = true;
        // A save is only ever a "come back later" convenience - it must never survive death,
        // or a player could just relaunch the game to undo dying (save-scumming).
        SaveManager.DeleteSave();
        Debug.Log("Player died.");
    }
}
