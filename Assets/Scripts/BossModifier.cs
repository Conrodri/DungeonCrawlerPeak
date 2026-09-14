// Boss-only modifier pool (see DungeonGenerator.RollBossModifiers) - distinct from the plain
// EliteModifier used by rank-and-file EnemyController (SpeedUp/HpUp only, single choice). Every
// boss draws N DISTINCT modifiers from this one shared pool regardless of family/tier - only the
// COUNT differs by tier (Zone 1, Ville 2, Region 3 - see project_xp_monster_leveling_backlog
// memory). Nothing beyond the count was specified, so the pool's actual content below is this
// session's own design pass:
// - Rapide: +30% move/charge speed.
// - Colossal: +50% max HP, slightly bigger.
// - Devastateur: +1 contact and volley damage.
// - Rafale: +2 projectiles per volley.
// - Blinde: flat damage reduction on every hit taken (see Health.flatDamageReduction), never
//   below 1 damage getting through so a fight can't stall out.
// - Vampirique: heals back half of every contact hit landed on the player (see
//   BossController.lifestealFraction) - ranged volley hits don't trigger it, simplification kept
//   since Projectile doesn't report back to its source on impact.
public enum BossModifier { Rapide, Colossal, Devastateur, Rafale, Blinde, Vampirique }
