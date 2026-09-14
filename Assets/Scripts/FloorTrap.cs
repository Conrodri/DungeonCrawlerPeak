using UnityEngine;

// Hidden floor hazard - looks like any other floor decal, damages once on first contact then goes
// inert (visually indistinguishable from a spent FuelPuddle). Never visually distinguishable by
// TrapType either (see DungeonGenerator.SpawnFloorTrap, which rolls it) - only the damage zone
// differs (2026-09-14 spec: "un piege a ours attaquera forcement une des deux jambes, un plafond
// qui s'effondre touchera forcement la tete, les epaules et les bras").
public class FloorTrap : MonoBehaviour
{
    public int damage = 2;
    public TrapType trapType;

    bool triggered;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered || !other.CompareTag("Player")) return;
        triggered = true;

        Health health = other.GetComponent<Health>();
        if (health != null) health.TakeDamage(damage, AttackSourceFor(trapType));
    }

    static AttackSource AttackSourceFor(TrapType type) => type switch
    {
        TrapType.BearTrap => AttackSource.BearTrap,
        TrapType.CollapsingCeiling => AttackSource.CollapsingCeiling,
        _ => AttackSource.Random,
    };
}
