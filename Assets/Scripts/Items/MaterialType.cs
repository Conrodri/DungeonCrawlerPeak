// What an item is physically made of - drives 2 things: which crafting currency (Wood/Metal/
// Stone/Cloth, see ItemIds) repairs it (see RepairUI.MaterialItemId), and whether it can catch
// fire while worn (only Tissu - see PlayerEquipment.IgniteFlammable). None = no material (rings,
// currency, materials themselves) - MaxDurability is 0 on those anyway.
public enum MaterialType { None, Bois, Metal, Pierre, Tissu }
