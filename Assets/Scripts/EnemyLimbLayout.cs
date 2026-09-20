// Per-species body layout for EnemyLimbs (2026-09-20 request: "chaque monstre a le meme systeme
// de membre que le joueur, parfois different pour les slimes ou les mobs avec plus ou moins de
// membres"). Reuses the player's own 6-part BodyPart enum rather than inventing new part types -
// a creature with fewer limbs than the player just gets a subset of it.
public static class EnemyLimbLayout
{
    static readonly BodyPart[] Humanoid = { BodyPart.Head, BodyPart.Torso, BodyPart.ArmLeft, BodyPart.ArmRight, BodyPart.LegLeft, BodyPart.LegRight };
    // ChauveSouris flies and never touches the ground - no legs to target, ArmLeft/ArmRight stand
    // in for its wings.
    static readonly BodyPart[] Flyer = { BodyPart.Head, BodyPart.Torso, BodyPart.ArmLeft, BodyPart.ArmRight };
    // Larve - a single-part blob, the "slime" case from the request: no separate limbs to target,
    // every hit lands on its one Torso pool.
    static readonly BodyPart[] Blob = { BodyPart.Torso };

    public static BodyPart[] For(EnemyType type) => type switch
    {
        EnemyType.Zombie => Humanoid,
        EnemyType.ChauveSouris => Flyer,
        _ => Blob, // Larve
    };
}
