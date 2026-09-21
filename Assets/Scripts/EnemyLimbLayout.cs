// Per-species body layout for EnemyLimbs (2026-09-20 request: "chaque monstre a le meme systeme
// de membre que le joueur, parfois different pour les slimes ou les mobs avec plus ou moins de
// membres"). Reuses the player's own 6-part BodyPart enum rather than inventing new part types -
// a creature with fewer limbs than the player just gets a subset of it.
//
// Part COUNT was retuned 2026-09-21 ("la vie de certains monstres sont beaucoup trop hautes...
// une chauve souris devrait mourrir en 3 coups sans arme, 1 ou 2 coup d'epee maximum") - death
// only happens once EVERY part in a creature's layout is separately brought to 0 (see
// EnemyLimbs/Health.SetFromLimbs), and a hit only ever fully clears the ONE part it randomly
// rolls onto (see EnemyLimbs.MitigateHit). With N parts and a weapon that already one-shots any
// single part, the number of swings needed to actually finish the kill is a coupon-collector
// problem (expected N*(1+1/2+...+1/N) swings, not close to N) - the old 4-part ChauveSouris
// (Head/Torso/ArmLeft/ArmRight) averaged ~8 hits to clear regardless of its total HP number, way
// more than the "3 fist / 1-2 sword" the request asks for. Fewer parts is the only lever that
// actually controls that average, not total HP - see DungeonGenerator's assets.enemyPresets for
// the matching HP retune.
public static class EnemyLimbLayout
{
    // Zombie/Momie - the two species that keep real limb variety, since a "shambling, slows down
    // with a broken leg" identity is worth the extra swings a 4-part coupon-collector still costs
    // (average ~8 hits) compared to the fragile mobs below. Legs kept (not arms) so
    // EnemyController's broken-leg speed penalty is the one that actually fires for it - fits a
    // shambler's limp far better than a weakened bite would. Momie (2026-09-21) reuses this
    // outright rather than a near-duplicate layout - same undead-tank archetype as Zombie.
    static readonly BodyPart[] Grunt = { BodyPart.Head, BodyPart.Torso, BodyPart.LegLeft, BodyPart.LegRight };
    // ChauveSouris/Larve (fragile flier, weak swarm unit) and Sanglier/Skinwalker (2026-09-21 -
    // charging animal, eerie fast predator: neither has player-style limbs to speak of, and both
    // are meant to die in a predictable handful of hits, not survive a coupon-collector grind) - a
    // single-part blob means death is a deterministic ceil(HP/damage), no RNG tax from spreading HP
    // across limbs that only fragment an already-small pool. Also literally the "slime" case from
    // the original 2026-09-20 request.
    static readonly BodyPart[] Blob = { BodyPart.Torso };

    public static BodyPart[] For(EnemyType type) => type switch
    {
        EnemyType.Zombie => Grunt,
        EnemyType.Momie => Grunt,
        _ => Blob, // ChauveSouris, Larve, Sanglier, Skinwalker
    };
}
