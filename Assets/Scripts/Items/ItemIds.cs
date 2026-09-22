public static class ItemIds
{
    public const string Gold = "gold";
    public const string Shuriken = "shuriken";
    public const string Caillou = "caillou";
    public const string Baton = "baton";
    public const string Bomb = "bomb";
    public const string Anvil = "anvil";
    // Real, deliberately-equipped weapons (see EquipmentSlotType.Weapon/ItemDefinition.IsWeapon) -
    // found on the ground/in a Chest/on a corpse like any other item, equipped by hand from the
    // inventory panel. CursedSword below stays a separate, special-cased forced weapon.
    public const string Sword = "sword";
    public const string Staff = "staff";
    public const string CursedSword = "cursed_sword";
    // 2026-09-22 request: 5 new melee weapon types + 4 new ranged (infinite-ammo) weapon types - see
    // PlayerController.WeaponType/DungeonGenerator's item registration for their stats/icons.
    // ShurikenLauncher is deliberately a separate id from Shuriken above - that one stays the
    // consumable throwable stack, this is the equippable weapon that never consumes it.
    public const string Halberd = "halberd";
    public const string ShortSword = "short_sword";
    public const string SpikedGloves = "spiked_gloves";
    public const string Rapier = "rapier";
    public const string Hammer = "hammer";
    public const string Sling = "sling";
    public const string ShurikenLauncher = "shuriken_launcher";
    public const string Revolver = "revolver";
    public const string Bow = "bow";
    public const string TrapSack = "trap_sack";
    public const string HealthPotion = "health_potion";
    public const string CerberusCollar = "cerberus_collar";
    public const string Wood = "wood";
    public const string Metal = "metal";
    public const string Stone = "stone";
    // Corpse-only material (see CorpseLoot.cs) - NPC-type bodies drop this instead of Wood/Metal/
    // Stone. No destructible-decor source (yet) - foreshadows the future clothing flammability
    // chantier (see project vision), not usable for anything today beyond being a lootable good.
    public const string Cloth = "cloth";

    // Flora - crafting ingredients for the Table de Craft's Potion de Soin recipes, no other use.
    // FlowerRed/FlowerBlue/Herb are scattered through decorable rooms (see DungeonGenerator.
    // DecorType.Flower); Mushroom only grows in the Flower Garden Safe-room variant (see
    // SafeRoomVariant.FlowerGarden/SpawnFlowerGardenContent) - deliberately exclusive to it.
    public const string FlowerRed = "flower_red";
    public const string FlowerBlue = "flower_blue";
    public const string Herb = "herb";
    public const string Mushroom = "mushroom";

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

    // Boss crafting-material drops (see DungeonGenerator.BossFamilyFor.resourceDropIds) - 3 per
    // family, "griffe, peau, aile, oeil, poils, crocs" per body part that fits the creature
    // (2026-09-21 request: "du craft de potions ou equipement" - no recipe uses these yet, this is
    // just the drop itself).
    public const string CerbereClaw = "cerbere_claw";
    public const string CerbereFang = "cerbere_fang";
    public const string CerbereFur = "cerbere_fur";
    public const string AnacondaFang = "anaconda_fang";
    public const string AnacondaEye = "anaconda_eye";
    public const string AnacondaSkin = "anaconda_skin";
    public const string EntBark = "ent_bark";
    public const string EntRoot = "ent_root";
    public const string EntSap = "ent_sap";
    public const string GolemPlate = "golem_plate";
    public const string GolemRivet = "golem_rivet";
    public const string GolemCable = "golem_cable";
    public const string KrakenEye = "kraken_eye";
    public const string KrakenSucker = "kraken_sucker";
    public const string KrakenInk = "kraken_ink";
    public const string AigleFeather = "aigle_feather";
    public const string AigleTalon = "aigle_talon";
    public const string AigleSensor = "aigle_sensor";
    public const string ArpenteurShadow = "arpenteur_shadow";
    public const string ArpenteurDust = "arpenteur_dust";
    public const string ArpenteurEcho = "arpenteur_echo";

    // New potions (2026-09-21 request: Table d'Alchimie) - HealthPotion above is repurposed as the
    // "Petite" tier (healAmount bumped 3->20) rather than duplicated.
    public const string GreaterHealthPotion = "greater_health_potion";
    public const string SpeedPotion = "speed_potion";
    public const string AdrenalinePotion = "adrenaline_potion";

    // Generic crafting ingredient (Table de Craft) - not tied to any one biome/boss.
    public const string Corde = "corde";

    // Boss equipment crafts (2026-09-21 request: "met des craft de boss, par exemple la patoune de
    // cerbere... tu vois le style") - one themed equipment piece per boss family, built from that
    // family's resourceDropIds above plus a generic ingredient (Sword/Corde/etc).
    public const string CerberePaw = "cerbere_paw";
    public const string AnacondaFangDagger = "anaconda_fang_dagger";
    public const string EntBarkPauldrons = "ent_bark_pauldrons";
    public const string GolemPlateGauntlets = "golem_plate_gauntlets";
    public const string KrakenInkNecklace = "kraken_ink_necklace";
    public const string AigleFeatherBoots = "aigle_feather_boots";
    public const string ArpenteurShadowRing = "arpenteur_shadow_ring";
}
