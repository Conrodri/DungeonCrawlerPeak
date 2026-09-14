using System;
using UnityEngine;

// Rolls which BodyPart an enemy/boss attack lands on and mitigates the damage with the armor on
// whichever equipment slot(s) protect that part (see EquipmentSlotType/ItemDefinition.ArmorValue).
// No per-limb HP pool here on purpose - "membres casses" (a limb at 0 HP ignoring basic heals,
// damage redistributing once broken) is a separate later chantier, not built yet.
[RequireComponent(typeof(PlayerEquipment))]
public class PlayerLimbs : MonoBehaviour
{
    // Zombie = "haut du corps" only. ChauveSouris always targets the head (see RollTarget). Every
    // other attacker (Larve, bosses, enemy projectiles) has no documented preference, so it rolls
    // fully at random across all 6 parts.
    static readonly BodyPart[] UpperBodyParts = { BodyPart.Head, BodyPart.Torso, BodyPart.ArmLeft, BodyPart.ArmRight };
    static readonly BodyPart[] AllParts = (BodyPart[])Enum.GetValues(typeof(BodyPart));

    PlayerEquipment equipment;

    // (part hit, damage actually applied after armor) - for a future hit-location UI/log hookup.
    public event Action<BodyPart, int> OnHit;

    void Awake()
    {
        equipment = GetComponent<PlayerEquipment>();
    }

    public int MitigateHit(EnemyType? attackerType, int amount)
    {
        BodyPart part = RollTarget(attackerType);
        int armor = GetArmor(part);
        int mitigated = Mathf.Max(0, amount - armor);
        OnHit?.Invoke(part, mitigated);
        return mitigated;
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
