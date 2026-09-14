// The 6 targetable body parts for enemy/boss attacks landing on the player (see PlayerLimbs).
// Broken-limb mechanics (a limb at 0 HP ignoring basic heals, damage redistributing to the rest of
// the body) are a separate later chantier - not implemented here. This enum only drives hit
// location targeting and per-part armor mitigation.
public enum BodyPart { Head, Torso, ArmLeft, ArmRight, LegLeft, LegRight }
