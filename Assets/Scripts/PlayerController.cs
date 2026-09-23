using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerController : MonoBehaviour
{
    // 2026-09-22 request: 5 new melee types (Halberd/ShortSword/SpikedGloves/Rapier/Hammer) + 4 new
    // ranged types (Sling/Shuriken/Revolver/Bow) - see IsMeleeWeapon/KnockbackFor and the matching
    // [Header] blocks below for their stats, and TryAttack's switch for how each one actually swings/
    // fires.
    public enum WeaponType { Fist, Sword, Staff, Halberd, ShortSword, SpikedGloves, Rapier, Hammer, Sling, Shuriken, Revolver, Bow, Fouet }

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
    // Set/cleared by BossRoomController's intro cutscene (2026-09-21 request: "personne ne bouge"
    // while the camera pans to the boss) - checked in Update() exactly like DialogueManager.IsOpen/
    // InventoryUI.IsOpen just below, so it needs no special-casing anywhere else: moveInput/
    // isSprinting zero out and FixedUpdate's velocity naturally follows suit.
    public bool cutsceneFrozen;
    // Which inventory item is currently worn in the PlayerEquipment.Weapon slot (see
    // EquipWeaponItem/UnequipToFistIfCurrent) - null for Fist or a curse-forced weapon (see
    // ForceEquipWeapon, which never goes through the equipment slot at all).
    string currentWeaponItemId;
    public Sprite projectileSprite;
    public Sprite fistVisualSprite;
    public Sprite swordVisualSprite;
    // Shown above the player's head via StatusIconDisplay while the Hole debuff is active.
    public Sprite movementDebuffIcon;

    // Every base damage field below rescaled 2026-09-20 ("revois le systeme de degats, normalise
    // le sur les hp du joueur") - these were still at pre-limb-rework numbers (fist 1, sword 2,
    // fireball 5) while the player's own pool grew to 205 total HP across 6 limbs (see
    // PlayerLimbs.BaseMaxFor) and monster HP got rebalanced to match (see DungeonGenerator's
    // assets.enemyPresets/BossTierStatsFor) - relative ratios between weapons/spells kept the same,
    // just scaled up together (~x6-8 melee/ranged, ~x8 the two big spells).
    [Header("Fist")]
    public int fistDamage = 6;
    public float fistRange = 1.0f;
    public float fistOffset = 0.8f;
    public float fistCooldown = 0.33f;
    public float fistStaminaCost = 5f;

    [Header("Sword")]
    public int swordDamage = 14;
    public float swordRange = 0.7f;
    public float swordOffset = 0.9f;
    // Recharge before the NEXT swing can start (2026-09-16 request: originally 0.25, shorter than
    // swordSwingDuration below, so a new swing could start before the previous one's visual
    // finished - corrected same day to 0.75, longer than swordSwingDuration, so each swing's sweep
    // always plays out fully before the next one can begin. 2026-09-21 report: 0.75 roots the
    // player for too long between swings - cut to 0.45, alongside swordSwingDuration below, kept
    // just under it so the "sweep finishes before the next swing" invariant still holds).
    public float swordCooldown = 0.45f;
    // How long the semi-circular swing's visual sweep takes to play out (see SwordSwingVisual) -
    // purely cosmetic, not tied to swordCooldown or attackLockEndTime.
    public float swordSwingDuration = 0.35f;
    // Half-angle of the swing arc on EACH side of aimDirection - 90 = a full semi-circle in front
    // of the player, matching the "semi circulaire" request exactly.
    public float swordArcHalfDegrees = 90f;
    public float swordStaminaCost = 8f;

    // 2026-09-22 request: "hallebardes (plus lent plus d'allonge plus de degats, huge push)" - same
    // arc-sweep shape as Sword (see SwordSlash/swordArcHalfDegrees, reused rather than duplicated),
    // just bigger radius/damage and slower on both ends (cooldown AND the visual sweep itself).
    [Header("Halberd")]
    public int halberdDamage = 26;
    public float halberdRange = 0.9f;
    public float halberdOffset = 1.3f;
    public float halberdCooldown = 0.85f;
    public float halberdSwingDuration = 0.65f;
    public float halberdStaminaCost = 14f;

    // "epee courte (short range, cd rapide, coup rapide low push)" - a poke like Fist (see
    // MeleeAttack), not an arc sweep: fast and precise instead of a wide swing.
    [Header("Short Sword")]
    public int shortSwordDamage = 10;
    public float shortSwordRange = 0.5f;
    public float shortSwordOffset = 0.6f;
    public float shortSwordCooldown = 0.28f;
    public float shortSwordStaminaCost = 6f;

    // "gants a pics (ultra short range, mid push)" - same poke shape as Short Sword, just even
    // tighter reach.
    [Header("Spiked Gloves")]
    public int spikedGlovesDamage = 8;
    public float spikedGlovesRange = 0.6f;
    public float spikedGlovesOffset = 0.5f;
    public float spikedGlovesCooldown = 0.3f;
    public float spikedGlovesStaminaCost = 5f;

    // "arme d'estoc" - a thrusting weapon: long reach but a narrow hit (large offset, small range),
    // still just a poke like Fist/Short Sword/Spiked Gloves above, not a new hit shape.
    [Header("Rapier")]
    public int rapierDamage = 11;
    public float rapierRange = 0.4f;
    public float rapierOffset = 1.1f;
    public float rapierCooldown = 0.35f;
    public float rapierStaminaCost = 7f;

    // "marteau" - heaviest melee weapon: highest damage, slowest cooldown, huge push. Arc sweep
    // like Sword/Halberd, not a poke - a hammer swing reads as a wide blow, not a jab.
    [Header("Hammer")]
    public int hammerDamage = 30;
    public float hammerRange = 0.8f;
    public float hammerOffset = 1.0f;
    public float hammerCooldown = 1.0f;
    public float hammerSwingDuration = 0.75f;
    public float hammerStaminaCost = 16f;

    [Header("Staff")]
    public int staffDamage = 8;
    public float staffCooldown = 0.5f;
    public float projectileSpeed = 8f;
    // The staff isn't hitscan/infinite range: it reaches three sword-lengths out.
    public float staffRangeMultiplier = 3f;
    public float staffStaminaCost = 10f;

    // 2026-09-22 request: 4 physical ranged weapons, all infinite ammo (nothing consumed from the
    // inventory, unlike Shuriken/Caillou/Baton the throwables) - reuse LaunchProjectile exactly like
    // Staff does, just with their own damage/speed/range/push instead of Staff's magic damage.
    [Header("Sling")]
    public int slingDamage = 9;
    public float slingCooldown = 0.45f;
    public float slingProjectileSpeed = 10f;
    public float slingRange = 7f;
    public float slingStaminaCost = 6f;

    // "shuriken munitions infini slow push" - distinct WeaponType/item from the throwable Shuriken
    // (ItemIds.Shuriken, a consumable stack) - this is the equippable launcher, never consumes it.
    [Header("Shuriken Weapon")]
    public int shurikenWeaponDamage = 7;
    public float shurikenWeaponCooldown = 0.3f;
    public float shurikenWeaponProjectileSpeed = 13f;
    public float shurikenWeaponRange = 6f;
    public float shurikenWeaponStaminaCost = 4f;

    // "revolver (6 coups puis recharge 2 sec) munitions infinies huge push" - see revolverAmmo/
    // revolverReloadEndTime below (TryAttack) for the clip/reload state machine.
    [Header("Revolver")]
    public int revolverDamage = 16;
    public float revolverCooldown = 0.35f;
    public float revolverProjectileSpeed = 16f;
    public float revolverRange = 9f;
    public float revolverStaminaCost = 8f;
    public int revolverAmmoCapacity = 6;
    public float revolverReloadDuration = 2f;

    // "arc munitions infinies short push to huge push (arme avec charge possible, le push et les
    // degats dependent de la charge)" - hold the aim direction to charge, release to fire (see
    // UpdateBowCharge/FireBow) rather than firing on press like every other weapon.
    [Header("Bow")]
    public int bowMinDamage = 6;
    public int bowMaxDamage = 22;
    // Lockout after a shot before the NEXT charge can start - not a "rate of fire" in the normal
    // sense since firing itself is gated by charge+release, not this cooldown alone.
    public float bowCooldown = 0.4f;
    public float bowProjectileSpeed = 13f;
    public float bowRange = 10f;
    public float bowStaminaCost = 9f;
    public float bowMaxChargeDuration = 1.2f;

    // "fouet en arme de distance" (2026-09-23 request) - same infinite-ammo LaunchProjectile pattern
    // as Sling/Shuriken/Revolver, just its own damage/speed/range/push tuning (long reach, low push).
    [Header("Fouet")]
    public int fouetDamage = 10;
    public float fouetCooldown = 0.5f;
    public float fouetProjectileSpeed = 15f;
    public float fouetRange = 8f;
    public float fouetStaminaCost = 6f;

    [Header("Throwables")]
    public int throwDamage = 8;
    public float throwSpeed = 10f;
    public float throwCooldown = 0.3f;
    public float throwRangeMultiplier = 2f;

    [Header("Bomb")]
    public Sprite explosionSprite;
    public int bombDamage = 26;
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
    public int lightningOrbDamage = 20;
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
    public int fireballDamage = 40;
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
    public int burnDamagePerTick = 6;
    public float burnTickInterval = 1f;
    public float burnDuration = 5f;
    public Sprite fireLineSprite;
    public Sprite burnIconSprite;

    // A weapon's total reach: how far from the player its hit area extends.
    float SwordReach => (swordOffset + swordRange) * stats.RangeMultiplier;
    float StaffMaxRange => SwordReach * staffRangeMultiplier;
    float ThrowMaxRange => SwordReach * throwRangeMultiplier;

    // Push tiers for the 2026-09-22 weapon-types request (see Health.OnDamagedFrom/EnemyController.
    // HandleDamagedFrom) - a multiplier on the receiving enemy's own base stagger speed, not a flat
    // distance, so it scales naturally with however that base ever gets tuned. KnockbackNormal (1)
    // is every weapon that existed before this request (Fist/Sword/Staff) - unchanged feel.
    const float KnockbackNormal = 1f;
    const float KnockbackLow = 0.6f;
    const float KnockbackMid = 1.3f;
    const float KnockbackHuge = 2.2f;

    static bool IsMeleeWeapon(WeaponType weapon) => weapon == WeaponType.Fist || weapon == WeaponType.Sword
        || weapon == WeaponType.Halberd || weapon == WeaponType.ShortSword || weapon == WeaponType.SpikedGloves
        || weapon == WeaponType.Rapier || weapon == WeaponType.Hammer;

    // Base push tier per weapon (Bow excluded - its push scales continuously with charge, see
    // FireBow, instead of picking one fixed tier).
    static float KnockbackFor(WeaponType weapon) => weapon switch
    {
        WeaponType.Halberd => KnockbackHuge,
        WeaponType.ShortSword => KnockbackLow,
        WeaponType.SpikedGloves => KnockbackMid,
        WeaponType.Rapier => KnockbackLow,
        WeaponType.Hammer => KnockbackHuge,
        WeaponType.Sling => KnockbackMid,
        WeaponType.Shuriken => KnockbackLow,
        WeaponType.Revolver => KnockbackHuge,
        WeaponType.Fouet => KnockbackLow,
        _ => KnockbackNormal,
    };

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
    // Revolver's 6-shot clip (see TryAttack's Revolver case) - starts negative so the very first
    // shot ever fired always finds ammo<=0 with reloadEndTime already in the past and refills
    // straight to full instead of forcing an idle reload wait before the player's first shot.
    int revolverAmmo = -1;
    float revolverReloadEndTime = -999f;
    // Bow's hold-to-charge/release-to-fire state (see UpdateBowCharge/FireBow) - unlike every other
    // weapon, which fires the instant the aim direction is pressed.
    bool isBowCharging;
    float bowChargeStartTime;
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
    // 2026-09-23 request: "poise actif" a la Dark Souls sur les armes lourdes (Halberd/Hammer) -
    // pendant la fenetre de swing, un coup recu inflige toujours ses degats normalement (voir
    // Health.TakeDamage, non affecte) mais ne declenche plus le stagger ci-dessus - le swing va au
    // bout au lieu d'etre interrompu. Set par SwordSlash via son parametre hyperarmorDuration.
    float hyperarmorEndTime = -999f;
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
    // Starts entirely empty - see knownSpells below for why (2026-09-23 fix).
    public readonly string[] spellSlots = new string[SpellBarUI.SlotCount];
    // Which spells this run has actually learned (see LearnSpell/tome items, ItemDefinition.
    // GrantsSpellId) - 2026-09-23 fix: "tu donnes 3 sorts de base au crawler qu'il ne devrait pas
    // avoir". Previously every spell was just hardcoded as known-and-equipped from Awake with no
    // way to NOT have it; now this starts empty and SpellBookUI only lists/allows equipping a spell
    // once its id is in here.
    public readonly HashSet<string> knownSpells = new HashSet<string>();

    public bool KnowsSpell(string spellId) => !string.IsNullOrEmpty(spellId) && knownSpells.Contains(spellId);

    public void LearnSpell(string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return;
        knownSpells.Add(spellId);
    }
    // See ApplySlow (e.g. BossController's Cerbere slobber puddle) - a temporary multiplier on top
    // of the normal speed calc, same "take the strongest, extend the duration" pattern as
    // ApplyMovementDebuff below.
    float slowMultiplier = 1f;
    float slowEndTime = -999f;
    // Potion de Vitesse (2026-09-21 request) - same "strongest wins, duration extends" pattern as
    // slowMultiplier above, just multiplying speed UP instead of down. See ApplyHaste/UsePotionItem.
    float hasteMultiplier = 1f;
    float hasteEndTime = -999f;
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
    bool HasBrokenArm => limbs != null && (limbs.IsBroken(BodyPart.ArmLeft) || limbs.IsBroken(BodyPart.ArmRight));
    // 2026-09-21 request: a broken weaponHand no longer blocks attacks/casts outright - it just
    // halves their speed (double cooldown) instead. Checked against weaponHand specifically (not
    // HasBrokenArm above), same as every gate that used to hard-block here.
    float WeaponHandCooldownMultiplier => limbs != null && limbs.IsBroken(weaponHand) ? 2f : 1f;

    // 2026-09-21 request: "rajoute des debuff pour afficher une jambe cassee un bras casse" -
    // same shared Cross_Bright icon for both (see EnemyController's identical pattern for
    // monsters), told apart by tint since there's no dedicated limb-icon art (see IconPack).
    const string BrokenLegIconKey = "BrokenLeg";
    const string BrokenArmIconKey = "BrokenArm";
    static readonly Color BrokenLegTint = new Color(1f, 0.55f, 0.15f);
    static readonly Color BrokenArmTint = new Color(0.9f, 0.85f, 0.2f);

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
        if (limbs != null)
        {
            limbs.OnLimbsChanged += UpdateLimbDebuffIcons;
            UpdateLimbDebuffIcons(); // covers a restored save that loads in with a limb already broken
        }

    }

    void UpdateLimbDebuffIcons()
    {
        if (statusIcons == null) return;
        Sprite crossIcon = IconPack.Get("Cross_Bright");
        if (crossIcon == null) return;

        if (HasBrokenLeg) statusIcons.ShowIcon(BrokenLegIconKey, crossIcon, tint: BrokenLegTint);
        else statusIcons.HideIcon(BrokenLegIconKey);

        if (HasBrokenArm) statusIcons.ShowIcon(BrokenArmIconKey, crossIcon, tint: BrokenArmTint);
        else statusIcons.HideIcon(BrokenArmIconKey);
    }

    // knockback (2nd param) is only meaningful for the player's own outgoing weapon hits (see
    // EnemyController.HandleDamagedFrom) - whatever hit the PLAYER never sets a push tier, so it's
    // ignored here; the player's own incoming stagger stays the fixed pop it always was.
    void HandleDamagedFrom(Vector2 fromPosition, float knockback)
    {
        if (Time.time - lastStaggerTime < StaggerCooldown) return;
        if (Time.time < hyperarmorEndTime) return; // poise actif - le swing en cours absorbe le stagger

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

        if (DialogueManager.IsOpen || InventoryUI.IsOpen || cutsceneFrozen)
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

        // The combo (see AdvanceMeleeCombo) only continues while a direction key stays held, give
        // or take a short ComboReleaseGrace (2026-09-16 request: "uniquement si la touche
        // d'attaque reste enfoncee, sinon pas de combo" - 2026-09-23 follow-up: that instant break
        // on a single dropped frame read as too finicky, so a brief release is now tolerated
        // instead of resetting immediately). Checked every frame here rather than only when an
        // attack actually fires, so a release mid-cooldown still breaks the chain once the grace
        // window elapses, instead of waiting for the next swing to notice.
        if (aim != Vector2.zero)
        {
            aimDirection = aim;
            aimReleasedTime = -1f;
        }
        else
        {
            if (aimReleasedTime < 0f) aimReleasedTime = Time.time;
            if (Time.time - aimReleasedTime > ComboReleaseGrace) comboCount = 0;
        }

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
        else if (currentWeapon == WeaponType.Bow)
        {
            // Bow doesn't fire on press like every other weapon - holding the aim direction charges
            // the shot, releasing it fires (see UpdateBowCharge/FireBow, 2026-09-22 request: "arme
            // avec charge possible, le push et les degats dependent de la charge").
            UpdateBowCharge(aim);
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
        if (Time.time < hasteEndTime) speedMultiplier *= hasteMultiplier;
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

    const string SpeedBuffIconKey = "SpeedBuff";
    public Sprite speedBuffIcon;
    const string AdrenalineBuffIconKey = "AdrenalineBuff";
    public Sprite adrenalineBuffIcon;

    // Potion de Vitesse (2026-09-21 request) - mirror of ApplySlow above, just multiplying UP:
    // takes the strongest active haste and the longer remaining duration.
    public void ApplyHaste(float multiplier, float duration)
    {
        if (Time.time >= hasteEndTime || multiplier > hasteMultiplier) hasteMultiplier = multiplier;
        hasteEndTime = Mathf.Max(hasteEndTime, Time.time + duration);
        if (statusIcons != null && speedBuffIcon != null)
            statusIcons.ShowIcon(SpeedBuffIconKey, speedBuffIcon, hasteEndTime - Time.time);
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

    // SaveManager.Apply only (the non-cursed restore path) - a plain field restore like weaponHand
    // above, not a fresh equip (EquipWeaponItem has side effects like resetting isBowCharging that a
    // pure restore shouldn't trigger).
    public void SetCurrentWeaponItem(string itemId, WeaponType weapon)
    {
        currentWeapon = weapon;
        currentWeaponItemId = itemId;
    }

    // A cursed weapon-item forces itself on and can't be swapped out until UnlockWeapon runs - never
    // goes through the equipment Weapon slot (currentWeaponItemId stays null), the curse item sits
    // in the inventory instead, locked there via PlayerInventory.ApplyCurse/CursedItemId. Weapons
    // don't wear down (2026-09-22: durability removed), so the only way out is the NPC dialogue
    // option that lifts the curse (see DialogueManager's removesCursedItem outcome -> UnlockWeapon).
    public void ForceEquipWeapon(WeaponType weapon)
    {
        currentWeapon = weapon;
        currentWeaponItemId = null;
        weaponLocked = true;
        Debug.Log("Cursed weapon forced on: " + weapon);
    }

    // Called from PlayerEquipment.ApplyItemEffects when a real weapon item (Sword/Staff) enters the
    // Weapon slot - drag-and-drop from the inventory, or a fresh pickup swap. A cursed weapon never
    // reaches here (see ForceEquipWeapon).
    public void EquipWeaponItem(string itemId, WeaponType weapon)
    {
        if (weaponLocked) return;
        currentWeapon = weapon;
        currentWeaponItemId = itemId;
        isBowCharging = false; // switching weapons mid-charge shouldn't leave a stale draw armed
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
        isBowCharging = false;
    }

    public void UnlockWeapon()
    {
        weaponLocked = false;
    }

    // Manual only, not automatic - a broken arm doesn't force-switch you, it just halves your
    // attack/cast speed until you either come here yourself or get the arm repaired (see
    // PlayerLimbs, WeaponHandCooldownMultiplier).
    void SwitchWeaponHand()
    {
        weaponHand = weaponHand == BodyPart.ArmRight ? BodyPart.ArmLeft : BodyPart.ArmRight;
        Debug.Log("Main d'arme : " + weaponHand);
    }

    // Base (pre-multiplier) cooldown/stamina cost for every weapon - Bow excluded, since it never
    // reaches this method (see UpdateBowCharge/FireBow, dispatched separately from Update()).
    float BaseCooldownFor(WeaponType weapon) => weapon switch
    {
        WeaponType.Fist => fistCooldown,
        WeaponType.Sword => swordCooldown,
        WeaponType.Halberd => halberdCooldown,
        WeaponType.ShortSword => shortSwordCooldown,
        WeaponType.SpikedGloves => spikedGlovesCooldown,
        WeaponType.Rapier => rapierCooldown,
        WeaponType.Hammer => hammerCooldown,
        WeaponType.Staff => staffCooldown,
        WeaponType.Sling => slingCooldown,
        WeaponType.Shuriken => shurikenWeaponCooldown,
        WeaponType.Revolver => revolverCooldown,
        WeaponType.Fouet => fouetCooldown,
        _ => fistCooldown,
    };

    float BaseStaminaCostFor(WeaponType weapon) => weapon switch
    {
        WeaponType.Fist => fistStaminaCost,
        WeaponType.Sword => swordStaminaCost,
        WeaponType.Halberd => halberdStaminaCost,
        WeaponType.ShortSword => shortSwordStaminaCost,
        WeaponType.SpikedGloves => spikedGlovesStaminaCost,
        WeaponType.Rapier => rapierStaminaCost,
        WeaponType.Hammer => hammerStaminaCost,
        WeaponType.Staff => staffStaminaCost,
        WeaponType.Sling => slingStaminaCost,
        WeaponType.Shuriken => shurikenWeaponStaminaCost,
        WeaponType.Revolver => revolverStaminaCost,
        WeaponType.Fouet => fouetStaminaCost,
        _ => fistStaminaCost,
    };

    void TryAttack()
    {
        // Revolver's clip (2026-09-22 request: "6 coups puis recharge 2 sec") - checked before the
        // shared cooldown/stamina gate below so an empty clip neither drains stamina nor locks the
        // attack while just waiting out the reload. revolverAmmo starts at -1 (see field comment) so
        // the very first shot ever fired always finds this "empty" and refills straight to full.
        if (currentWeapon == WeaponType.Revolver && revolverAmmo <= 0)
        {
            if (revolverAmmo == 0 && Time.time < revolverReloadEndTime) return;
            revolverAmmo = revolverAmmoCapacity;
        }

        // Attack speed only affects melee weapons (Fist/Sword/Halberd/Short Sword/Spiked Gloves/
        // Rapier/Hammer) - no equivalent bonus was requested for Staff's magic cooldown or the 4
        // physical ranged weapons added 2026-09-22 (Sling/Shuriken/Revolver), which stay outside it
        // for the same reason Staff always has. Level 5 Melee (see PlayerSkills) speeds up the same
        // melee weapons further on top of Dexterite's own AttackSpeedMultiplier.
        bool isMelee = IsMeleeWeapon(currentWeapon);
        float meleeCooldownMultiplier = skills != null ? skills.MeleeCooldownMultiplier : 1f;
        float baseCooldown = BaseCooldownFor(currentWeapon);
        float cooldown = (isMelee ? baseCooldown / stats.AttackSpeedMultiplier * meleeCooldownMultiplier : baseCooldown) * WeaponHandCooldownMultiplier;

        if (Time.time - lastAttackTime < cooldown) return;

        // "chaque attaque depense de l'endurance... compense par la force" (2026-09-15 request) -
        // empty stamina blocks the swing outright (same gate Update's sprint check already uses),
        // rather than letting it go through for free or drain below zero.
        if (stamina.currentStamina <= 0f) return;
        float staminaCost = BaseStaminaCostFor(currentWeapon) * stats.AttackStaminaCostMultiplier;
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
                    ComboFinisherAttack(fistOffset * stats.RangeMultiplier, fistRange * stats.RangeMultiplier, ScaledPhysicalDamage(fistDamage), fistVisualSprite, KnockbackNormal);
                else
                    MeleeAttack(fistOffset * stats.RangeMultiplier, fistRange * stats.RangeMultiplier, ScaledPhysicalDamage(fistDamage), fistVisualSprite, KnockbackNormal);
                break;
            case WeaponType.Sword:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(swordOffset * stats.RangeMultiplier, swordRange * stats.RangeMultiplier, ScaledPhysicalDamage(swordDamage), swordVisualSprite, KnockbackNormal);
                else
                    SwordSlash((swordOffset + swordRange) * stats.RangeMultiplier, ScaledPhysicalDamage(swordDamage), swordVisualSprite, swordArcHalfDegrees, swordSwingDuration, KnockbackNormal);
                break;
            case WeaponType.Halberd:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(halberdOffset * stats.RangeMultiplier, halberdRange * stats.RangeMultiplier, ScaledPhysicalDamage(halberdDamage), swordVisualSprite, KnockbackHuge);
                else
                    SwordSlash((halberdOffset + halberdRange) * stats.RangeMultiplier, ScaledPhysicalDamage(halberdDamage), swordVisualSprite, swordArcHalfDegrees, halberdSwingDuration, KnockbackHuge, hyperarmorDuration: halberdSwingDuration);
                break;
            case WeaponType.Hammer:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(hammerOffset * stats.RangeMultiplier, hammerRange * stats.RangeMultiplier, ScaledPhysicalDamage(hammerDamage), swordVisualSprite, KnockbackHuge);
                else
                    SwordSlash((hammerOffset + hammerRange) * stats.RangeMultiplier, ScaledPhysicalDamage(hammerDamage), swordVisualSprite, swordArcHalfDegrees, hammerSwingDuration, KnockbackHuge, hyperarmorDuration: hammerSwingDuration);
                break;
            case WeaponType.ShortSword:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(shortSwordOffset * stats.RangeMultiplier, shortSwordRange * stats.RangeMultiplier, ScaledPhysicalDamage(shortSwordDamage), fistVisualSprite, KnockbackLow);
                else
                    MeleeAttack(shortSwordOffset * stats.RangeMultiplier, shortSwordRange * stats.RangeMultiplier, ScaledPhysicalDamage(shortSwordDamage), fistVisualSprite, KnockbackLow);
                break;
            case WeaponType.SpikedGloves:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(spikedGlovesOffset * stats.RangeMultiplier, spikedGlovesRange * stats.RangeMultiplier, ScaledPhysicalDamage(spikedGlovesDamage), fistVisualSprite, KnockbackMid);
                else
                    MeleeAttack(spikedGlovesOffset * stats.RangeMultiplier, spikedGlovesRange * stats.RangeMultiplier, ScaledPhysicalDamage(spikedGlovesDamage), fistVisualSprite, KnockbackMid);
                break;
            case WeaponType.Rapier:
                if (AdvanceMeleeCombo())
                    ComboFinisherAttack(rapierOffset * stats.RangeMultiplier, rapierRange * stats.RangeMultiplier, ScaledPhysicalDamage(rapierDamage), fistVisualSprite, KnockbackLow);
                else
                    MeleeAttack(rapierOffset * stats.RangeMultiplier, rapierRange * stats.RangeMultiplier, ScaledPhysicalDamage(rapierDamage), fistVisualSprite, KnockbackLow);
                break;
            case WeaponType.Staff:
                comboCount = 0; // ranged/cast - doesn't continue or count toward the melee combo
                LaunchProjectile(projectileSprite, ScaledMagicDamage(staffDamage), projectileSpeed, StaffMaxRange);
                break;
            case WeaponType.Sling:
                comboCount = 0;
                LaunchProjectile(projectileSprite, ScaledPhysicalDamage(slingDamage), slingProjectileSpeed, slingRange * stats.RangeMultiplier, KnockbackMid);
                break;
            case WeaponType.Shuriken:
                comboCount = 0;
                LaunchProjectile(projectileSprite, ScaledPhysicalDamage(shurikenWeaponDamage), shurikenWeaponProjectileSpeed, shurikenWeaponRange * stats.RangeMultiplier, KnockbackLow);
                break;
            case WeaponType.Revolver:
                comboCount = 0;
                revolverAmmo--;
                if (revolverAmmo <= 0) revolverReloadEndTime = Time.time + revolverReloadDuration;
                LaunchProjectile(projectileSprite, ScaledPhysicalDamage(revolverDamage), revolverProjectileSpeed, revolverRange * stats.RangeMultiplier, KnockbackHuge);
                break;
            case WeaponType.Fouet:
                comboCount = 0;
                LaunchProjectile(projectileSprite, ScaledPhysicalDamage(fouetDamage), fouetProjectileSpeed, fouetRange * stats.RangeMultiplier, KnockbackLow);
                break;
        }
    }

    // Bow never calls TryAttack (see Update's dispatch) - holding the aim direction charges instead
    // of firing immediately, releasing it fires via FireBow below with damage/push scaled by however
    // long it was held (2026-09-22 request).
    void UpdateBowCharge(Vector2 aim)
    {
        if (aim != Vector2.zero)
        {
            if (!isBowCharging && Time.time - lastAttackTime >= bowCooldown * WeaponHandCooldownMultiplier && stamina.currentStamina > 0f)
            {
                isBowCharging = true;
                bowChargeStartTime = Time.time;
            }
            return;
        }

        if (isBowCharging)
        {
            isBowCharging = false;
            FireBow();
        }
    }

    void FireBow()
    {
        float chargeFraction = Mathf.Clamp01((Time.time - bowChargeStartTime) / bowMaxChargeDuration);
        float cooldown = bowCooldown * WeaponHandCooldownMultiplier;
        lastAttackTime = Time.time;
        attackLockEndTime = Time.time + cooldown;

        stamina.Drain(bowStaminaCost * stats.AttackStaminaCostMultiplier);

        int damage = Mathf.RoundToInt(Mathf.Lerp(bowMinDamage, bowMaxDamage, chargeFraction));
        float knockback = Mathf.Lerp(KnockbackLow, KnockbackHuge, chargeFraction);
        comboCount = 0;
        LaunchProjectile(projectileSprite, ScaledPhysicalDamage(damage), bowProjectileSpeed, bowRange * stats.RangeMultiplier, knockback);
    }

    // 3rd chained melee swing (Fist or Sword, see ComboWindow) becomes the finisher instead of a
    // normal hit (2026-09-16 request: "3 coups de poing enchaines... 2 coups normaux et un coup
    // qui nous fait dash en avant et tape en aoe"). Resets the chain back to 0 on the finisher
    // itself, so it's always hit 1, 2, 3-finisher, 1, 2, 3-finisher... never a longer run.
    const int ComboFinisherHitNumber = 3;
    const float ComboWindow = 1f;
    const float ComboFinisherDashDistance = 3f;
    const float ComboFinisherDashSpeed = 16f;
    // 2026-09-23 request: the "release breaks the combo instantly" rule (see Update, aim ==
    // Vector2.zero branch) read as too finicky/heavy - a single dropped frame on the key shouldn't
    // undo a combo the ComboWindow above would otherwise still allow. This grace period tolerates
    // a brief release without touching the original "you must stay engaged" intent.
    const float ComboReleaseGrace = 0.12f;
    int comboCount;
    float lastMeleeComboTime = -999f;
    float aimReleasedTime = -999f;

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
        if (definition != null && definition.IsSpellTome)
        {
            UseTomeItem(itemId, definition);
            return;
        }
        if (definition != null && definition.IsPotion)
        {
            UsePotionItem(itemId, definition);
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

    // Reads a tome (see ItemDefinition.GrantsSpellId) - learns the spell for good and consumes the
    // book, same "no-op if already known" spirit as picking up a duplicate ring. No cooldown gate
    // like UsePotionItem's throwCooldown - reading a book isn't a combat action.
    void UseTomeItem(string itemId, ItemDefinition definition)
    {
        if (!inventory.TryConsume(itemId)) return;
        LearnSpell(definition.GrantsSpellId);
    }

    // Applies whichever effects this potion actually has (see ItemDefinition.IsPotion) - a plain
    // heal, a timed speed buff, a timed stamina-regen buff, or several at once, nothing stops a
    // future potion combining them.
    void UsePotionItem(string itemId, ItemDefinition definition)
    {
        float cooldown = throwCooldown / stats.AttackSpeedMultiplier;
        if (Time.time - lastThrowTime < cooldown) return;
        if (!inventory.TryConsume(itemId)) return;

        lastThrowTime = Time.time;
        if (definition.HealAmount > 0) health.Heal(definition.HealAmount);
        if (definition.SpeedBuffMultiplier > 0f) ApplyHaste(definition.SpeedBuffMultiplier, definition.SpeedBuffDuration);
        if (definition.StaminaRegenBuffMultiplier > 0f)
        {
            stamina.ApplyRegenBuff(definition.StaminaRegenBuffMultiplier, definition.StaminaRegenBuffDuration);
            if (statusIcons != null && adrenalineBuffIcon != null)
                statusIcons.ShowIcon(AdrenalineBuffIconKey, adrenalineBuffIcon, stamina.RegenBuffEndTime - Time.time);
        }
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

    void MeleeAttack(float offset, float range, int damage, Sprite visualSprite, float knockback = KnockbackNormal)
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
            if (targetHealth != null) { targetHealth.TakeDamage(damage, fromPosition: rb.position, knockback: knockback); connected = true; }

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
    // arcHalfDegrees/swingDuration are per-caller now (2026-09-22: Halberd/Hammer reuse this same
    // arc-sweep shape with their own numbers instead of duplicating the method) - Sword's own call
    // site passes swordArcHalfDegrees/swordSwingDuration, unchanged from before.
    void SwordSlash(float radius, int damage, Sprite visualSprite, float arcHalfDegrees, float swingDuration, float knockback = KnockbackNormal, float hyperarmorDuration = 0f)
    {
        damage = PrepareMeleeDamage(damage);

        attackLungeVelocity = aimDirection * AttackLungeSpeed;
        attackLungeEndTime = Time.time + AttackLungeDuration;
        if (hyperarmorDuration > 0f) hyperarmorEndTime = Time.time + hyperarmorDuration;

        Vector2 origin = rb.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius);
        bool connected = false;
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Vector2 toHit = (hit.attachedRigidbody != null ? hit.attachedRigidbody.position : (Vector2)hit.bounds.center) - origin;
            if (toHit.sqrMagnitude > 0.0001f && Vector2.Angle(aimDirection, toHit) > arcHalfDegrees) continue;

            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) { targetHealth.TakeDamage(damage, fromPosition: origin, knockback: knockback); connected = true; }

            DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
            if (destructible != null) { destructible.TryDamage(damage, stats.force); connected = true; }
        }

        if (weaponLocked && !connected && health != null)
            health.TakeDamage(Mathf.CeilToInt(health.maxHealth * CurseMissDamageFraction));

        SpawnSwordSwingVisual(visualSprite, radius, arcHalfDegrees, swingDuration);
    }

    void SpawnSwordSwingVisual(Sprite sprite, float radius, float arcHalfDegrees, float swingDuration)
    {
        if (sprite == null) return;

        GameObject go = new GameObject("SwordSwingVisual", typeof(SpriteRenderer), typeof(SwordSwingVisual));
        SwordSwingVisual swing = go.GetComponent<SwordSwingVisual>();
        swing.anchor = transform;
        swing.aimDirection = aimDirection;
        swing.radius = radius;
        swing.halfArcDegrees = arcHalfDegrees;
        swing.duration = swingDuration;

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
    void ComboFinisherAttack(float offset, float range, int damage, Sprite visualSprite, float knockback = KnockbackNormal)
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
                if (targetHealth != null && hitHealths.Add(targetHealth)) { targetHealth.TakeDamage(damage, fromPosition: startPos, knockback: knockback); connected = true; }

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

    void LaunchProjectile(Sprite sprite, int damage, float speed, float maxDistance, float knockback = KnockbackNormal)
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
        // Push tier for this shot (2026-09-22 request) - explicit every launch, same pool-hygiene
        // reasoning as chainRadius/ignoreTag above (defaults to KnockbackNormal for Staff, unchanged).
        projectile.knockback = knockback;
        projectile.Launch(direction);
    }

    void TryCastLightningOrb()
    {
        if (Time.time - lastLightningOrbTime < lightningOrbCooldown * WeaponHandCooldownMultiplier) return;
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
        // Pool hygiene, same reasoning as chainRadius above - a pooled instance previously fired by
        // one of the player's ranged weapons (Sling/Shuriken/Revolver/Bow) could otherwise leak its
        // push tier onto this spell (2026-09-22).
        projectile.knockback = 1f;
        projectile.Launch(direction);
    }

    void TryCastFireball()
    {
        if (Time.time - lastFireballTime < fireballCooldown * WeaponHandCooldownMultiplier) return;
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
        if (Time.time - lastFireLineTime < fireLineCooldown * WeaponHandCooldownMultiplier) return;
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
