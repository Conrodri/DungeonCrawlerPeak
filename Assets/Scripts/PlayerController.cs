using System.Collections.Generic;
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
    public float sprintSpeedMultiplier = 1.5f;
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
    // Which arm currently wields currentWeapon (Fist/Sword/Staff all count - you're still
    // swinging/aiming with a hand either way) - toggled with H (SwitchWeaponHand). See
    // PlayerLimbs/LimbState: a broken weaponHand blocks TryAttack entirely until the player either
    // switches to the other arm or gets it repaired (Tavernier "Se reposer").
    public BodyPart weaponHand = BodyPart.ArmRight;
    // Base Sword/Staff never go through ItemDatabase (see WeaponPickup - equipping one is a flat
    // capability flip, not an inventory item), so their durability lives here as a plain pair of
    // maxes instead of ItemDefinition.MaxDurability. 0 = infinite, matching that same convention -
    // Fist's currentWeaponDurability is always 0, which is also why DamageWeaponDurability needs
    // no explicit "is this Fist" check.
    public int maxSwordDurability = 40;
    public int maxStaffDurability = 40;
    int currentWeaponDurability;
    // Which inventory item is currently worn in the PlayerEquipment.Weapon slot (see
    // EquipWeaponItem/UnequipToFistIfCurrent) - null for Fist or a curse-forced weapon (see
    // ForceEquipWeapon, which never goes through the equipment slot at all). Durability for THIS
    // case lives on equipment.GetDurability(EquipmentSlotType.Weapon) instead of
    // currentWeaponDurability above - see DamageWeaponDurability.
    string currentWeaponItemId;
    public Sprite projectileSprite;
    public Sprite fistVisualSprite;
    public Sprite swordVisualSprite;
    // Shown above the player's head via StatusIconDisplay while the Hole debuff is active.
    public Sprite movementDebuffIcon;

    [Header("Fist")]
    public int fistDamage = 1;
    public float fistRange = 1.0f;
    public float fistOffset = 0.8f;
    public float fistCooldown = 0.33f;
    public float fistStaminaCost = 5f;

    [Header("Sword")]
    public int swordDamage = 2;
    public float swordRange = 0.7f;
    public float swordOffset = 0.9f;
    // Recharge before the NEXT swing can start (2026-09-16 request: originally 0.25, shorter than
    // swordSwingDuration below, so a new swing could start before the previous one's visual
    // finished - corrected same day to 0.75, longer than swordSwingDuration, so each swing's sweep
    // always plays out fully before the next one can begin).
    public float swordCooldown = 0.75f;
    // How long the semi-circular swing's visual sweep takes to play out (see SwordSwingVisual) -
    // purely cosmetic, not tied to swordCooldown or attackLockEndTime.
    public float swordSwingDuration = 0.5f;
    // Half-angle of the swing arc on EACH side of aimDirection - 90 = a full semi-circle in front
    // of the player, matching the "semi circulaire" request exactly.
    public float swordArcHalfDegrees = 90f;
    public float swordStaminaCost = 8f;

    [Header("Staff")]
    public int staffDamage = 1;
    public float staffCooldown = 0.5f;
    public float projectileSpeed = 8f;
    // The staff isn't hitscan/infinite range: it reaches three sword-lengths out.
    public float staffRangeMultiplier = 3f;
    public float staffStaminaCost = 10f;

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

    // 2026-09-16 request: "rajoutes des competences, type orb de foudre, qui consommera du mana...
    // une orbe qui se lance comme un projectile normal, qui rebondit entre tous les enemis a moins
    // de 2 unites de l'impact de l'orbe". First entry in what's meant to grow into a real spell
    // list later - bound to its own key (R) rather than a WeaponType, so it's available regardless
    // of whatever's currently equipped in the weapon hand.
    [Header("Lightning Orb")]
    public int lightningOrbManaCost = 20;
    public int lightningOrbDamage = 3;
    public float lightningOrbSpeed = 9f;
    public float lightningOrbRange = 10f;
    public float lightningOrbCooldown = 1f;
    // "rebondit entre tous les ennemis a moins de 2 unites de l'impact" - see
    // Projectile.chainRadius/ChainToNearbyEnemies.
    public float lightningOrbChainRadius = 2f;
    public Sprite lightningOrbSprite;
    public Sprite lightningBoltSprite;

    // 2026-09-19 request: "boule de feu... fera grossir un projectile devant soi puis le
    // lancera" - a visible wind-up (see FireballCharge) before it fires as an oversized
    // single-target projectile, unlike Orbe de Foudre's instant cast.
    [Header("Boule de Feu")]
    public int fireballManaCost = 30;
    public int fireballDamage = 5;
    public float fireballSpeed = 7f;
    public float fireballRange = 9f;
    public float fireballCooldown = 1.5f;
    public float fireballChargeDuration = 0.5f;
    public Sprite fireballSprite;

    // 2026-09-19 request: "mur de feu ou ligne de feu, qui laissera une trainee de feu pour 3
    // secondes. Et infligera le debuff brulure pour 5 secondes (se refresh tant que la cible est
    // dans le feu)" - a line of FireTrail segments in the aim direction, each applying/refreshing
    // BurnStatus on anything standing in it.
    [Header("Ligne de Feu")]
    public int fireLineManaCost = 25;
    public float fireLineRange = 6f;
    public float fireLineSegmentSpacing = 1.3f;
    public float fireLineLifetime = 3f;
    public float fireLineCooldown = 4f;
    public int burnDamagePerTick = 2;
    public float burnTickInterval = 1f;
    public float burnDuration = 5f;
    public Sprite fireLineSprite;
    public Sprite burnIconSprite;

    // A weapon's total reach: how far from the player its hit area extends.
    float SwordReach => (swordOffset + swordRange) * stats.RangeMultiplier;
    float StaffMaxRange => SwordReach * staffRangeMultiplier;
    float ThrowMaxRange => SwordReach * throwRangeMultiplier;

    Rigidbody2D rb;
    Health health;
    Stamina stamina;
    Mana mana;
    PlayerInventory inventory;
    PlayerEquipment equipment;
    PlayerStats stats;
    PlayerSkills skills;
    PlayerLimbs limbs;
    CircleCollider2D bodyCollider;
    StatusIconDisplay statusIcons;
    Vector2 moveInput;
    Vector2 aimDirection = Vector2.down;
    float lastAttackTime = -999f;
    float attackLockEndTime = -999f;
    float lastThrowTime = -999f;
    float lastLightningOrbTime = -999f;
    float lastFireballTime = -999f;
    float lastFireLineTime = -999f;
    // Small forward "step" on a melee swing (Fist/Sword, see MeleeAttack) - a plain root (velocity
    // zero for the whole attackLockEndTime window) read as the character just standing still while
    // punching, this gives the first sliver of that window a forward nudge instead so it reads as
    // a real step-in (2026-09-16 request). Deliberately shorter than any weapon's own cooldown so
    // it always settles back to a full stop before the swing's rooted window ends.
    const float AttackLungeDuration = 0.08f;
    const float AttackLungeSpeed = 3.5f;
    Vector2 attackLungeVelocity;
    float attackLungeEndTime = -999f;
    // Small knockback "pop" away from whoever landed the hit (2026-09-16 request) - capped to once
    // per StaggerCooldown so a burst of fast hits (e.g. standing in a boss volley) can't lock the
    // player out of moving via back-to-back stagger windows ("pas se faire stagger en chaine").
    const float StaggerDuration = 0.15f;
    const float StaggerSpeed = 6f;
    const float StaggerCooldown = 0.5f;
    Vector2 staggerVelocity;
    float staggerEndTime = -999f;
    float lastStaggerTime = -999f;
    // Selected via the hotbar (see UseItem) but not yet thrown - the next direction key press is
    // what actually launches it (see Update's aim-key handling / ThrowArmedItem), instead of the
    // old "hotbar key = instant throw in whatever direction you already happened to be aiming".
    string armedThrowItemId;
    const string ArmedThrowIconKey = "ArmedThrow";
    // See Update's directionConsumedByThrowOrCast comment - blocks a melee swing from also firing
    // off the same held key that just threw an item or cast a spell.
    Vector2 directionConsumedByThrowOrCast = Vector2.zero;
    // Same arm-then-aim pattern as armedThrowItemId, driven by SpellBarUI instead of the item
    // hotbar (2026-09-18 request: "un sort se lance en le selectionnant puis en cliquant dans une
    // direction") - mutually exclusive with armedThrowItemId, see ArmSpell/UseItem.
    string armedSpellId;
    const string ArmedSpellIconKey = "ArmedSpell";
    public string ArmedSpellId => armedSpellId;
    // Which spell sits in each of SpellBarUI's 5 slots - lives here (not on SpellBarUI itself) so
    // the Z/X/C/V/B shortcuts below (2026-09-18 request: "il nous faut evidemment une touche
    // attribuee") and a mouse click on the bar both arm the exact same thing through ArmSpell.
    // Only slot 0 is populated today (Orbe de Foudre); see Awake.
    public readonly string[] spellSlots = new string[SpellBarUI.SlotCount];
    // See ApplySlow (e.g. BossController's Cerbere slobber puddle) - a temporary multiplier on top
    // of the normal speed calc, same "take the strongest, extend the duration" pattern as
    // ApplyMovementDebuff below.
    float slowMultiplier = 1f;
    float slowEndTime = -999f;
    bool isDead;
    bool isSprinting;
    bool isRolling;
    float rollEndTime = -999f;
    float lastRollTime = -999f;
    Vector2 rollDirection;
    // Set by Hole.OnTriggerEnter2D - blocks sprint/roll until this timestamp.
    float movementDebuffEndTime = -999f;
    // See HasBrokenLeg/FixedUpdate (-50% speed) and Update's sprint block (1 damage/second while
    // sprinting on it) - explicit request, not from the original vision doc's "membres casses".
    const float SprintInjuryInterval = 1f;
    float lastSprintInjuryTime = -999f;
    bool HasBrokenLeg => limbs != null && (limbs.IsBroken(BodyPart.LegLeft) || limbs.IsBroken(BodyPart.LegRight));

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // Same tunneling guard as EnemyController: without it, a player moving against a tilemap
        // CompositeCollider2D can occasionally skip clean through a thin wall in a single physics
        // step instead of colliding with it.
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        mana = GetComponent<Mana>();
        inventory = GetComponent<PlayerInventory>();
        equipment = GetComponent<PlayerEquipment>();
        stats = GetComponent<PlayerStats>();
        skills = GetComponent<PlayerSkills>();
        limbs = GetComponent<PlayerLimbs>();
        bodyCollider = GetComponent<CircleCollider2D>();
        statusIcons = GetComponent<StatusIconDisplay>();
        health.OnDeath += HandleDeath;
        health.OnDamagedFrom += HandleDamagedFrom;

        spellSlots[0] = SpellIds.LightningOrb;
        spellSlots[1] = SpellIds.Fireball;
        spellSlots[2] = SpellIds.FireLine;
    }

    void HandleDamagedFrom(Vector2 fromPosition)
    {
        if (Time.time - lastStaggerTime < StaggerCooldown) return;

        Vector2 away = rb.position - fromPosition;
        if (away.sqrMagnitude < 0.0001f) away = -aimDirection;
        staggerVelocity = away.normalized * StaggerSpeed;
        staggerEndTime = Time.time + StaggerDuration;
        lastStaggerTime = Time.time;
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

        if (Keyboard.current == null)
        {
            moveInput = Vector2.zero;
            isSprinting = false;
            return;
        }

        if (KeyBindings.WasPressedThisFrame(GameAction.SwitchHand)) SwitchWeaponHand();

        // Movement: rebindable (see KeyBindings), WASD by default.
        float x = 0f;
        float y = 0f;
        if (KeyBindings.IsPressed(GameAction.MoveRight)) x += 1f;
        if (KeyBindings.IsPressed(GameAction.MoveLeft)) x -= 1f;
        if (KeyBindings.IsPressed(GameAction.MoveUp)) y += 1f;
        if (KeyBindings.IsPressed(GameAction.MoveDown)) y -= 1f;
        moveInput = new Vector2(x, y).normalized;

        // Sprint: held Shift while actually moving, gated on stamina - runs out mid-sprint and it
        // cuts off on its own instead of going negative. Also gated on the Hole debuff.
        isSprinting = KeyBindings.IsPressed(GameAction.Sprint) && moveInput != Vector2.zero && stamina.currentStamina > 0f
            && Time.time >= movementDebuffEndTime;
        if (isSprinting)
        {
            float staminaCostMultiplier = skills != null ? skills.SprintStaminaCostMultiplier : 1f;
            stamina.Drain(sprintStaminaCostPerSecond * staminaCostMultiplier * Time.deltaTime);
            if (skills != null) skills.AddUsage(SkillType.Sprint, Time.deltaTime);

            // Sprinting on a broken leg re-injures it every second instead of just being slower
            // (see HasBrokenLeg/FixedUpdate's -50% speed) - explicit request: running on a broken
            // leg should cost you, not just be sluggish.
            if (HasBrokenLeg && Time.time - lastSprintInjuryTime >= SprintInjuryInterval)
            {
                health.TakeDamage(1);
                lastSprintInjuryTime = Time.time;
            }
        }

        if (KeyBindings.WasPressedThisFrame(GameAction.Roll)) TryRoll();
        if (isRolling) return; // just started rolling this frame - no attack/hotbar until it ends

        // Aiming: still tracked while sprinting (so the facing is already correct the instant
        // sprint stops), but attacking/hotbar are not - can't fight with your weapon out while
        // running, same as the roll's commitment above.
        Vector2 aim = Vector2.zero;
        bool directionPressedThisFrame = false;
        if (KeyBindings.IsPressed(GameAction.AimUp)) { aim = Vector2.up; directionPressedThisFrame = KeyBindings.WasPressedThisFrame(GameAction.AimUp); }
        else if (KeyBindings.IsPressed(GameAction.AimDown)) { aim = Vector2.down; directionPressedThisFrame = KeyBindings.WasPressedThisFrame(GameAction.AimDown); }
        else if (KeyBindings.IsPressed(GameAction.AimLeft)) { aim = Vector2.left; directionPressedThisFrame = KeyBindings.WasPressedThisFrame(GameAction.AimLeft); }
        else if (KeyBindings.IsPressed(GameAction.AimRight)) { aim = Vector2.right; directionPressedThisFrame = KeyBindings.WasPressedThisFrame(GameAction.AimRight); }

        if (aim != Vector2.zero) aimDirection = aim;
        // The combo (see AdvanceMeleeCombo) only continues while a direction key stays held -
        // letting go for even one frame breaks it back to hit 1, regardless of how little time has
        // passed (2026-09-16 request: "uniquement si la touche d'attaque reste enfoncee, sinon pas
        // de combo"). Checked every frame here rather than only when an attack actually fires, so a
        // release mid-cooldown breaks the chain immediately instead of waiting for the next swing
        // to notice.
        else comboCount = 0;

        // A direction press that just threw an item/cast a spell must not ALSO start a melee swing
        // the instant that same key is still held on the next frame (2026-09-19 bug report: "si je
        // lance un projectile ou un sort, je ne dois pas mettre un coup") - armedThrowItemId/
        // armedSpellId only block TryAttack for the one frame they're still non-null, and both are
        // cleared inside ThrowArmedItem/CastArmedSpell before this method returns, so the very next
        // frame's aim != Vector2.zero would otherwise read as a fresh attack input. This remembers
        // which direction was just consumed that way and keeps blocking TryAttack for it specifically
        // until the key is released (aim goes back to zero) or a different direction is pressed.
        if (aim == Vector2.zero) directionConsumedByThrowOrCast = Vector2.zero;

        // Level 10 Sprint (see PlayerSkills.CanAttackWhileSprinting) lifts this - everyone else
        // still can't fight with their weapon out while running.
        if (isSprinting && (skills == null || !skills.CanAttackWhileSprinting)) return;

        if (armedSpellId != null)
        {
            // Same idea as armedThrowItemId just below - the direction press itself is the cast.
            if (directionPressedThisFrame) { CastArmedSpell(); directionConsumedByThrowOrCast = aim; }
        }
        else if (armedThrowItemId != null)
        {
            // The direction press itself is the throw, not a melee swing - TryAttack is skipped
            // entirely while something is armed (see UseItem).
            if (directionPressedThisFrame) { ThrowArmedItem(); directionConsumedByThrowOrCast = aim; }
        }
        else if (aim != Vector2.zero && aim != directionConsumedByThrowOrCast)
        {
            TryAttack();
        }

        // Hotbar: reads whatever the player actually assigned to each slot (drag & drop, feature
        // 2) instead of assuming the starting loadout.
        if (KeyBindings.WasPressedThisFrame(GameAction.Hotbar1)) UseHotbarSlot(0);
        if (KeyBindings.WasPressedThisFrame(GameAction.Hotbar2)) UseHotbarSlot(1);
        if (KeyBindings.WasPressedThisFrame(GameAction.Hotbar3)) UseHotbarSlot(2);
        if (KeyBindings.WasPressedThisFrame(GameAction.Hotbar4)) UseHotbarSlot(3);
        if (KeyBindings.WasPressedThisFrame(GameAction.Hotbar5)) UseHotbarSlot(4);

        // Spell bar: Z/X/C/V/B by default (2026-09-18 request) - arms the slot's spell exactly
        // like clicking it on SpellBarUI does.
        if (KeyBindings.WasPressedThisFrame(GameAction.Spell1)) UseSpellSlot(0);
        if (KeyBindings.WasPressedThisFrame(GameAction.Spell2)) UseSpellSlot(1);
        if (KeyBindings.WasPressedThisFrame(GameAction.Spell3)) UseSpellSlot(2);
        if (KeyBindings.WasPressedThisFrame(GameAction.Spell4)) UseSpellSlot(3);
        if (KeyBindings.WasPressedThisFrame(GameAction.Spell5)) UseSpellSlot(4);
    }

    void FixedUpdate()
    {
        // Highest priority: a knockback pop from just having been hit overrides even the roll/
        // attack-lock roots below (can't actually co-occur with isRolling in practice - rolling
        // grants IsInvulnerable, which blocks OnDamagedFrom from firing at all - but attack-lock
        // has no such protection, and getting popped mid-swing is the point).
        if (!isDead && Time.time < staggerEndTime)
        {
            rb.linearVelocity = staggerVelocity;
            return;
        }

        // Drives the roll itself (and ends it) here rather than in Update, so it still finishes
        // and clears invulnerability even if Update starts bailing out early mid-roll (e.g. a
        // dialogue opens, or the player dies from something invulnerability doesn't block).
        if (isRolling)
        {
            float rollSpeedBonus = skills != null ? skills.RollSpeedBonus : 0f;
            rb.linearVelocity = isDead ? Vector2.zero : rollDirection * rollSpeed * (1f + rollSpeedBonus);
            if (Time.time >= rollEndTime) EndRoll();
            return;
        }

        // Rooted for the attack's own cooldown, whatever movement keys are still held (see
        // TryAttack) - takes priority over every multiplier below, same as isDead/isRolling above.
        // The opening sliver of that root (AttackLungeDuration) gets a forward nudge instead of a
        // flat zero, so a punch/swing reads as a small step-in rather than a standstill jab.
        if (!isDead && Time.time < attackLockEndTime)
        {
            rb.linearVelocity = Time.time < attackLungeEndTime ? attackLungeVelocity : Vector2.zero;
            return;
        }

        float sprintBonus = skills != null ? skills.SprintSpeedBonus : 0f;
        float speedMultiplier = stats.MoveSpeedMultiplier * (isSprinting ? sprintSpeedMultiplier + sprintBonus : 1f);
        // Applies whether sprinting or just walking - a broken leg slows you down outright, on top
        // of sprinting on it also costing health (see Update's sprint block).
        if (HasBrokenLeg) speedMultiplier *= 0.5f;
        if (Time.time < slowEndTime) speedMultiplier *= slowMultiplier;
        rb.linearVelocity = isDead ? Vector2.zero : moveInput * moveSpeed * speedMultiplier;
    }

    void TryRoll()
    {
        float rollCooldownMultiplier = skills != null ? skills.RollCooldownMultiplier : 1f;
        if (Time.time - lastRollTime < rollCooldown * rollCooldownMultiplier) return;
        if (Time.time < movementDebuffEndTime) return;
        float rollStaminaCostMultiplier = skills != null ? skills.RollStaminaCostMultiplier : 1f;
        float actualRollStaminaCost = rollStaminaCost * rollStaminaCostMultiplier;
        if (stamina.currentStamina < actualRollStaminaCost) return;

        // Rolls in the direction the player is currently moving; with no movement input, rolls
        // toward the last direction they aimed/faced (aimDirection is never Vector2.zero).
        rollDirection = moveInput != Vector2.zero ? moveInput : aimDirection;

        stamina.Drain(actualRollStaminaCost);
        if (skills != null) skills.AddUsage(SkillType.Roll, 1f);
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

    // Called by a floor hazard (e.g. BossController's Cerbere slobber puddle, see SlowPuddle) while
    // the player stands in it - takes the strongest active slow and the longer remaining duration,
    // same reasoning as ApplyMovementDebuff below, so re-ticking every physics step while standing
    // in the puddle doesn't let the effect flicker off between ticks.
    public void ApplySlow(float multiplier, float duration)
    {
        if (Time.time >= slowEndTime || multiplier < slowMultiplier) slowMultiplier = multiplier;
        slowEndTime = Mathf.Max(slowEndTime, Time.time + duration);
    }

    const string MovementDebuffIconKey = "MovementDebuff";

    // Called by Hole on entry - takes the longer of the current and new debuff instead of
    // resetting it, so walking across two holes in a row doesn't shorten the first one (and keeps
    // the icon showing for the longer remaining duration too).
    public void ApplyMovementDebuff(float duration)
    {
        // Anti-hole boots (see EquipmentSlotType.Boots/ItemIds.AntiHoleBoots) - immune outright,
        // never even shows the debuff icon.
        if (equipment != null && equipment.Get(EquipmentSlotType.Boots) == ItemIds.AntiHoleBoots) return;

        movementDebuffEndTime = Mathf.Max(movementDebuffEndTime, Time.time + duration);
        if (statusIcons != null && movementDebuffIcon != null)
            statusIcons.ShowIcon(MovementDebuffIconKey, movementDebuffIcon, movementDebuffEndTime - Time.time);
    }

    bool weaponLocked;
    public bool WeaponLocked => weaponLocked;

    public int CurrentWeaponDurability => currentWeaponDurability;
    // SaveManager.Apply only - a plain field restore like weaponHand just below it, not a fresh
    // equip (EquipWeapon/ForceEquipWeapon would reset this back to full instead of the saved value).
    public void SetCurrentWeaponDurability(int value) => currentWeaponDurability = value;

    int MaxDurabilityFor(WeaponType weapon) => weapon switch
    {
        WeaponType.Sword => maxSwordDurability,
        WeaponType.Staff => maxStaffDurability,
        _ => 0,
    };

    // SaveManager.Apply only (the non-cursed restore path) - a plain field restore like
    // SetCurrentWeaponDurability above, not a fresh equip (EquipWeaponItem would reset durability
    // to full instead of the saved, possibly worn-down value already restored on the equipment side).
    public void SetCurrentWeaponItem(string itemId, WeaponType weapon)
    {
        currentWeapon = weapon;
        currentWeaponItemId = itemId;
    }

    // A cursed weapon-item forces itself on and can't be swapped out until UnlockWeapon runs - never
    // goes through the equipment Weapon slot (currentWeaponItemId stays null), the curse item sits
    // in the inventory instead, locked there via PlayerInventory.ApplyCurse/CursedItemId.
    public void ForceEquipWeapon(WeaponType weapon)
    {
        currentWeapon = weapon;
        currentWeaponItemId = null;
        weaponLocked = true;
        currentWeaponDurability = MaxDurabilityFor(weapon);
        Debug.Log("Cursed weapon forced on: " + weapon);
    }

    // Called only from TryAttack's Sword/Staff cases below. A normal item-based weapon (see
    // EquipWeaponItem) delegates to PlayerEquipment.DamageDurability, whose own Set(slot, null) at
    // 0 already cascades back into UnequipToFistIfCurrent below - a forced/cursed weapon (never in
    // the equipment slot to begin with) keeps the old flat counter instead.
    void DamageWeaponDurability()
    {
        if (weaponLocked)
        {
            if (currentWeaponDurability <= 0) return;
            currentWeaponDurability--;
            if (currentWeaponDurability <= 0)
            {
                Debug.Log(currentWeapon + " s'est brise !");
                currentWeapon = WeaponType.Fist;
                weaponLocked = false; // nothing left to force - a broken cursed sword releases its lock too
            }
            return;
        }

        if (equipment != null && !string.IsNullOrEmpty(currentWeaponItemId))
            equipment.DamageDurability(EquipmentSlotType.Weapon, 0, 1);
    }

    // Called from PlayerEquipment.ApplyItemEffects when a real weapon item (Sword/Staff) enters the
    // Weapon slot - drag-and-drop from the inventory, or a fresh pickup swap. A cursed weapon never
    // reaches here (see ForceEquipWeapon).
    public void EquipWeaponItem(string itemId, WeaponType weapon)
    {
        if (weaponLocked) return;
        currentWeapon = weapon;
        currentWeaponItemId = itemId;
        Debug.Log("Equipped item " + itemId + " (" + weapon + ")");
    }

    // Called from PlayerEquipment.RemoveItemEffects when the item currently worn in the Weapon slot
    // leaves it (unequipped back to inventory, swapped for another weapon, or just broke) - the
    // itemId guard means a stale/unrelated call (e.g. a different weapon breaking after this one
    // was already swapped out) can't wrongly reset a weapon that's no longer even the active one.
    public void UnequipToFistIfCurrent(string itemId)
    {
        if (weaponLocked) return;
        if (currentWeaponItemId != itemId) return;
        currentWeapon = WeaponType.Fist;
        currentWeaponItemId = null;
    }

    public void UnlockWeapon()
    {
        weaponLocked = false;
    }

    // Manual only, not automatic - a broken arm doesn't force-switch you, it just makes attacking
    // useless until you either come here yourself or get the arm repaired (see PlayerLimbs).
    void SwitchWeaponHand()
    {
        weaponHand = weaponHand == BodyPart.ArmRight ? BodyPart.ArmLeft : BodyPart.ArmRight;
        Debug.Log("Main d'arme : " + weaponHand);
    }

    void TryAttack()
    {
        // A broken weaponHand can't swing/aim at all, whatever weapon it's holding - see
        // PlayerLimbs/LimbState and SwitchWeaponHand above.
        if (limbs != null && limbs.IsBroken(weaponHand)) return;

        // Attack speed only affects physical weapons (Fist/Sword) - no equivalent bonus was
        // requested for the Staff's magic cooldown. Level 5 Melee (see PlayerSkills) speeds up the
        // same two weapons further on top of Dexterite's own AttackSpeedMultiplier.
        float meleeCooldownMultiplier = skills != null ? skills.MeleeCooldownMultiplier : 1f;
        float cooldown = currentWeapon == WeaponType.Fist ? fistCooldown / stats.AttackSpeedMultiplier * meleeCooldownMultiplier
            : currentWeapon == WeaponType.Sword ? swordCooldown / stats.AttackSpeedMultiplier * meleeCooldownMultiplier
            : staffCooldown;

        if (Time.time - lastAttackTime < cooldown) return;

        // "chaque attaque depense de l'endurance... compense par la force" (2026-09-15 request) -
        // empty stamina blocks the swing outright (same gate Update's sprint check already uses),
        // rather than letting it go through for free or drain below zero.
        if (stamina.currentStamina <= 0f) return;
        float staminaCost = (currentWeapon == WeaponType.Fist ? fistStaminaCost
            : currentWeapon == WeaponType.Sword ? swordStaminaCost : staffStaminaCost) * stats.AttackStaminaCostMultiplier;
        stamina.Drain(staminaCost);

        lastAttackTime = Time.time;
        // "pareil pour le joueur" (2026-09-15 request) - committed to the swing/cast for its own
        // cooldown, same "the attack roots you" rule just added to bosses (see BossController.
        // LockMovement) - can't slide toward/away from a target mid-attack anymore.
        attackLockEndTime = Time.time + cooldown;

        switch (currentWeapon)
        {
            case WeaponType.Fist:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(fistOffset * stats.RangeMultiplier, fistRange * stats.RangeMultiplier, ScaledPhysicalDamage(fistDamage), fistVisualSprite);
                else
                    MeleeAttack(fistOffset * stats.RangeMultiplier, fistRange * stats.RangeMultiplier, ScaledPhysicalDamage(fistDamage), fistVisualSprite);
                break;
            case WeaponType.Sword:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(swordOffset * stats.RangeMultiplier, swordRange * stats.RangeMultiplier, ScaledPhysicalDamage(swordDamage), swordVisualSprite);
                else
                    SwordSlash((swordOffset + swordRange) * stats.RangeMultiplier, ScaledPhysicalDamage(swordDamage), swordVisualSprite);
                DamageWeaponDurability();
                break;
            case WeaponType.Staff:
                comboCount = 0; // ranged/cast - doesn't continue or count toward the melee combo
                LaunchProjectile(projectileSprite, ScaledMagicDamage(staffDamage), projectileSpeed, StaffMaxRange);
                DamageWeaponDurability();
                break;
        }
    }

    // 3rd chained melee swing (Fist or Sword, see ComboWindow) becomes the finisher instead of a
    // normal hit (2026-09-16 request: "3 coups de poing enchaines... 2 coups normaux et un coup
    // qui nous fait dash en avant et tape en aoe"). Resets the chain back to 0 on the finisher
    // itself, so it's always hit 1, 2, 3-finisher, 1, 2, 3-finisher... never a longer run.
    const int ComboFinisherHitNumber = 3;
    const float ComboWindow = 1f;
    const float ComboFinisherDashDistance = 3f;
    const float ComboFinisherDashSpeed = 16f;
    int comboCount;
    float lastMeleeComboTime = -999f;

    bool AdvanceMeleeCombo()
    {
        if (Time.time - lastMeleeComboTime > ComboWindow) comboCount = 0;
        lastMeleeComboTime = Time.time;
        comboCount++;
        bool isFinisher = comboCount >= ComboFinisherHitNumber;
        if (isFinisher) comboCount = 0;
        return isFinisher;
    }

    int ScaledPhysicalDamage(int baseDamage) => Mathf.RoundToInt(baseDamage * stats.PhysicalDamageMultiplier);
    int ScaledMagicDamage(int baseDamage) => Mathf.RoundToInt(baseDamage * stats.MagicDamageMultiplier);

    void UseHotbarSlot(int index)
    {
        string itemId = inventory.hotbarSlots[index];
        if (string.IsNullOrEmpty(itemId)) return;
        UseItem(itemId);
    }

    // Shared by the hotbar (1-5, see UseHotbarSlot) and a direct click on an inventory-grid slot
    // (see InventorySlotUI.OnPointerClick) - a throwable/bomb/potion no longer has to be dragged
    // onto the hotbar first just to be usable at all.
    public void UseItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;

        // Arming an item cancels whatever spell was armed (see ArmSpell) - only one directional
        // action can be pending at a time.
        if (armedSpellId != null)
        {
            armedSpellId = null;
            if (statusIcons != null) statusIcons.HideIcon(ArmedSpellIconKey);
        }

        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition != null && definition.HealAmount > 0)
        {
            UsePotion(itemId, definition.HealAmount);
            return;
        }

        // A throwable (bomb included) no longer fires the instant its hotbar slot is pressed - it
        // arms instead, and the next direction press is what throws it (see Update). Re-selecting
        // the same slot while already armed cancels it.
        if (armedThrowItemId == itemId)
        {
            armedThrowItemId = null;
            if (statusIcons != null) statusIcons.HideIcon(ArmedThrowIconKey);
            return;
        }

        armedThrowItemId = itemId;
        if (statusIcons != null) statusIcons.ShowIcon(ArmedThrowIconKey, definition != null ? definition.Icon : null);
    }

    void ThrowArmedItem()
    {
        string itemId = armedThrowItemId;
        armedThrowItemId = null;
        if (statusIcons != null) statusIcons.HideIcon(ArmedThrowIconKey);

        if (itemId == ItemIds.Bomb) TryThrowBomb();
        else TryThrow(itemId);
    }

    // Z/X/C/V/B (see KeyBindings.Spell1-5) - same shape as UseHotbarSlot, just arming instead of
    // firing straight away since a spell still needs a direction.
    void UseSpellSlot(int index)
    {
        string spellId = spellSlots[index];
        if (!string.IsNullOrEmpty(spellId)) ArmSpell(spellId);
    }

    // Called by SpellBarUI when a slot is clicked - arms the spell so the next direction press
    // (see Update) casts it, same as UseItem does for a throwable. Re-clicking the already-armed
    // slot cancels it.
    public void ArmSpell(string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return;

        if (armedThrowItemId != null)
        {
            armedThrowItemId = null;
            if (statusIcons != null) statusIcons.HideIcon(ArmedThrowIconKey);
        }

        if (armedSpellId == spellId)
        {
            armedSpellId = null;
            if (statusIcons != null) statusIcons.HideIcon(ArmedSpellIconKey);
            return;
        }

        armedSpellId = spellId;
        if (statusIcons != null) statusIcons.ShowIcon(ArmedSpellIconKey, IconForSpell(spellId));
    }

    // Public so SpellBarUI can reuse the exact same spellId->icon mapping instead of duplicating it.
    public Sprite IconForSpell(string spellId) => spellId switch
    {
        SpellIds.LightningOrb => lightningOrbSprite,
        SpellIds.Fireball => fireballSprite,
        SpellIds.FireLine => fireLineSprite,
        _ => null,
    };

    void CastArmedSpell()
    {
        string spellId = armedSpellId;
        armedSpellId = null;
        if (statusIcons != null) statusIcons.HideIcon(ArmedSpellIconKey);

        if (spellId == SpellIds.LightningOrb) TryCastLightningOrb();
        else if (spellId == SpellIds.Fireball) TryCastFireball();
        else if (spellId == SpellIds.FireLine) TryCastFireLine();
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

        int scaledBombDamage = ScaledPhysicalDamage(bombDamage);
        float actualBombSpeed = bombSpeed;
        float actualBombRange = ThrowMaxRange;
        if (skills != null)
        {
            scaledBombDamage = Mathf.RoundToInt(scaledBombDamage * (1f + skills.RangedDamageBonus));
            actualBombSpeed *= skills.RangedSpeedMultiplier;
            actualBombRange *= skills.RangedRangeMultiplier;
            skills.AddUsage(SkillType.Ranged, 1f); // a bomb is a thrown weapon too
        }

        Bomb bomb = go.GetComponent<Bomb>();
        bomb.damage = scaledBombDamage;
        bomb.explosionRadius = bombExplosionRadius;
        bomb.fuseTime = bombFuseTime;
        bomb.explosionSprite = explosionSprite;
        bomb.Launch(direction, actualBombSpeed, actualBombRange);
    }

    // Base direction is the chosen cardinal aim, but the player's current movement blends in:
    // aiming right while walking up-right sends the shot right-and-up, not purely right.
    Vector2 AimWithInertia()
    {
        Vector2 direction = aimDirection + moveInput;
        return direction.sqrMagnitude < 0.01f ? aimDirection : direction.normalized;
    }

    // 10% of max HP, applied when a cursed-weapon swing (see weaponLocked/CurseMissDamage below)
    // connects with nothing at all.
    const float CurseMissDamageFraction = 0.1f;

    // Shared by MeleeAttack and ComboFinisherAttack - skills bonus/crit roll + the cursed weapon's
    // automatic +50% (see weaponLocked below) apply the same way to either.
    int PrepareMeleeDamage(int damage)
    {
        if (skills != null)
        {
            damage = Mathf.RoundToInt(damage * (1f + skills.MeleeDamageBonus));
            if (Random.value < skills.MeleeCritChance) damage *= 2; // level 10 unlock
            skills.AddUsage(SkillType.Melee, 1f);
        }

        // A forced/cursed weapon (see ForceEquipWeapon) always lands a critical hit - the
        // Epee Maudite du Soldat Dechu's curse, "un coup critique automatique (+50% de degats),
        // par contre pour chaque coup qui ne touche pas de cible, le crawler perd 10% de sa vie".
        // A curse isn't purely a downside, and this one isn't described as cursed anywhere the
        // player can read (see ItemInspectManager) - only its effects reveal it.
        if (weaponLocked) damage = Mathf.RoundToInt(damage * 1.5f);
        return damage;
    }

    void MeleeAttack(float offset, float range, int damage, Sprite visualSprite)
    {
        damage = PrepareMeleeDamage(damage);

        attackLungeVelocity = aimDirection * AttackLungeSpeed;
        attackLungeEndTime = Time.time + AttackLungeDuration;

        Vector2 origin = (Vector2)transform.position + aimDirection * offset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, range);
        bool connected = false;
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) { targetHealth.TakeDamage(damage, fromPosition: rb.position); connected = true; }

            DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
            if (destructible != null) { destructible.TryDamage(damage, stats.force); connected = true; }
        }

        if (weaponLocked && !connected && health != null)
            health.TakeDamage(Mathf.CeilToInt(health.maxHealth * CurseMissDamageFraction));

        SpawnAttackVisual(visualSprite, origin, range);
    }

    // Semi-circular sword swing (2026-09-16 request: "un systeme de coup d'epee, semi circulaire,
    // qui prend 0.5 seconde d'animation, avec 0.25 de temps de recharge") - unlike MeleeAttack's
    // single small hit-circle offset in front of the player, this hits everything within `radius`
    // of the player that also falls inside the forward-facing swordArcHalfDegrees*2 arc, so it
    // actually reads as a wide sweep rather than a poke. Hit detection still resolves instantly (same
    // convention as every other attack in this file) - swordSwingDuration only controls the cosmetic
    // sweep (see SpawnSwordSwingVisual), independently of swordCooldown.
    void SwordSlash(float radius, int damage, Sprite visualSprite)
    {
        damage = PrepareMeleeDamage(damage);

        attackLungeVelocity = aimDirection * AttackLungeSpeed;
        attackLungeEndTime = Time.time + AttackLungeDuration;

        Vector2 origin = rb.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius);
        bool connected = false;
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Vector2 toHit = (hit.attachedRigidbody != null ? hit.attachedRigidbody.position : (Vector2)hit.bounds.center) - origin;
            if (toHit.sqrMagnitude > 0.0001f && Vector2.Angle(aimDirection, toHit) > swordArcHalfDegrees) continue;

            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) { targetHealth.TakeDamage(damage, fromPosition: origin); connected = true; }

            DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
            if (destructible != null) { destructible.TryDamage(damage, stats.force); connected = true; }
        }

        if (weaponLocked && !connected && health != null)
            health.TakeDamage(Mathf.CeilToInt(health.maxHealth * CurseMissDamageFraction));

        SpawnSwordSwingVisual(visualSprite, radius);
    }

    void SpawnSwordSwingVisual(Sprite sprite, float radius)
    {
        if (sprite == null) return;

        GameObject go = new GameObject("SwordSwingVisual", typeof(SpriteRenderer), typeof(SwordSwingVisual));
        SwordSwingVisual swing = go.GetComponent<SwordSwingVisual>();
        swing.anchor = transform;
        swing.aimDirection = aimDirection;
        swing.radius = radius;
        swing.halfArcDegrees = swordArcHalfDegrees;
        swing.duration = swordSwingDuration;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
    }

    // 3rd chained melee hit (see AdvanceMeleeCombo) - dashes the player forward along the aim
    // direction instead of striking once in place, hitting every enemy/destructible the dash line
    // crosses (2026-09-16 request). Sampled overlap circles along the line rather than one big hit
    // box, spaced no further apart than `range` so consecutive circles overlap and nothing between
    // sample points is missed; a HashSet keeps each target from being hit twice where two circles
    // overlap the same target.
    void ComboFinisherAttack(float offset, float range, int damage, Sprite visualSprite)
    {
        damage = PrepareMeleeDamage(damage);

        Vector2 startPos = rb.position;
        float dashDistance = ComboFinisherDashDistance * stats.RangeMultiplier;
        Vector2 endPos = startPos + aimDirection * dashDistance;

        var hitHealths = new HashSet<Health>();
        var hitDestructibles = new HashSet<DestructibleObject>();
        int steps = Mathf.Max(1, Mathf.CeilToInt(dashDistance / Mathf.Max(0.1f, range)));
        bool connected = false;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(startPos, endPos, (float)i / steps);
            Collider2D[] hits = Physics2D.OverlapCircleAll(point, range);
            foreach (Collider2D hit in hits)
            {
                if (hit.gameObject == gameObject) continue;
                Health targetHealth = hit.GetComponent<Health>();
                if (targetHealth != null && hitHealths.Add(targetHealth)) { targetHealth.TakeDamage(damage, fromPosition: startPos); connected = true; }

                DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
                if (destructible != null && hitDestructibles.Add(destructible)) { destructible.TryDamage(damage, stats.force); connected = true; }
            }
        }

        if (weaponLocked && !connected && health != null)
            health.TakeDamage(Mathf.CeilToInt(health.maxHealth * CurseMissDamageFraction));

        // Reuses the same attack-lunge velocity override a normal swing's small step already drives
        // in FixedUpdate - just bigger/longer - and extends attackLockEndTime (Mathf.Max, never
        // shrinks it) so ordinary movement input can't cut the dash short partway through. Duration
        // is derived from the ALREADY-scaled dashDistance (not the flat ComboFinisherDashDistance
        // constant) so the physical dash always travels exactly as far as the hit-detection line
        // above - with Portee investment scaling one but not the other, the finisher used to deal
        // damage well past where the player's sprite actually stopped moving (real bug, found
        // 2026-09-16).
        attackLungeVelocity = aimDirection * ComboFinisherDashSpeed;
        attackLungeEndTime = Time.time + dashDistance / ComboFinisherDashSpeed;
        attackLockEndTime = Mathf.Max(attackLockEndTime, attackLungeEndTime);

        SpawnAttackVisual(visualSprite, endPos, range * 1.5f);
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
        if (skills != null)
        {
            damage = Mathf.RoundToInt(damage * (1f + skills.RangedDamageBonus));
            speed *= skills.RangedSpeedMultiplier;
            maxDistance *= skills.RangedRangeMultiplier;
            skills.AddUsage(SkillType.Ranged, 1f);
        }

        Vector2 direction = AimWithInertia();
        Vector2 spawnPos = (Vector2)transform.position + aimDirection * 0.6f;

        GameObject go = ProjectilePool.Get();
        go.transform.position = spawnPos;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        Rigidbody2D projectileBody = go.GetComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;

        CircleCollider2D projectileCollider = go.GetComponent<CircleCollider2D>();
        projectileCollider.radius = 0.15f;

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.IgnoreCollisionWith(bodyCollider);
        projectile.damage = damage;
        projectile.speed = speed;
        projectile.maxDistance = maxDistance;
        projectile.attackerForce = stats.force;
        // A pooled instance may still carry the ignoreTag a PREVIOUS user set (e.g. a boss volley
        // clears it to hit the player) - this spawn path is always the player's own shot/throw, so
        // it always resets back to the default ("Player", never hit yourself) rather than trusting
        // whatever the projectile happened to be configured for last time.
        projectile.ignoreTag = "Player";
        // Same pool-hygiene reasoning as ignoreTag above - a pooled instance previously fired as a
        // lightning orb must not silently keep chaining on its next, completely unrelated shot.
        projectile.chainRadius = 0f;
        projectile.chainBoltSprite = null;
        projectile.Launch(direction);
    }

    void TryCastLightningOrb()
    {
        // Casting still needs a working arm, same gate every other weapon action uses.
        if (limbs != null && limbs.IsBroken(weaponHand)) return;
        if (Time.time - lastLightningOrbTime < lightningOrbCooldown) return;
        if (mana == null || mana.currentMana < lightningOrbManaCost) return;

        mana.Drain(lightningOrbManaCost);
        lastLightningOrbTime = Time.time;
        LaunchLightningOrb();
    }

    void LaunchLightningOrb()
    {
        int damage = ScaledMagicDamage(lightningOrbDamage);
        float speed = lightningOrbSpeed;
        float maxDistance = lightningOrbRange;
        if (skills != null)
        {
            damage = Mathf.RoundToInt(damage * (1f + skills.RangedDamageBonus));
            speed *= skills.RangedSpeedMultiplier;
            maxDistance *= skills.RangedRangeMultiplier;
            skills.AddUsage(SkillType.Ranged, 1f); // thrown like any other ranged attack
        }

        Vector2 direction = AimWithInertia();
        Vector2 spawnPos = (Vector2)transform.position + aimDirection * 0.6f;

        GameObject go = ProjectilePool.Get();
        go.transform.position = spawnPos;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = lightningOrbSprite;
        renderer.sortingOrder = 0;

        Rigidbody2D projectileBody = go.GetComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;

        CircleCollider2D projectileCollider = go.GetComponent<CircleCollider2D>();
        projectileCollider.radius = 0.15f;

        Projectile projectile = go.GetComponent<Projectile>();
        projectile.IgnoreCollisionWith(bodyCollider);
        projectile.damage = damage;
        projectile.speed = speed;
        projectile.maxDistance = maxDistance;
        projectile.attackerForce = stats.force;
        projectile.ignoreTag = "Player";
        projectile.chainRadius = lightningOrbChainRadius;
        projectile.chainBoltSprite = lightningBoltSprite;
        projectile.Launch(direction);
    }

    void TryCastFireball()
    {
        if (limbs != null && limbs.IsBroken(weaponHand)) return;
        if (Time.time - lastFireballTime < fireballCooldown) return;
        if (mana == null || mana.currentMana < fireballManaCost) return;

        mana.Drain(fireballManaCost);
        lastFireballTime = Time.time;
        LaunchFireballCharge();
    }

    // Spawns the growing wind-up (see FireballCharge) instead of a projectile directly - the
    // actual Projectile only gets created once FireballCharge finishes growing.
    void LaunchFireballCharge()
    {
        int damage = ScaledMagicDamage(fireballDamage);
        float speed = fireballSpeed;
        float maxDistance = fireballRange;
        if (skills != null)
        {
            damage = Mathf.RoundToInt(damage * (1f + skills.RangedDamageBonus));
            speed *= skills.RangedSpeedMultiplier;
            maxDistance *= skills.RangedRangeMultiplier;
            skills.AddUsage(SkillType.Ranged, 1f);
        }

        GameObject go = new GameObject("FireballCharge", typeof(SpriteRenderer), typeof(FireballCharge));

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = fireballSprite;
        renderer.sortingOrder = 0;

        FireballCharge charge = go.GetComponent<FireballCharge>();
        charge.chargeDuration = fireballChargeDuration;
        charge.direction = AimWithInertia();
        charge.speed = speed;
        charge.maxDistance = maxDistance;
        charge.damage = damage;
        charge.attackerForce = stats.force;
        charge.ignoreCollider = bodyCollider;
        charge.Begin(transform, aimDirection * 0.6f);
    }

    void TryCastFireLine()
    {
        if (limbs != null && limbs.IsBroken(weaponHand)) return;
        if (Time.time - lastFireLineTime < fireLineCooldown) return;
        if (mana == null || mana.currentMana < fireLineManaCost) return;

        mana.Drain(fireLineManaCost);
        lastFireLineTime = Time.time;
        LaunchFireLine();
    }

    // A row of FireTrail segments laid out from the player toward the aimed direction - each one
    // independently applies/refreshes BurnStatus on anything standing in it (see FireTrail).
    void LaunchFireLine()
    {
        Vector2 direction = AimWithInertia();
        Vector2 origin = (Vector2)transform.position;
        float range = fireLineRange * (skills != null ? skills.RangedRangeMultiplier : 1f);
        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(range / fireLineSegmentSpacing));
        if (skills != null) skills.AddUsage(SkillType.Ranged, 1f);

        for (int i = 1; i <= segmentCount; i++)
        {
            Vector2 pos = origin + direction * (fireLineSegmentSpacing * i);

            GameObject go = new GameObject("FireTrailSegment", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(FireTrail));
            go.transform.position = pos;

            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = fireLineSprite;
            renderer.sortingOrder = -1; // ground decal, under mobs/the player

            CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
            collider.isTrigger = true;
            // Slight overlap between neighboring segments so the line reads as one continuous
            // strip rather than a row of separate gaps.
            collider.radius = fireLineSegmentSpacing * 0.6f;

            FireTrail trail = go.GetComponent<FireTrail>();
            trail.lifetime = fireLineLifetime;
            trail.burnDuration = burnDuration;
            trail.burnDamagePerTick = ScaledMagicDamage(burnDamagePerTick);
            trail.burnTickInterval = burnTickInterval;
            trail.burnIcon = burnIconSprite;
        }
    }

    void HandleDeath()
    {
        isDead = true;
        armedThrowItemId = null;
        if (statusIcons != null) statusIcons.HideIcon(ArmedThrowIconKey);
        armedSpellId = null;
        if (statusIcons != null) statusIcons.HideIcon(ArmedSpellIconKey);
        // A save is only ever a "come back later" convenience - it must never survive death,
        // or a player could just relaunch the game to undo dying (save-scumming).
        SaveManager.DeleteSave();
        Debug.Log("Player died.");
    }
}
