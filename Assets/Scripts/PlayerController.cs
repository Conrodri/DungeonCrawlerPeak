using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class PlayerController : MonoBehaviour
{
    public enum WeaponType { Fist, Sword, Staff }

    public float moveSpeed = 5f;

    [Header("Weapon")]
    public WeaponType currentWeapon = WeaponType.Fist;
    public Sprite projectileSprite;

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

    Rigidbody2D rb;
    Health health;
    CircleCollider2D bodyCollider;
    Vector2 moveInput;
    Vector2 aimDirection = Vector2.down;
    float lastAttackTime = -999f;
    bool isDead;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
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
            case WeaponType.Fist: MeleeAttack(fistOffset, fistRange, fistDamage); break;
            case WeaponType.Sword: MeleeAttack(swordOffset, swordRange, swordDamage); break;
            case WeaponType.Staff: FireProjectile(); break;
        }
    }

    void MeleeAttack(float offset, float range, int damage)
    {
        Vector2 origin = (Vector2)transform.position + aimDirection * offset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, range);
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) targetHealth.TakeDamage(damage);
        }
    }

    void FireProjectile()
    {
        Vector2 spawnPos = (Vector2)transform.position + aimDirection * 0.6f;

        GameObject go = new GameObject("Projectile", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Projectile));
        go.transform.position = spawnPos;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = projectileSprite;
        renderer.sortingOrder = 0;

        Rigidbody2D projectileBody = go.GetComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;

        CircleCollider2D projectileCollider = go.GetComponent<CircleCollider2D>();
        projectileCollider.radius = 0.15f;
        if (bodyCollider != null) Physics2D.IgnoreCollision(projectileCollider, bodyCollider);

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.damage = staffDamage;
        projectile.speed = projectileSpeed;
        projectile.Launch(aimDirection);
    }

    void HandleDeath()
    {
        isDead = true;
        Debug.Log("Player died.");
    }
}
