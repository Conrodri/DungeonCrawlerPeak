using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public int attackDamage = 1;
    public float attackRange = 0.9f;
    public float attackCooldown = 0.4f;

    Rigidbody2D rb;
    Health health;
    Vector2 moveInput;
    Vector2 facing = Vector2.down;
    float lastAttackTime = -999f;
    bool isDead;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
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

        float x = 0f;
        float y = 0f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;

        moveInput = new Vector2(x, y).normalized;
        if (moveInput.sqrMagnitude > 0.01f) facing = moveInput;

        if (kb.spaceKey.wasPressedThisFrame) Attack();
    }

    void FixedUpdate()
    {
        rb.linearVelocity = isDead ? Vector2.zero : moveInput * moveSpeed;
    }

    void Attack()
    {
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;

        Vector2 origin = (Vector2)transform.position + facing * 0.5f;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, attackRange);
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) targetHealth.TakeDamage(attackDamage);
        }
    }

    void HandleDeath()
    {
        isDead = true;
        Debug.Log("Player died.");
    }
}
