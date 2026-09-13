public static class ItemIds
{
    public const string Gold = "gold";
    public const string Shuriken = "shuriken";
    public const string Caillou = "caillou";
    public const string Baton = "baton";
    public const string Bomb = "bomb";
    public const string Anvil = "anvil";
    public const string CursedSword = "cursed_sword";
    public const string TrapSack = "trap_sack";
    public const string HealthPotion = "health_potion";
    public const string CerberusCollar = "cerberus_collar";
    public const string Wood = "wood";
    public const string Metal = "metal";
    public const string Stone = "stone";

    // Equipment (see EquipmentSlotType) - one plain placeholder per slot for now, no stat bonuses
    // yet, just enough to actually fill and test the new equipment panel. Sold at the Shop.
    public const string IronHelmet = "iron_helmet";
    public const string LeatherPauldrons = "leather_pauldrons";
    public const string CombatGloves = "combat_gloves";
    public const string WalkingBoots = "walking_boots";
    public const string SimpleNecklace = "simple_necklace";
    public const string LeatherBelt = "leather_belt";
    public const string LeatherKneepads = "leather_kneepads";
    public const string SimpleRing = "simple_ring";

    // Real shop wares (see DungeonGenerator.SpawnMerchantNpc) - one of the 8 stat rings is picked
    // at random each floor rather than sold all at once.
    public const string RingForce = "ring_force";
    public const string RingDexterite = "ring_dexterite";
    public const string RingIntelligence = "ring_intelligence";
    public const string RingVitesse = "ring_vitesse";
    public const string RingConstitution = "ring_constitution";
    public const string RingPortee = "ring_portee";
    public const string RingCharisme = "ring_charisme";
    public const string RingEndurance = "ring_endurance";
    public const string AntiHoleBoots = "anti_hole_boots";
    public const string VisionGlasses = "vision_glasses";
}
