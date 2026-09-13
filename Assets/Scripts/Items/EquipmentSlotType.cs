// Where a piece of gear can be equipped. Head covers hats/helmets/scarves/hoods and Neck covers
// necklaces/amulets/scarves - several item flavors sharing one slot, per the user's grouping -
// rather than a dedicated slot per flavor. RingLeft/RingRight each have 5 slots (see
// PlayerEquipment.ringsLeft/ringsRight) instead of a single slot like every other type here.
public enum EquipmentSlotType
{
    Head,
    Shoulders,
    Gloves,
    Boots,
    Neck,
    Belt,
    Knees,
    RingLeft,
    RingRight,
}
