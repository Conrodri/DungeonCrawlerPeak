// A FloorTrap's flavor - purely mechanical (which zone it targets, see AttackSource), never
// visually distinguishable before it triggers (FloorTrap stays a hidden floor decal either way,
// see its own comment) - rolled randomly per spawn in DungeonGenerator.SpawnFloorTrap.
public enum TrapType { BearTrap, CollapsingCeiling }
