using UnityEngine;

// Per-species stat band + per-floor level range for regular (non-boss) monsters - rolled once at
// spawn (see RoomController.SpawnEnemies), not a live leveling system: monsters don't persist
// across rooms/floors/sessions so there's nothing to grow over time, unlike the player. See
// project_xp_monster_leveling_backlog memory for the original spec and its open questions - this
// is this session's concrete resolution of them, noted inline below.
public static class MonsterLeveling
{
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

    // Explicit example given: a level-20 ChauveSouris should never reach anything like 20 Force
    // (capped ~2-5), while its Vitesse should run higher (8-10) - fast, not strong. Zombie is the
    // opposite (tanky/strong, slow, matches its existing preset). Larve has no stated identity
    // beyond "weak swarm unit" so both ranges stay low. Only Force/Dexterite -> the 2 stats that
    // map to something EnemyController actually models (contactDamage, moveSpeed) - Intelligence/
    // Portee/Charisme/Endurance have no monster-side equivalent (no monster casts, shoots at
    // range, shops, or has stamina today), so building a 5-stat table nobody would ever read felt
    // like the wrong kind of "complete" - scoped down deliberately rather than invented wholesale.
    // Kept modest in absolute terms - the player starts with only ~4 HP (see Health.maxHealth/
    // PlayerStats.constitution), and a monster at the very TOP of its species band can already be
    // rolled on floor 1 (t below is relative to the current floor's own range, not the dungeon's
    // deepest floor - see ApplyLevelStats) - so even Zombie's "strong" end must stay well short of
    // one-shotting a fresh player.
    static StatRange RangeFor(EnemyType type) => type switch
    {
        EnemyType.Zombie => new StatRange { forceMin = 3, forceMax = 9, vitesseMin = 1, vitesseMax = 3 },
        EnemyType.ChauveSouris => new StatRange { forceMin = 2, forceMax = 5, vitesseMin = 8, vitesseMax = 10 },
        _ => new StatRange { forceMin = 1, forceMax = 3, vitesseMin = 2, vitesseMax = 4 }, // Larve
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

        // Additive, not PlayerStats' 1%-per-point multiplier - contactDamage starts as a small
        // flat int (1-2 across every current preset), where a 1% multiplier would round away to
        // nothing even at Force 15 (1 * 1.15 still rounds to 1). +1 damage per 5 Force keeps the
        // stat's effect visible at the top of a species' band (Zombie: +1) without needing to
        // rebalance every preset's base damage, and without a lucky floor-1 roll coming close to
        // one-shotting a fresh player (see RangeFor's Force caps).
        contactDamage += Mathf.FloorToInt(force / 5f);
        moveSpeed *= 1f + vitesse * 0.02f;
    }

    // Constitution does NOT scale with the monster's own level - it scales with the FLOOR it's
    // found on, independent of level/species (explicit goal: stop the player from one-shotting
    // every mob forever regardless of how over-leveled they get relative to a monster's nominal
    // level range - a deep floor's monsters always have a real HP floor). +2 per floor past the
    // first, same "1 point = 1 half-heart" scale PlayerStats uses for the player's own
    // Constitution - this session's own numeric choice, not specified beyond the qualitative goal.
    public static int ConstitutionBonusForFloor(int floor) => 2 * Mathf.Max(0, floor - 1);
}
