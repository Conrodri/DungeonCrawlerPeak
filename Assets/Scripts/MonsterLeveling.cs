using UnityEngine;

// Per-species stat band + per-floor level range for regular (non-boss) monsters - rolled once at
// spawn (see RoomController.SpawnEnemies), not a live leveling system: monsters don't persist
// across rooms/floors/sessions so there's nothing to grow over time, unlike the player. See
// project_xp_monster_leveling_backlog memory for the original spec and its open questions - this
// is this session's concrete resolution of them, noted inline below.
public static class MonsterLeveling
{
    // Matches PlayerController.moveSpeed's own default - the whole point of anchoring every
    // monster's speed to this same number is that "a monster with 1 Vitesse moves exactly as fast
    // as a crawler with 1 Vitesse" (2026-09-15 report: some monsters were far slower than the
    // player at baseline because their old preset moveSpeed floats - 1.2/3.2/1.6 - were arbitrary
    // numbers unrelated to the player's own base(5)/Vitesse formula). See ApplyLevelStats below.
    public const float BaseSpeed = 5f;
    // Dialed back from the 1:1 player parity above (2026-09-15: "vitesse des mobs a 0.8 au lieu de
    // 1", halved 2026-09-21 morning: "ralentis les monstres de moitie" to 0.4, then bumped back up
    // 25% the same day: "accelere les mobs de 25%" -> 0.4 * 1.25 = 0.5) - every monster/boss now
    // moves at 50% of what its Vitesse stat would give an equivalent player, applied on top of
    // BaseSpeed/MoveSpeedMultiplierFor rather than changing that shared formula itself (which the
    // player's own MoveSpeedMultiplier still uses at 100%).
    public const float MonsterSpeedScale = 0.5f;


    // Only 2 data points were given (floor 1: 1-5, floor 2: 10-15) with "et ainsi de suite" for
    // the rest - 5->10 is exactly double, so each floor's range is built by doubling the previous
    // floor's max (floor 1 is the fixed base case, nothing to double from). Widens by one extra
    // level every floor too (4/5/6/7... between min and max) so deeper floors roll more varied
    // monsters, not just uniformly stronger ones.
    public static void GetLevelRange(int floor, out int min, out int max)
    {
        min = 1;
        max = 5;
        for (int f = 2; f <= floor; f++)
        {
            min = max * 2;
            max = min + 3 + f;
        }
    }

    public static int RollLevel(int floor)
    {
        GetLevelRange(floor, out int min, out int max);
        return Random.Range(min, max + 1);
    }

    struct StatRange { public int forceMin, forceMax, vitesseMin, vitesseMax; }

