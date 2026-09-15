using System;
using UnityEngine;

// Distinct from EnemyController on purpose - a boss's attack pattern (charge + projectile
// volley + a rage phase) doesn't fit the generic "chase and contact-damage" model.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class BossController : MonoBehaviour
{
    public float moveSpeed = 1.5f;
    public int contactDamage = 2;
    public float contactCooldown = 1f;

    [Header("Charge")]
    public float chargeSpeed = 8f;
    public float chargeDuration = 0.5f;
    public float chargeCooldown = 4f;

    [Header("Volley")]
    public Sprite projectileSprite;
    public int volleyProjectileCount = 5;
    public int volleyDamage = 1;
    public float volleyProjectileSpeed = 6f;
    public float volleySpreadAngle = 45f;
    public float volleyCooldown = 3.5f;

    [Header("Rage")]
    // Fraction of max HP at which the boss permanently speeds up and attacks more often.
    public float rageHealthFraction = 0.5f;
    public float rageSpeedMultiplier = 1.5f;
    public float rageCooldownMultiplier = 0.6f;

    [Header("Combat")]
    // "lorsqu'un monstre lance une attaque, il ne peut plus se deplacer" (2026-09-15 request) -
    // every INSTANT attack (FireVolley, SpitSlobber, Golem's shockwave, Kraken's tentacle slap,
    // Ent's root) roots the boss in place for this long via LockMovement/ChaseOrLock below,
    // instead of sliding toward the player mid-cast. Charges/dives/the teleport-lunge are exempt
    // on purpose - their movement IS the attack, and a telegraph (Ent/Aigle) already freezes
    // itself with its own rb.linearVelocity = Vector2.zero.
    public float attackLockDuration = 0.4f;

    // Set by DungeonGenerator.SetupBossRoom per biome family (see BossFamilyFor) - Generic keeps
    // the plain charge+volley pattern above; every other value swaps in that family's own
    // XxxFixedUpdate below instead. Each bespoke kit still reuses the generic volley (flavor
    // difference only) and usually the generic charge/StartCharge too, just with a different
    // payload attached - see each kit's own header for what it actually adds.
    public enum BossKit { Generic, Cerbere, Anaconda, Ent, Golem, Kraken, Aigle, Arpenteur }
    public BossKit kit = BossKit.Generic;

    [Header("Cerbere Kit")]
    // 3 short lunges in a row, each one a bite that advances the boss (reuses charging/
    // chargeDirection/chargeEndTime below, just fired chainBiteCount times with a short gap
    // between each instead of once) - "3 morsures en chaine qui le fait avancer".
    public int chainBiteCount = 3;
    public float chainBiteGap = 0.35f;
    public float chainBiteCooldownMin = 3f;
    public float chainBiteCooldownMax = 4f;
    // Spits toward the player's current position, dropping a slowing puddle there instead of a
    // damaging projectile - "un crachat de bave pour mettre une flaque au sol ralentissante".
    // Also reused as-is by Anaconda's poison puddle sprite (see DropPoisonPuddle) - same generic
    // hazard-puddle sprite, no need for a second field.
    public Sprite slobberPuddleSprite;
    public float slobberCooldown = 5f;
    public float slobberRange = 6f;
    public float slobberSlowMultiplier = 0.5f;
    public float slobberSlowDuration = 3f;
    public float slobberPuddleLifetime = 5f;

    [Header("Anaconda Kit")]
    // The charge itself is the plain generic lunge (StartCharge below) - landing leaves a poison
    // puddle where the bite connects instead of a one-off hit, punishing a player who lingers.
    public int poisonDamagePerTick = 1;
    public float poisonTickInterval = 1f;
    public float poisonPuddleLifetime = 4f;

    [Header("Ent Kit")]
    // Roots erupt where the player is standing right NOW, after a short telegraph delay - moving
    // away dodges it, standing still doesn't. Replaces the generic charge slot entirely (an Ent
    // doesn't lunge).
    public float rootTelegraphDelay = 1f;
    public float rootDamageRadius = 2f;
    public int rootDamage = 2;
    public float rootCooldown = 4f;

    [Header("Golem Kit")]
    // The charge itself is the plain generic lunge - on landing it also triggers a shockwave pulse
    // around the impact point, so standing just outside the lunge's own hitbox doesn't fully dodge it.
    public float shockwaveRadius = 2.5f;
    public int shockwaveDamage = 2;

    [Header("Kraken Kit")]
    // Replaces the generic charge slot: a Kraken doesn't dash, it slaps anything already within
    // tentacle range instead.
    public float tentacleRange = 2.2f;
    public int tentacleDamage = 2;
    public float tentacleCooldown = 2.5f;

    [Header("Aigle Kit")]
    // Hovers still for a telegraph beat, then dives through the player in a straight line - much
    // faster/longer than the generic lunge (reuses charging/chargeDirection/chargeEndTime with its
    // own speed/duration via the StartCharge overload below instead of chargeSpeed/chargeDuration).
    public float diveTelegraphDelay = 0.6f;
    public float diveSpeed = 14f;
    public float diveDuration = 0.8f;
    public float diveCooldown = 5f;

    [Header("Arpenteur Kit")]
    // No telegraph at all, unlike every other kit above - the ambush IS the mechanic. Blinks to a
    // random point at a fixed ring distance around the player then immediately lunges back in.
    public float teleportDistance = 4f;
    public float teleportCooldown = 5f;

    [Header("Modifiers")]
    // Vampirique (see BossModifier/DungeonGenerator.ApplyBossModifiers) - fraction of contact
    // damage healed back on every landed hit. 0 = no modifier. Volley hits don't trigger this -
    // Projectile doesn't report back to its source on impact.
    public float lifestealFraction;

    public event Action OnDied;

    Rigidbody2D rb;
    Health health;
    CircleCollider2D bodyCollider;
    Transform target;
    Rect? roomBounds;

    float lastContactTime = -999f;
    float lastChargeTime = -999f;
    float lastVolleyTime = -999f;
    bool charging;
    Vector2 chargeDirection;
    float activeChargeSpeed;
    float chargeEndTime;
    Action onChargeEnd;
    bool enraged;
    float attackLockEndTime;

    // Cerbere-only state (see BossKit.Cerbere) - chainBitesLeft counts the bites still due after
    // the one currently charging/gapping; nextChainBiteCooldown is re-rolled each cycle (3-4s).
    int chainBitesLeft;
    float nextBiteTime;
    float lastChainBiteTime = -999f;
    float nextChainBiteCooldown;
    float lastSlobberTime = -999f;

    // Ent-only state.
    bool rootTelegraphing;
    float rootTelegraphEndTime;
    Vector2 rootTelegraphTarget;
    float lastRootTime = -999f;

    // Kraken-only state.
    float lastTentacleTime = -999f;

    // Aigle-only state.
    bool diveTelegraphing;
    float diveTelegraphEndTime;
    Vector2 diveTelegraphDirection;
    float lastDiveTime = -999f;

    // Arpenteur-only state.
    float lastTeleportTime = -999f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        bodyCollider = GetComponent<CircleCollider2D>();
        health.OnDeath += HandleDeath;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        nextChainBiteCooldown = UnityEngine.Random.Range(chainBiteCooldownMin, chainBiteCooldownMax);
    }

    public void SetTarget(Transform t) => target = t;
    public void SetRoomBounds(Rect bounds) => roomBounds = bounds;

    void FixedUpdate()
    {
        if (target == null) return;

        if (!enraged && health.currentHealth <= health.maxHealth * rageHealthFraction) enraged = true;
        float cooldownScale = enraged ? rageCooldownMultiplier : 1f;

        if (charging)
        {
            rb.linearVelocity = chargeDirection * activeChargeSpeed;
            if (Time.time >= chargeEndTime)
            {
                charging = false;
                // Mid-chain (see BossKit.Cerbere) - the gap below fires the next bite instead of
                // falling through to a fresh charge/volley decision.
                if (chainBitesLeft > 0) nextBiteTime = Time.time + chainBiteGap;
                Action end = onChargeEnd;
                onChargeEnd = null;
                end?.Invoke();
            }
            return;
        }

        Vector2 toTarget = (Vector2)target.position - rb.position;
        Vector2 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;

        switch (kit)
        {
            case BossKit.Cerbere: CerbereFixedUpdate(dir, cooldownScale); return;
            case BossKit.Anaconda: AnacondaFixedUpdate(dir, cooldownScale); return;
            case BossKit.Ent: EntFixedUpdate(dir, cooldownScale); return;
            case BossKit.Golem: GolemFixedUpdate(dir, cooldownScale); return;
            case BossKit.Kraken: KrakenFixedUpdate(dir, cooldownScale); return;
            case BossKit.Aigle: AigleFixedUpdate(dir, cooldownScale); return;
            case BossKit.Arpenteur: ArpenteurFixedUpdate(dir, cooldownScale); return;
        }

        ChaseOrLock(dir);

        if (Time.time - lastChargeTime >= chargeCooldown * cooldownScale)
        {
            lastChargeTime = Time.time;
            StartCharge(dir);
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    // Chain bite reuses StartCharge/charging above (a bite IS a short lunge that also lands
    // contact damage via TryContactDamage on collision) fired chainBiteCount times with a short
    // gap between each, instead of once. Between chains and outside the spit's own cooldown, it
    // just chases like the generic pattern does.
    void CerbereFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        if (chainBitesLeft > 0)
        {
            if (Time.time < nextBiteTime)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }
            chainBitesLeft--;
            StartCharge(dirToTarget);
            return;
        }

        ChaseOrLock(dirToTarget);

        if (Time.time - lastChainBiteTime >= nextChainBiteCooldown * cooldownScale)
        {
            lastChainBiteTime = Time.time;
            nextChainBiteCooldown = UnityEngine.Random.Range(chainBiteCooldownMin, chainBiteCooldownMax);
            chainBitesLeft = chainBiteCount - 1;
            StartCharge(dirToTarget);
        }
        else if (Time.time - lastSlobberTime >= slobberCooldown * cooldownScale)
        {
            lastSlobberTime = Time.time;
            SpitSlobber();
            LockMovement();
        }
    }

    void SpitSlobber()
    {
        if (target == null) return;
        Vector2 toTarget = (Vector2)target.position - rb.position;
        float dist = Mathf.Min(slobberRange, toTarget.magnitude);
        Vector2 spawnPos = rb.position + (toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero) * dist;

        GameObject puddle = SlowPuddle.SpawnAt(spawnPos, slobberPuddleSprite, slobberSlowMultiplier, slobberSlowDuration, slobberPuddleLifetime);
        // Same parent as the boss itself (DungeonRoot) - otherwise it leaks across floors like the
        // corpse/pickup leaks fixed earlier (see Corpse.SpawnAt's callers).
        puddle.transform.SetParent(transform.parent);
    }

    // "Morsure empoisonnee" - the lunge is the plain generic charge (see StartCharge), it just
    // drops a damaging puddle where it lands via onChargeEnd instead of a one-off hit.
    void AnacondaFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        ChaseOrLock(dirToTarget);

        if (Time.time - lastChargeTime >= chargeCooldown * cooldownScale)
        {
            lastChargeTime = Time.time;
            StartCharge(dirToTarget, DropPoisonPuddle);
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    void DropPoisonPuddle()
    {
        GameObject puddle = SlowPuddle.SpawnAt(rb.position, slobberPuddleSprite, 1f, 0f, poisonPuddleLifetime, poisonDamagePerTick, poisonTickInterval);
        puddle.transform.SetParent(transform.parent);
    }

    // "Racines" - stands still and telegraphs, then erupts under wherever the player was standing
    // when the telegraph started. Moving away dodges it; the generic charge slot is unused here.
    void EntFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        if (rootTelegraphing)
        {
            rb.linearVelocity = Vector2.zero;
            if (Time.time >= rootTelegraphEndTime)
            {
                rootTelegraphing = false;
                ResolveAoEDamage(rootTelegraphTarget, rootDamageRadius, rootDamage);
                LockMovement();
            }
            return;
        }

        ChaseOrLock(dirToTarget);

        if (Time.time - lastRootTime >= rootCooldown * cooldownScale)
        {
            lastRootTime = Time.time;
            rootTelegraphing = true;
            rootTelegraphTarget = target.position;
            rootTelegraphEndTime = Time.time + rootTelegraphDelay;
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    // "Poing + onde de choc" - the lunge is the plain generic charge, it just also hits anything
    // within shockwaveRadius of the impact point via onChargeEnd, not just whatever it collided with.
    void GolemFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        ChaseOrLock(dirToTarget);

        if (Time.time - lastChargeTime >= chargeCooldown * cooldownScale)
        {
            lastChargeTime = Time.time;
            StartCharge(dirToTarget, () => { ResolveAoEDamage(rb.position, shockwaveRadius, shockwaveDamage); LockMovement(); });
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    // "Gifle de tentacule" - replaces the dash entirely: a Kraken slaps anything already in range
    // instead of lunging across the room.
    void KrakenFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        ChaseOrLock(dirToTarget);

        float distToTarget = Vector2.Distance(rb.position, target.position);
        if (distToTarget <= tentacleRange && Time.time - lastTentacleTime >= tentacleCooldown * cooldownScale)
        {
            lastTentacleTime = Time.time;
            ResolveAoEDamage(rb.position, tentacleRange, tentacleDamage);
            LockMovement();
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    // "Pique" - hovers for a telegraph beat, then dives through the player far faster/longer than
    // the generic lunge (StartCharge's speed/duration overload below).
    void AigleFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        if (diveTelegraphing)
        {
            rb.linearVelocity = Vector2.zero;
            if (Time.time >= diveTelegraphEndTime)
            {
                diveTelegraphing = false;
                StartCharge(diveTelegraphDirection, diveSpeed, diveDuration);
            }
            return;
        }

        ChaseOrLock(dirToTarget);

        if (Time.time - lastDiveTime >= diveCooldown * cooldownScale)
        {
            lastDiveTime = Time.time;
            diveTelegraphing = true;
            diveTelegraphDirection = dirToTarget;
            diveTelegraphEndTime = Time.time + diveTelegraphDelay;
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    // "Teleportation embusquee" - no telegraph, unlike every kit above: blinks to a random point
    // at a fixed ring distance around the player then immediately lunges back in.
    void ArpenteurFixedUpdate(Vector2 dirToTarget, float cooldownScale)
    {
        ChaseOrLock(dirToTarget);

        if (Time.time - lastTeleportTime >= teleportCooldown * cooldownScale)
        {
            lastTeleportTime = Time.time;
            TeleportAmbush();
        }
        else if (Time.time - lastVolleyTime >= volleyCooldown * cooldownScale)
        {
            lastVolleyTime = Time.time;
            FireVolley();
            LockMovement();
        }
    }

    void TeleportAmbush()
    {
        float angle = UnityEngine.Random.Range(0f, 360f);
        Vector2 offset = (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.right) * teleportDistance;
        Vector2 newPos = (Vector2)target.position + offset;
        if (roomBounds.HasValue)
        {
            Rect b = roomBounds.Value;
            float margin = 1f + bodyCollider.radius;
            newPos.x = Mathf.Clamp(newPos.x, b.xMin + margin, b.xMax - margin);
            newPos.y = Mathf.Clamp(newPos.y, b.yMin + margin, b.yMax - margin);
        }
        rb.position = newPos;

        Vector2 dirToTarget = (Vector2)target.position - newPos;
        StartCharge(dirToTarget.sqrMagnitude > 0.0001f ? dirToTarget.normalized : Vector2.zero);
    }

    // Instant one-shot AoE check used by the ground-slam-style kits (Ent roots, Golem shockwave,
    // Kraken tentacle slap) instead of a spawned hazard GameObject - the damage window is a single
    // FixedUpdate tick, not a lingering zone like SlowPuddle. target is always the player
    // (see SetTarget), so no overlap query is needed, just a distance check.
    void ResolveAoEDamage(Vector2 center, float radius, int damage)
    {
        if (target == null) return;
        if (Vector2.Distance(center, target.position) > radius) return;
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth == null) return;
        targetHealth.TakeDamageFromEnemy(damage, AttackSource.Random);
    }

    void LateUpdate()
    {
        if (!roomBounds.HasValue) return;

        Rect b = roomBounds.Value;
        float margin = 1f + bodyCollider.radius;
        Vector2 clamped = rb.position;
        clamped.x = Mathf.Clamp(clamped.x, b.xMin + margin, b.xMax - margin);
        clamped.y = Mathf.Clamp(clamped.y, b.yMin + margin, b.yMax - margin);
        if (clamped != rb.position) rb.position = clamped;
    }

    // Shared chase-velocity line every kit's FixedUpdate used to set directly - now routed through
    // here so an active attackLockEndTime (see LockMovement) roots the boss in place instead.
    void ChaseOrLock(Vector2 dirToTarget)
    {
        if (Time.time < attackLockEndTime) { rb.linearVelocity = Vector2.zero; return; }
        ChaseOrLock(dirToTarget);
    }

    void LockMovement() => attackLockEndTime = Time.time + attackLockDuration;

    void StartCharge(Vector2 dir, Action onEnd = null) => StartCharge(dir, chargeSpeed, chargeDuration, onEnd);

    // Speed/duration overload lets a kit's lunge read as something other than the generic
    // chargeSpeed/chargeDuration (see Aigle's dive) without a second charging state machine, and
    // onEnd lets a kit attach a one-off payload to the landing (see Anaconda/Golem) without the
    // shared charging block above needing to know which kit is active.
    void StartCharge(Vector2 dir, float speed, float duration, Action onEnd = null)
    {
        if (dir == Vector2.zero) return;
        charging = true;
        chargeDirection = dir;
        activeChargeSpeed = speed;
        chargeEndTime = Time.time + duration;
        onChargeEnd = onEnd;
    }

    void FireVolley()
    {
        if (target == null) return;
        Vector2 baseDir = ((Vector2)target.position - rb.position).normalized;

        for (int i = 0; i < volleyProjectileCount; i++)
        {
            float t = volleyProjectileCount > 1 ? i / (float)(volleyProjectileCount - 1) : 0.5f;
            float angle = Mathf.Lerp(-volleySpreadAngle / 2f, volleySpreadAngle / 2f, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * baseDir;
            LaunchProjectile(dir);
        }
    }

    void LaunchProjectile(Vector2 direction)
    {
        GameObject go = new GameObject("BossProjectile", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Projectile));
        go.transform.position = (Vector2)transform.position + direction * 0.6f;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = projectileSprite;
        renderer.sortingOrder = 0;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;

        CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
        collider.radius = 0.15f;
        if (bodyCollider != null) Physics2D.IgnoreCollision(collider, bodyCollider);

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.damage = volleyDamage;
        projectile.speed = volleyProjectileSpeed;
        projectile.maxDistance = 20f;
        projectile.ignoreTag = ""; // must be able to hit the player, unlike a player-thrown one
        projectile.Launch(direction);
    }

    void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    void TryContactDamage(Collider2D other)
    {
        if (Time.time - lastContactTime < contactCooldown) return;
        if (!other.CompareTag("Player")) return;

        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth == null) return;

        // No AttackSource given for bosses in the 2026-09-14 spec - rolls a fully random body part
        // (see PlayerLimbs.RollTarget).
        targetHealth.TakeDamageFromEnemy(contactDamage, AttackSource.Random);
        if (lifestealFraction > 0f) health.Heal(Mathf.CeilToInt(contactDamage * lifestealFraction));
        lastContactTime = Time.time;
    }

    void HandleDeath()
    {
        Debug.Log(name + " (boss) died.");
        OnDied?.Invoke();
        Destroy(gameObject);
    }
}
