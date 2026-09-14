using System;
using System.Collections.Generic;
using UnityEngine;

// Rolls which BodyPart an enemy/boss attack lands on, mitigates the damage with the armor on
// whichever equipment slot(s) protect that part (see EquipmentSlotType/ItemDefinition.ArmorValue),
// and tracks each part's own small HP pool - separate from the player's overall Health, which is
// still the one that actually kills them. A part reaching 0 goes Broken (see LimbState): normal
// healing skips it (Health.Heal -> HealNonBroken) until repaired outright (RepairAll, currently
// only from the Tavernier's "Se reposer" - see DungeonGenerator.SpawnTavernNpc), and further hits
// on an already-broken part skip its armor entirely (nothing left there to protect it).
// PlayerController additionally refuses to attack with a broken weaponHand.
[RequireComponent(typeof(PlayerEquipment))]
public class PlayerLimbs : MonoBehaviour
{
    // Not tied to Constitution or anything else on purpose - a small, flat, easy-to-reason-about
    // pool distinct from the player's real (and much more variable) Health max. Not specified
    // beyond "a limb can break"; this session's own numeric choice.
    public const int MaxLimbHealth = 3;

    // Zombie = "haut du corps" only. ChauveSouris always targets the head (see RollTarget). Every
    // other attacker (Larve, bosses, enemy projectiles) has no documented preference, so it rolls
    // fully at random across all 6 parts.
    static readonly BodyPart[] UpperBodyParts = { BodyPart.Head, BodyPart.Torso, BodyPart.ArmLeft, BodyPart.ArmRight };
    static readonly BodyPart[] AllParts = (BodyPart[])Enum.GetValues(typeof(BodyPart));

    PlayerEquipment equipment;
    readonly Dictionary<BodyPart, int> limbHealth = new Dictionary<BodyPart, int>();

    // (part hit, damage actually applied after armor) - for a future hit-location UI/log hookup.
    public event Action<BodyPart, int> OnHit;
    // Fired whenever any limb's HP changes (hit, heal, or repair) - InventoryUI's silhouette
    // panel redraws on this instead of polling every frame.
    public event Action OnLimbsChanged;

    void Awake()
    {
        equipment = GetComponent<PlayerEquipment>();
        foreach (BodyPart part in AllParts) limbHealth[part] = MaxLimbHealth;
    }

    public int GetLimbHealth(BodyPart part) => limbHealth.TryGetValue(part, out int hp) ? hp : MaxLimbHealth;

    public LimbState GetState(BodyPart part)
    {
        int hp = GetLimbHealth(part);
        return hp <= 0 ? LimbState.Broken : hp < MaxLimbHealth ? LimbState.Damaged : LimbState.Healthy;
    }

    public bool IsBroken(BodyPart part) => GetState(part) == LimbState.Broken;

    // Raw setter for SaveManager restore only - unlike a hit/heal, this doesn't go through the
    // normal clamped +/- flow, it just replaces the stored value outright (still clamped to a
    // valid range so a corrupt/old save can't leave a limb at a nonsense HP).
    public void SetLimbHealth(BodyPart part, int hp)
    {
        limbHealth[part] = Mathf.Clamp(hp, 0, MaxLimbHealth);
        OnLimbsChanged?.Invoke();
    }

    public int MitigateHit(EnemyType? attackerType, int amount)
    {
        BodyPart part = RollTarget(attackerType);
        // An already-broken part has nothing left to protect it - the hit goes straight through
        // instead of being looked up against that slot's armor ("toute attaque dessus tant qu'il
        // est casse se repartit sur tout le corps en ignorant les resistances").
        bool broken = IsBroken(part);
        int armor = broken ? 0 : GetArmor(part);
        int mitigated = Mathf.Max(0, amount - armor);

        limbHealth[part] = Mathf.Max(0, GetLimbHealth(part) - mitigated);
        OnHit?.Invoke(part, mitigated);
        OnLimbsChanged?.Invoke();
        return mitigated;
    }

    // Normal healing (potions, the Tavernier's rest) - restores HP on every part that isn't
    // already at 0, but never brings a broken part back on its own ("un membre a 0 PV ignore les
    // soins de base"). Called from Health.Heal whenever this component is present.
    public void HealNonBroken(int amount)
    {
        if (amount <= 0) return;
        List<BodyPart> keys = new List<BodyPart>(limbHealth.Keys);
        foreach (BodyPart part in keys)
        {
            if (limbHealth[part] <= 0) continue;
            limbHealth[part] = Mathf.Min(MaxLimbHealth, limbHealth[part] + amount);
        }
        OnLimbsChanged?.Invoke();
    }

    // The one way to actually fix a broken limb - currently only reachable via the Tavernier's
    // "Se reposer" (see DialogueManager.Resolve/DungeonGenerator.SpawnTavernNpc), same place the
    // rest of the player's state gets a full reset. No standalone repair item/price yet.
    public void RepairAll()
    {
        List<BodyPart> keys = new List<BodyPart>(limbHealth.Keys);
        foreach (BodyPart part in keys) limbHealth[part] = MaxLimbHealth;
        OnLimbsChanged?.Invoke();
    }

    static BodyPart RollTarget(EnemyType? attackerType)
    {
        if (attackerType == EnemyType.ChauveSouris) return BodyPart.Head;
        if (attackerType == EnemyType.Zombie) return UpperBodyParts[UnityEngine.Random.Range(0, UpperBodyParts.Length)];
        return AllParts[UnityEngine.Random.Range(0, AllParts.Length)];
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
}
