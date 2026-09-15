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
    // Restaurant wares (see DungeonGenerator.SpawnCookNpc) - same HealAmount mechanism as
    // HealthPotion, bigger heals at a gold price to match, distinct from the Shop's potion.
    public const string Soup = "soup";
    public const string Stew = "stew";
    public const string Feast = "feast";
    public const string CerberusCollar = "cerberus_collar";
    public const string Wood = "wood";
    public const string Metal = "metal";
    public const string Stone = "stone";
    // Corpse-only material (see CorpseLoot.cs) - NPC-type bodies drop this instead of Wood/Metal/
    // Stone. No destructible-decor source (yet) - foreshadows the future clothing flammability
    // chantier (see project vision), not usable for anything today beyond being a lootable good.
    public const string Cloth = "cloth";

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

    // Boss trophies (see DungeonGenerator.BossFamilyFor) - one per biome family, shared across its
    // 3 power tiers (Zone/Ville/Region only change drop CHANCE and stats, not which item drops).
    // The Cave family reuses CerberusCollar above rather than a new id.
    public const string AnacondaScale = "anaconda_scale";
    public const string EntHeartshard = "ent_heartshard";
    public const string GolemCore = "golem_core";
    public const string KrakenTentacle = "kraken_tentacle";
    public const string EagleCog = "eagle_cog";
    public const string WandererFragment = "wanderer_fragment";
}
