// Which specific attack or trap is dealing a hit - drives PlayerLimbs.RollTarget's damage-zone
// bias. Spec given explicitly by the user (2026-09-14): every mob attack and every trap has a
// defined zone, not just a per-EnemyType guess. Random is the catch-all for anything not given one
// (bosses, Larve, enemy projectiles, generic/unlabeled environmental hazards).
public enum AttackSource { Random, Zombie, ChauveSouris, BearTrap, CollapsingCeiling }

// EnemyController already carries an EnemyType for other reasons (XP/MonsterLeveling) - this maps
// it to the matching AttackSource instead of duplicating a second field on every enemy.
public static class AttackSourceMapping
{
    public static AttackSource For(EnemyType type) => type switch
    {
        EnemyType.Zombie => AttackSource.Zombie,
        EnemyType.ChauveSouris => AttackSource.ChauveSouris,
        _ => AttackSource.Random, // Larve - no special zone given
    };
}
