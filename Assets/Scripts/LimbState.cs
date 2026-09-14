// A BodyPart's condition, derived from its own small HP pool (see PlayerLimbs) - separate from
// the player's overall Health pool, which is still what actually kills them. Healthy = full,
// Damaged = partial, Broken = 0 and immune to normal healing (see Health.Heal/PlayerLimbs.
// HealNonBroken) until repaired (PlayerLimbs.RepairAll, see the Tavernier's "Se reposer").
public enum LimbState { Healthy, Damaged, Broken }
