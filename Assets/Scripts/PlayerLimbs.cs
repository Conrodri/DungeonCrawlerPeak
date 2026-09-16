using System;
using System.Collections.Generic;
using UnityEngine;

// Each BodyPart carries its own real HP pool - together they ARE the player's total HP (see
// TotalMaxHealth/TotalCurrentHealth, synced into Health.SetFromLimbs after every change), not a
// small cosmetic layer on top of a separate flat pool. Base pools given explicitly by the user:
// Head 35, Torso 70, each Arm 20, each Leg 30 (205 total, see BaseMaxFor) - Constitution adds +1 to
// EVERY limb per point (so +6 total HP per point), also specified explicitly (see
// SetConstitutionBonus, called from PlayerStats).
//
// Also rolls which BodyPart an enemy/boss attack lands on and mitigates the damage with the armor
// on whichever equipment slot(s) protect that part (see EquipmentSlotType/ItemDefinition.
// ArmorValue). A part reaching 0 goes Broken (see LimbState): normal healing skips it
// (Health.Heal -> HealNonBroken) until repaired outright (RepairAll, currently only from the
// Tavernier's "Se reposer"), and further hits on an already-broken part skip its armor entirely
// (nothing left there to protect it). PlayerController additionally refuses to attack with a
// broken weaponHand, and treats either broken leg as -50% speed + damage while sprinting.
[RequireComponent(typeof(PlayerEquipment))]
[RequireComponent(typeof(Health))]
public class PlayerLimbs : MonoBehaviour
{
    public static int BaseMaxFor(BodyPart part) => part switch
    {
        BodyPart.Head => 35,
        BodyPart.Torso => 70,
        BodyPart.ArmLeft => 20,
        BodyPart.ArmRight => 20,
        BodyPart.LegLeft => 30,
        BodyPart.LegRight => 30,
        _ => 0,
    };

    // CollapsingCeiling = "tete, epaules (torse), bras" - see RollTarget. AttackSource.Random (the
    // catch-all: bosses, Larve, enemy projectiles, generic hazards) rolls fully at random across
    // all 6 parts instead.
    static readonly BodyPart[] UpperBodyParts = { BodyPart.Head, BodyPart.Torso, BodyPart.ArmLeft, BodyPart.ArmRight };
    static readonly BodyPart[] AllParts = (BodyPart[])Enum.GetValues(typeof(BodyPart));

    PlayerEquipment equipment;
    // Populated via a field initializer, NOT Awake (real bug, found 2026-09-16 from an actual
    // "Player died." log firing straight out of PlayerStats.Awake -> SetConstitutionBonus ->
    // SyncHealth on a freshly spawned character): DungeonGenerator's Player GameObject constructor
    // lists PlayerLimbs before PlayerStats specifically so PlayerLimbs.Awake() runs first, but
    // Unity's multi-type `new GameObject(name, params Type[])` overload does NOT actually guarantee
    // Awake() fires in that listed order - PlayerStats.Awake() could still call
    // limbs.SetConstitutionBonus() while these dictionaries were still empty, syncing 0/0 into
    // Health and killing the character before its first frame. A field initializer runs as part of
    // the instance's construction (guaranteed by C#, independent of Unity's Awake scheduling), so
    // by the time ANY component's Awake can even call GetComponent<PlayerLimbs>() on this object,
    // these are already correctly populated - no ordering assumption left to break.
    readonly Dictionary<BodyPart, int> limbHealth = InitialLimbMap();
    // Base + constitutionBonus, same bonus added to every part - never a per-species/per-part
    // scaling, Constitution is flat across the whole body.
    readonly Dictionary<BodyPart, int> limbMaxHealth = InitialLimbMap();
    int constitutionBonus;

    static Dictionary<BodyPart, int> InitialLimbMap()
    {
        var map = new Dictionary<BodyPart, int>();
        foreach (BodyPart part in AllParts) map[part] = BaseMaxFor(part);
        return map;
    }

    // (part hit, damage actually applied after armor) - for a future hit-location UI/log hookup.
    public event Action<BodyPart, int> OnHit;
    // Fired whenever any limb's HP (current or max) changes - InventoryUI's silhouette panel
    // redraws on this instead of polling every frame.
    public event Action OnLimbsChanged;

    void Awake()
    {
        equipment = GetComponent<PlayerEquipment>();
    }

    public int GetMaxLimbHealth(BodyPart part) => limbMaxHealth.TryGetValue(part, out int max) ? max : BaseMaxFor(part);
    public int GetLimbHealth(BodyPart part) => limbHealth.TryGetValue(part, out int hp) ? hp : GetMaxLimbHealth(part);

    public int TotalMaxHealth
    {
        get { int total = 0; foreach (int v in limbMaxHealth.Values) total += v; return total; }
    }

