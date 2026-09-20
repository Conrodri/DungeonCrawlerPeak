// Shared contract between PlayerLimbs and EnemyLimbs - lets Health.cs apply/heal/kill through
// whichever one is actually present on this GameObject without needing to know which concrete
// type it is (see Health.Limbs). PlayerLimbs additionally applies armor and a weighted hit-zone
// roll by AttackSource; EnemyLimbs has neither (no equipment, no per-attack zone bias) but still
// distributes the hit across its own BodyPart layout the same way.
public interface ILimbs
{
    int TotalCurrentHealth { get; }
    int TotalMaxHealth { get; }
    int MitigateHit(AttackSource source, int amount);
    void HealNonBroken(int amount);
    void KillAll();
}
