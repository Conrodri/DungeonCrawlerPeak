using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerController : MonoBehaviour
{
    public enum WeaponType { Fist, Sword, Staff }

    public float moveSpeed = 5f;

    [Header("Weapon")]
    public WeaponType currentWeapon = WeaponType.Fist;
    public Sprite projectileSprite;
    public Sprite fistVisualSprite;
    public Sprite swordVisualSprite;

    [Header("Fist")]
    public int fistDamage = 1;
    public float fistRange = 0.5f;
    public float fistOffset = 0.4f;
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
    public Sprite shurikenSprite;
    public Sprite caillouSprite;
    public Sprite batonSprite;
    public int throwDamage = 1;
    public float throwSpeed = 10f;
    public float throwCooldown = 0.3f;
    public float throwRangeMultiplier = 2f;

    // A weapon's total reach: how far from the player its hit area extends.
    float SwordReach => swordOffset + swordRange;
    float StaffMaxRange => SwordReach * staffRangeMultiplier;
    float ThrowMaxRange => SwordReach * throwRangeMultiplier;

    Rigidbody2D rb;
    Health health;
    PlayerInventory inventory;
    CircleCollider2D bodyCollider;
    Vector2 moveInput;
    Vector2 aimDirection = Vector2.down;
    float lastAttackTime = -999f;
    float lastThrowTime = -999f;
    bool isDead;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        inventory = GetComponent<PlayerInventory>();
        bodyCollider = GetComponent<CircleCollider2D>();
        health.OnDeath += HandleDeath;
    }

    void Update()
    {
        if (isDead) return;

        var kb = Keyboard.current;
        if (kb == null)
        {
            moveInput = Vector2.zero;
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

        // Aiming/attacking: arrow keys only, cardinal directions, no diagonals.
        Vector2 aim = Vector2.zero;
        if (kb.upArrowKey.isPressed) aim = Vector2.up;
        else if (kb.downArrowKey.isPressed) aim = Vector2.down;
        else if (kb.leftArrowKey.isPressed) aim = Vector2.left;
        else if (kb.rightArrowKey.isPressed) aim = Vector2.right;

        if (aim != Vector2.zero)
        {
            aimDirection = aim;
            TryAttack();
        }

        // Hotbar: slots 1-3 throw a stocked consumable; 4-5 are reserved (bombs, later).
        if (kb.digit1Key.wasPressedThisFrame) TryThrow(ItemType.Shuriken, shurikenSprite);
        if (kb.digit2Key.wasPressedThisFrame) TryThrow(ItemType.Caillou, caillouSprite);
        if (kb.digit3Key.wasPressedThisFrame) TryThrow(ItemType.Baton, batonSprite);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = isDead ? Vector2.zero : moveInput * moveSpeed;
    }

    public void EquipWeapon(WeaponType weapon)
    {
        currentWeapon = weapon;
        Debug.Log("Equipped " + weapon);
    }

    void TryAttack()
    {
        float cooldown = currentWeapon == WeaponType.Fist ? fistCooldown
            : currentWeapon == WeaponType.Sword ? swordCooldown
            : staffCooldown;

        if (Time.time - lastAttackTime < cooldown) return;
        lastAttackTime = Time.time;

        switch (currentWeapon)
        {
            case WeaponType.Fist: MeleeAttack(fistOffset, fistRange, fistDamage, fistVisualSprite); break;
            case WeaponType.Sword: MeleeAttack(swordOffset, swordRange, swordDamage, swordVisualSprite); break;
            case WeaponType.Staff: LaunchProjectile(projectileSprite, staffDamage, projectileSpeed, StaffMaxRange); break;
        }
    }

    void TryThrow(ItemType type, Sprite sprite)
    {
        if (Time.time - lastThrowTime < throwCooldown) return;
        if (!inventory.TryConsume(type)) return;

        lastThrowTime = Time.time;
        LaunchProjectile(sprite, throwDamage, throwSpeed, ThrowMaxRange);
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
        // Base direction is the chosen cardinal aim, but the player's current movement blends
        // in: aiming right while walking up-right sends the shot right-and-up, not purely right.
        Vector2 direction = aimDirection + moveInput;
        if (direction.sqrMagnitude < 0.01f) direction = aimDirection;

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
        projectile.Launch(direction);
    }

    void HandleDeath()
    {
        isDead = true;
        Debug.Log("Player died.");
    }
}