    public int TotalCurrentHealth
    {
        get { int total = 0; foreach (int v in limbHealth.Values) total += v; return total; }
    }

    public LimbState GetState(BodyPart part)
    {
        int hp = GetLimbHealth(part);
        int max = GetMaxLimbHealth(part);
        return hp <= 0 ? LimbState.Broken : hp < max ? LimbState.Damaged : LimbState.Healthy;
    }

    public bool IsBroken(BodyPart part) => GetState(part) == LimbState.Broken;

    // Called by PlayerStats whenever Constitution changes (including once at startup, from
    // Awake) - +1 max HP on EVERY limb per point, so +6 total per point, exactly as specified.
    // Current HP moves by the same delta as max (a gain heals along with the new ceiling, a loss
    // can't drop a limb below 0), mirroring how the old flat Health.maxHealth/currentHealth pair
    // used to move together on a Constitution change.
    public void SetConstitutionBonus(int bonus)
    {
        int delta = bonus - constitutionBonus;
        constitutionBonus = bonus;
        if (delta != 0)
        {
            List<BodyPart> keys = new List<BodyPart>(limbMaxHealth.Keys);
            foreach (BodyPart part in keys)
            {
                limbMaxHealth[part] = Mathf.Max(1, limbMaxHealth[part] + delta);
                limbHealth[part] = Mathf.Clamp(limbHealth[part] + delta, 0, limbMaxHealth[part]);
            }
        }
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    // Raw setter for SaveManager restore only - unlike a hit/heal, this doesn't go through the
    // normal clamped +/- flow, it just replaces the stored value outright (clamped to this limb's
    // CURRENT max, which SaveManager.Apply is careful to set via SetConstitutionBonus first).
    public void SetLimbHealth(BodyPart part, int hp)
    {
        limbHealth[part] = Mathf.Clamp(hp, 0, GetMaxLimbHealth(part));
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    public int MitigateHit(AttackSource source, int amount)
    {
        BodyPart part = RollTarget(source);
        // An already-broken part has nothing left to protect it - the hit goes straight through
        // instead of being looked up against that slot's armor ("toute attaque dessus tant qu'il
        // est casse se repartit sur tout le corps en ignorant les resistances").
        bool broken = IsBroken(part);
        int armor = broken ? 0 : GetArmor(part);
        int mitigated = Mathf.Max(0, amount - armor);

        // Only the armor that actually stood between the hit and the limb wears down - a broken
        // part (armor already ignored above) or bare skin has nothing to degrade.
        if (!broken && armor > 0) DamageArmorDurability(part);

        limbHealth[part] = Mathf.Max(0, GetLimbHealth(part) - mitigated);
        OnHit?.Invoke(part, mitigated);
        SyncHealth();
        OnLimbsChanged?.Invoke();
        return mitigated;
    }

    // Distributes `amount` total HP across every non-broken limb (a broken one gets nothing - "un
    // membre a 0 PV ignore les soins de base"), one point at a time to whichever eligible limb is
    // currently missing the most HP. Keeps a big heal (e.g. the Tavernier's full rest, or any heal
    // amount at all now that limb pools run into the tens/hundreds) from concentrating on a
    // near-full limb while a badly hurt one goes untouched, without needing a fixed split ratio.
    // Called from Health.Heal whenever this component is present.
    public void HealNonBroken(int amount)
    {
        if (amount <= 0) return;
        for (int i = 0; i < amount; i++)
        {
            BodyPart? best = null;
            int bestDeficit = 0;
            foreach (BodyPart part in AllParts)
            {
                int hp = GetLimbHealth(part);
                if (hp <= 0) continue; // broken - skip, doesn't come back from a normal heal
                int deficit = GetMaxLimbHealth(part) - hp;
                if (deficit > bestDeficit) { bestDeficit = deficit; best = part; }
            }
            if (best == null) break; // every eligible limb is already full
            limbHealth[best.Value] = GetLimbHealth(best.Value) + 1;
        }
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    // The one way to actually fix a broken limb - currently only reachable via the Tavernier's
    // "Se reposer" (see DialogueManager.Resolve/DungeonGenerator.SpawnTavernNpc), same place the
    // rest of the player's state gets a full reset. No standalone repair item/price yet.
    public void RepairAll()
    {
        List<BodyPart> keys = new List<BodyPart>(limbMaxHealth.Keys);
        foreach (BodyPart part in keys) limbHealth[part] = limbMaxHealth[part];
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    // Health.Kill's player-side counterpart - a guaranteed lethal hazard zeroes every limb instead
    // of just the overall pool, so the silhouette reads consistently with the death it caused.
    public void KillAll()
    {
        List<BodyPart> keys = new List<BodyPart>(limbHealth.Keys);
        foreach (BodyPart part in keys) limbHealth[part] = 0;
        SyncHealth();
        OnLimbsChanged?.Invoke();
    }

    // Pushes the totals into Health (currentHealth/maxHealth + OnHealthChanged + the death check)
    // - Health.cs stays the single source every other system (HUD, death screen, save) reads from,
    // this component just keeps it truthfully in sync with the real per-limb pools.
    void SyncHealth()
    {
        Health health = GetComponent<Health>();
        if (health != null) health.SetFromLimbs(TotalCurrentHealth, TotalMaxHealth);
    }

    // Every explicit source below comes straight from the 2026-09-14 spec:
    // - ChauveSouris (Rush + Morsure, both) -> always the head. Rush is a pure gap-closing dash
    //   (see EnemyController.canRush) - no zone of its own was given, and the bat's whole identity
    //   was already "always the head", so it shares Morsure's zone rather than inventing a second.
    // - Zombie (Morsure + Griffure, same mechanical contact hit - see EnemyController.TryDamage)
    //   -> weighted 50% Torso / 25% each arm, exact split given by the user, never the head.
    // - BearTrap -> 50/50 either leg.
    // - CollapsingCeiling -> uniform among Head/Torso/ArmLeft/ArmRight (UpperBodyParts).
    // - Random (bosses, Larve, enemy projectiles, unlabeled hazards) -> uniform across all 6.
    static BodyPart RollTarget(AttackSource source)
    {
        switch (source)
        {
            case AttackSource.ChauveSouris:
                return BodyPart.Head;
            case AttackSource.Zombie:
                return RollWeighted((BodyPart.Torso, 0.5f), (BodyPart.ArmLeft, 0.25f), (BodyPart.ArmRight, 0.25f));
            case AttackSource.BearTrap:
                return UnityEngine.Random.value < 0.5f ? BodyPart.LegLeft : BodyPart.LegRight;
            case AttackSource.CollapsingCeiling:
                return UpperBodyParts[UnityEngine.Random.Range(0, UpperBodyParts.Length)];
            default:
                return AllParts[UnityEngine.Random.Range(0, AllParts.Length)];
        }
    }

    static BodyPart RollWeighted(params (BodyPart part, float weight)[] options)
    {
        float total = 0f;
        foreach (var option in options) total += option.weight;
        float roll = UnityEngine.Random.value * total;
        float cumulative = 0f;
        foreach (var option in options)
        {
            cumulative += option.weight;
            if (roll <= cumulative) return option.part;
        }
        return options[options.Length - 1].part;
    }

    // Torso is covered by 3 slots at once (shoulders/belt/neck all sit around the torso), arms
    // share the single Gloves slot (one glove pair, not a left/right slot each) and legs share
    // Boots+Knees the same way - matches PlayerEquipment's existing one-slot-per-type layout,
    // there's no left/right distinction for anything but rings.
    public int GetArmor(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return ArmorOf(EquipmentSlotType.Head);
            case BodyPart.Torso: return ArmorOf(EquipmentSlotType.Shoulders) + ArmorOf(EquipmentSlotType.Belt) + ArmorOf(EquipmentSlotType.Neck);
            case BodyPart.ArmLeft:
            case BodyPart.ArmRight: return ArmorOf(EquipmentSlotType.Gloves);
            case BodyPart.LegLeft:
            case BodyPart.LegRight: return ArmorOf(EquipmentSlotType.Boots) + ArmorOf(EquipmentSlotType.Knees);
            default: return 0;
        }
    }

    int ArmorOf(EquipmentSlotType slot)
    {
        string itemId = equipment.Get(slot);
        if (string.IsNullOrEmpty(itemId)) return 0;
        ItemDefinition definition = ItemDatabase.Get(itemId);
        return definition != null ? definition.ArmorValue : 0;
    }

    // Same body-part -> slot mapping as GetArmor above, 1 point of wear per slot that actually
    // covers the part hit - a hit to the torso wears all 3 of its slots at once, matching how
    // their armor already stacks together on that same hit.
    void DamageArmorDurability(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head:
                equipment.DamageDurability(EquipmentSlotType.Head, 0, 1);
                break;
            case BodyPart.Torso:
                equipment.DamageDurability(EquipmentSlotType.Shoulders, 0, 1);
                equipment.DamageDurability(EquipmentSlotType.Belt, 0, 1);
                equipment.DamageDurability(EquipmentSlotType.Neck, 0, 1);
                break;
            case BodyPart.ArmLeft:
            case BodyPart.ArmRight:
                equipment.DamageDurability(EquipmentSlotType.Gloves, 0, 1);
                break;
            case BodyPart.LegLeft:
            case BodyPart.LegRight:
                equipment.DamageDurability(EquipmentSlotType.Boots, 0, 1);
                equipment.DamageDurability(EquipmentSlotType.Knees, 0, 1);
                break;
        }
    }
}