    // Vitesse now feeds the exact same formula the player's own Vitesse stat uses (see
    // ApplyLevelStats/PlayerStats.MoveSpeedMultiplierFor), off the shared BaseSpeed above - so
    // these ranges are on the PLAYER'S OWN SCALE, not an arbitrary per-species knob: 0 ties the
    // player's unbuffed walk exactly, negative is genuinely slower-than-player, positive faster.
    // Zombie stays tanky/strong (Force 3-9, unchanged) but slow (Vitesse -30 to -10 -> 2-4 u/s,
    // vs. the player's 5). ChauveSouris stays fast/fragile (Force 2-5, unchanged; Vitesse 30-50 ->
    // 8-10 u/s, faster than even the player's own sprint at 8 u/s) - a real "scary flier" now
    // instead of the old 3.2 u/s that barely out-walked the player. Larve has no stated identity
    // beyond "weak swarm unit" so it sits close to the player's own pace either way (Vitesse -10
    // to 10 -> 4-5.5 u/s). Only Force/Vitesse -> the 2 stats that map to something EnemyController
    // actually models (contactDamage, moveSpeed) - Intelligence/Portee/Charisme/Endurance have no
    // monster-side equivalent (no monster casts, shoots at range, shops, or has stamina today), so
    // building a 5-stat table nobody would ever read felt like the wrong kind of "complete" -
    // scoped down deliberately rather than invented wholesale.
    // Force kept modest in absolute terms - the player has 205 total HP across 6 limb pools (see
    // PlayerLimbs.BaseMaxFor), and a monster at the very TOP of its species band can already be
    // rolled on floor 1 (t below is relative to the current floor's own range, not the dungeon's
    // deepest floor - see ApplyLevelStats) - so even Zombie's "strong" end must stay well short of
    // one-shotting a fresh player.
    // Momie/Sanglier/Skinwalker added 2026-09-21 alongside their own preset entries (see
    // DungeonGenerator's assets.enemyPresets) - Momie shares Zombie's tanky/slow band outright
    // (same undead-shambler archetype); Sanglier is a fast charger (close to ChauveSouris' band,
    // a touch less extreme); Skinwalker sits as the agile mid-point between Larve and ChauveSouris.
    static StatRange RangeFor(EnemyType type) => type switch
    {
        EnemyType.Zombie => new StatRange { forceMin = 3, forceMax = 9, vitesseMin = -30, vitesseMax = -10 },
        EnemyType.ChauveSouris => new StatRange { forceMin = 2, forceMax = 5, vitesseMin = 30, vitesseMax = 50 },
        EnemyType.Momie => new StatRange { forceMin = 3, forceMax = 9, vitesseMin = -30, vitesseMax = -10 },
        EnemyType.Sanglier => new StatRange { forceMin = 2, forceMax = 6, vitesseMin = 25, vitesseMax = 45 },
        EnemyType.Skinwalker => new StatRange { forceMin = 3, forceMax = 7, vitesseMin = 10, vitesseMax = 30 },
        _ => new StatRange { forceMin = 1, forceMax = 3, vitesseMin = -10, vitesseMax = 10 }, // Larve
    };

    // Where in its species' band this monster's rolled level places it, relative to the CURRENT
    // FLOOR's own level range - keeps Force/Vitesse bounded by species identity regardless of how
    // high raw level numbers climb with depth (see GetLevelRange), instead of scaling without
    // limit.
    public static void ApplyLevelStats(EnemyType type, int level, int floor, ref float moveSpeed, ref int contactDamage)
    {
        GetLevelRange(floor, out int floorMin, out int floorMax);
        float t = floorMax > floorMin ? Mathf.InverseLerp(floorMin, floorMax, level) : 0f;
        StatRange range = RangeFor(type);
        int force = Mathf.RoundToInt(Mathf.Lerp(range.forceMin, range.forceMax, t));
        int vitesse = Mathf.RoundToInt(Mathf.Lerp(range.vitesseMin, range.vitesseMax, t));

        // Additive, not PlayerStats' 1%-per-point multiplier - a 1% multiplier would barely move a
        // preset's own base contactDamage. +4 per 5 Force (rescaled 2026-09-20 alongside every
        // preset's base contactDamage - was +1, negligible against the new 4-12 base range) keeps
        // the stat's effect visible at the top of a species' band (Zombie: +4) without a lucky
        // floor-1 roll coming close to one-shotting a fresh player (see RangeFor's Force caps).
        contactDamage += Mathf.FloorToInt(force / 5f) * 4;
        // Overwrites the caller's incoming value outright rather than multiplying it - a preset's
        // old flat moveSpeed number no longer means anything once Vitesse alone decides speed (see
        // RoomController.SpawnEnemies, which now passes in a throwaway 0f).
        moveSpeed = BaseSpeed * PlayerStats.MoveSpeedMultiplierFor(vitesse) * MonsterSpeedScale;
    }

    // Constitution does NOT scale with the monster's own level - it scales with the FLOOR it's
    // found on, independent of level/species (explicit goal: stop the player from one-shotting
    // every mob forever regardless of how over-leveled they get relative to a monster's nominal
    // level range - a deep floor's monsters always have a real HP floor). +2 per floor past the
    // first, same "1 point = 1 half-heart" scale PlayerStats uses for the player's own
    // Constitution - this session's own numeric choice, not specified beyond the qualitative goal.
    public static int ConstitutionBonusForFloor(int floor) => 2 * Mathf.Max(0, floor - 1);
}
