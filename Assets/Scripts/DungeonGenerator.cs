using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Runtime-safe (no UnityEditor dependency - grep this file for "UnityEditor" to confirm) so a
// build can call Build(seed) directly, not just the Unity Editor via Dungeon/Generate Floor
// (Assets/Editor/DungeonBootstrap.cs, a thin wrapper around this). Same seed => same floor,
// since Build() seeds UnityEngine.Random itself before generating anything.
public static class DungeonGenerator
{
    enum RoomType { Start, Empty, Monster, Shop, Treasure, Secret, Gamble, Boss, Event, Safe, Stairs }

    // What the third Safe room of a floor actually contains (see arcadeCell/arcadeVariant in
    // GenerateLayout) - picked at random per floor, Arcade is no longer guaranteed.
    enum SafeRoomVariant { Arcade, FlowerGarden, MaterialStorage }

    // Sized to fill a 16:9 screen at the camera's orthographic size (RoomHeight/2) with no
    // letterboxing: 22x12 slightly overscans widescreen rather than under-filling it.
    const int RoomWidth = 22;
    const int RoomHeight = 12;
    // No walkable corridor lives in this gap anymore - crossing a doorway teleports straight to
    // the connected room, so it only needs to keep the two rooms' walls from touching.
    const int Gap = 3;
    const int DoorWidth = 2;
    const int DoorMargin = 2; // keep doors at least this far from a room's corners
    const int StepX = RoomWidth + Gap;
    const int StepY = RoomHeight + Gap;
    // Was 8 - doubled per explicit request ("etages 2 fois plus grands"). Every special room type
    // (Boss/Secret/Stairs/Treasure/Shop/Event/Gamble/Safe x2 - see the second PlaceSpecialRoom
    // call below) RECLASSIFIES an existing tree cell rather than adding a new one, so 8 was already
    // tight - up to 9 conversions could eat most of a floor's actual Monster rooms. 16 leaves
    // plenty of room for real combat content alongside every special room, including the extra
    // Safe room.
    // Was 16 - raised to 30 alongside the Safe-room minimum grid spacing below (see
    // SafeRoomMinGridDistance) and Safe being placed earlier in GenerateLayout: a floor this small
    // didn't leave enough physical room on the grid to keep 3 Safe rooms 5+ cells apart from each
    // other in a tree that also has to fit 3 Boss rooms, Stairs, Treasure, Shop, Event and Gamble as
    // dead ends. Measured empirically across 60 seeds - 20 rooms still left 22/60 with a violation,
    // 24 got it down to 9/60, 30 to 3/60 (and those 3 are near-misses, never a truly glued pair).
    const int TargetNormalRooms = 30; // includes the Start room
    // Minimum GRID distance (Manhattan, in map cells) required between any two Safe rooms - reported
    // 2026-09-16: an Arcade/Restaurant Safe room still landed right beside another Safe room even
    // after the graph-adjacency fix earlier the same day. That fix only forbade a new Safe room from
    // branching directly OFF an existing Safe room (distance 1 through the corridor graph) - it said
    // nothing about two Safe rooms that end up spatially close on the grid via two DIFFERENT branches
    // of the tree (a winding layout can put a corridor-far room only 1-2 cells away in actual map
    // space). See FindPlacementCandidate's minGridDistanceFromSameType for the actual filter.
    const int SafeRoomMinGridDistance = 5;
    // Wall tint for Boss/Safe rooms (2026-09-16 request) - multiplied onto the biome's own wall
    // texture (see BuildFloorAssets's CreateWallSprite) rather than replacing it, so the tint still
    // reads as "this biome's walls, but different" instead of a flat unrelated color. Same hues as
    // MinimapController's bossOutlineColor/safeOutlineColor so the in-room cue and the minimap icon
    // reinforce each other instead of teaching the player two unrelated color codes.
    static readonly Color BossWallTint = new Color(1f, 0.45f, 0.4f);
    static readonly Color SafeWallTint = new Color(0.55f, 1f, 0.7f);
    const float EliteChance = 0.05f;

    // Physics2D layers for the flying-enemy pass-through rule (2026-09-15 request: "les mobs
    // volants passent au travers de tous les murs et objets bloquants") - arbitrary unused user
    // layer indices (8-31 exist by default in every Unity project, no Tags-and-Layers asset edit
    // needed to use them purely from code). Every solid wall/door-blocker/destructible/prop sits
    // on BlockingLayer; a flying enemy (see RoomController.SpawnEnemies) sits on FlyingLayer
    // instead of Default. IgnoreLayerCollision is a global physics setting, not per-object - set
    // once per Build()/BuildTutorial() below (idempotent, repeating it every floor is harmless).
    public const int BlockingLayer = 9;
    public const int FlyingLayer = 10;

    const int TilePixelSize = 16;
    const int WallExtraHeight = 8;

    // Spawn placement tuning (see GenerateEnemySpawnPositions): distance kept from walls, doors,
    // and other spawned enemies.
    const float EnemySpawnWallMargin = 3f;
    // Bumped from 3 - per feedback, spawning this close to the door the player just walked
    // through (combined with no activation delay, since fixed separately - see
    // EnemyController.ActivationDelay) felt like an ambush rather than a room to size up.
    const float EnemySpawnMinDoorDistance = 5f;
    // Bumped from 3 - per feedback, scattered spawns still read as "all in one pack".
    const float EnemySpawnMinSpacing = 4.5f;
    const int EnemySpawnMaxAttempts = 30;

    // Stone block tuning: tougher than a fresh player's Force (1) so bombs matter, but breakable
    // in melee once Force is raised by some future means.
    const int StoneBlockHealth = 3;
    const int StoneBlockRequiredForce = 2;
    const int WoodDebrisHealth = 1;
    const int WoodDebrisRequiredForce = 0;
    const int MetalDebrisHealth = 5;
    const int MetalDebrisRequiredForce = 3;
    const float DecorWallMargin = 3f;
    const float DecorMinSpacing = 2f;
    const float DecorMinDoorDistance = 2.5f;
    const float DecorMinAvoidDistance = 2f;

    // Souls-like floor progression tuning (see FloorTimer/Staircase).
    const float FloorDuration = 600f; // 10 minutes
    const float TimedStairsUnlockFraction = 0.5f; // unlocks 5 of the 10 minutes in

    static readonly string[] SkullMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXOXXXXOXX",
        "XXOXXXXOXX",
        "XXXXXXXXXX",
        "XXXX  XXXX",
        "XXXXXXXXXX",
        "XX X X XX ",
        "  XXXXXX  ",
    };

    static readonly string[] ChestMask =
    {
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "          ",
        "XXXXXXXXXX",
        "XXXX  XXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
    };

    static readonly string[] ExclamationMask =
    {
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "          ",
        "          ",
        "   XXXX   ",
        "   XXXX   ",
    };

    static readonly string[] QuestionMask =
    {
        "   XXXXX  ",
        "  XX   XX ",
        "  X    XX ",
        "      XX  ",
        "     XX   ",
        "     XX   ",
        "          ",
        "     XX   ",
        "     XX   ",
    };

    // Monster/boss silhouettes (see enemyPresets/BossFamilyFor) - flat recognizable shapes instead
    // of a plain tinted circle, same mask-to-texture approach as the markers above.
    static readonly string[] ZombieMask =
    {
        "  XXXX    ",
        "  XXXX    ",
        "  XXXX    ",
        " XXXXXXX  ",
        "XXXXXXXXX ",
        "XXXXXXXXX ",
        "XX XXXX X ",
        "XX XXXX X ",
        "XX      X ",
        "XX      X ",
    };
    static readonly string[] ChauveSourisMask =
    {
        "XX      XX",
        "XXX    XXX",
        " XXX  XXX ",
        "  XXXXXX  ",
        "   XXXX   ",
        "   XXXX   ",
        "   X  X   ",
        "   X  X   ",
    };
    static readonly string[] LarveMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
    };
    // Bulkier and more tapered than ZombieMask - bandaged arms held close to the body instead of
    // out, legs wrapped together into two narrow stripes (2026-09-21 request: "rajoute quelques
    // mobs, momies...").
    static readonly string[] MomieMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        " XX    XX ",
        " XX    XX ",
        " XX    XX ",
    };
    // Low, wide quadruped body with a tapered snout at front and 4 short legs - "sangliers".
    static readonly string[] SanglierMask =
    {
        "    XXXX  ",
        "  XXXXXXXX",
        " XXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XX  XX  XX",
        "XX  XX  XX",
    };
    // Hunched humanoid with a jagged/antlered head silhouette and long gangly limbs - "skinwalkers".
    static readonly string[] SkinwalkerMask =
    {
        " X  X  X  ",
        " XXXXXXX  ",
        "  XXXXX   ",
        "  XXXXX   ",
        " XXXXXXX  ",
        "XXXXXXXXX ",
        "X X    X X",
        "X X    X X",
        "  X    X  ",
        "  X    X  ",
    };
    // Robed humanoid with a pointed hat and a staff held to its side - "sorciers" (2026-09-21
    // request, "mobs type sorcier"). Deliberately reads as a caster, not a brawler, next to the
    // Grunt/Blob melee silhouettes above.
    static readonly string[] SorcierMask =
    {
        "   XX     ",
        "  XXXXX   ",
        " XXXXXXX  ",
        "  XXXX  X ",
        " XXXXXX X ",
        "XXXXXXXX X",
        "XXXXXXXX X",
        "XXXXXXXXX ",
        "XX XXXX X ",
        "X   XX   X",
    };
    static readonly string[] AnacondaMask =
    {
        "XXXX      ",
        "XXXXX     ",
        " XXXXX    ",
        "  XXXXX   ",
        "   XXXXX  ",
        "    XXXXX ",
        "     XXXXX",
        "      XXXX",
        "       XXX",
        "        XX",
    };
    static readonly string[] EntMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "   XXXX   ",
        "   XXXX   ",
        "   XXXX   ",
        "  X    X  ",
        " X      X ",
    };
    static readonly string[] GolemMask =
    {
        " XXXXXXXX ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXX XX XXX",
        "XXX XX XXX",
        "XXX    XXX",
        "XXX    XXX",
        "XX      XX",
    };
    static readonly string[] KrakenMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "X X X X X ",
        " X X X X X",
        "X X X X X ",
    };
    static readonly string[] CerbereMask =
    {
        " XX XX XX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XX XX XX X",
        "XX XX XX X",
        "X   X   X ",
        "X   X   X ",
    };
    static readonly string[] AigleMask =
    {
        "X        X",
        "XX      XX",
        " XXX  XXX ",
        "  XXXXXX  ",
        "   XXXX   ",
        "  XXXXXX  ",
        "   XXXX   ",
        "    XX    ",
    };
    static readonly string[] ArpenteurMask =
    {
        "   XXXX   ",
        "   XXXX   ",
        "  XXXXXX  ",
        "  XXXXXX  ",
        "  XXXXXX  ",
        "  XXXXXX  ",
        "  XXXXXX  ",
        "  XX  XX  ",
        "  XX  XX  ",
        "  XX  XX  ",
    };

    // A locked staircase's blocker - vertical bars with a top/bottom frame, transparent gaps in
    // between, so the Exit_Bright icon underneath (see SetupStaircase, sortingOrder 0 vs. the
    // blocker's 1) stays visible through the cage instead of being fully hidden by a solid square.
    static readonly string[] CageMask =
    {
        "XXXXXXXXXX",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "X..X..X..X",
        "XXXXXXXXXX",
    };

    // Ground item silhouettes (see RegisterItem below) - same mask-to-texture approach as the
    // enemy/marker shapes above, recognizable shapes instead of a plain flat-colored square.
    static readonly string[] SwordMask =
    {
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "  XXXXXX  ",
        "    XX    ",
        "   XXXX   ",
        "    XX    ",
    };
    static readonly string[] CursedSwordMask =
    {
        "    XX    ",
        "   X  X   ",
        "    XX    ",
        "   XX     ",
        "    XX    ",
        "   X  X   ",
        "  XXXXXX  ",
        "    XX    ",
        "   XXXX   ",
        "    XX    ",
    };
    static readonly string[] StaffMask =
    {
        "   XXXX   ",
        "  XXXXXX  ",
        "   XXXX   ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
        "    XX    ",
    };
    static readonly string[] ShurikenMask =
    {
        "X        X",
        "XX      XX",
        " XX    XX ",
        "  XX  XX  ",
        "   XXXX   ",
        "   XXXX   ",
        "  XX  XX  ",
        " XX    XX ",
        "XX      XX",
        "X        X",
    };
    static readonly string[] BatonMask =
    {
        "XX        ",
        "XXX       ",
        " XXX      ",
        "  XXX     ",
        "   XXX    ",
        "    XXX   ",
        "     XXX  ",
        "      XXX ",
        "       XXX",
        "        XX",
    };
    // A simple diagonal handle on a wide base plate - reads as "lever/switch" at a glance instead
    // of a plain square indistinguishable from room decor (StoneBlock/WoodDebris/MetalDebris all
    // use CreateSolidSprite, no masked shape - see 2026-09-16 lever visibility rework).
    static readonly string[] LeverMask =
    {
        "     XX   ",
        "    XXX   ",
        "   XXX    ",
        "  XXX     ",
        " XXX      ",
        " XXX      ",
        " XXX      ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
    };
    static readonly string[] AnvilMask =
    {
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "  XXXXXX  ",
        "  XXXXXX  ",
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "   XXXX   ",
        "   XXXX   ",
        "  XXXXXX  ",
    };
    static readonly string[] TrapSackMask =
    {
        "   XXXX   ",
        "   XXXX   ",
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        " XXXXXXXX ",
        "  XXXXXX  ",
    };
    static readonly string[] CerberusCollarMask =
    {
        " X  X  X  ",
        "  XXXXXX  ",
        " XX    XX ",
        "XX      XX",
        "X        X",
        "X        X",
        "XX      XX",
        " XX    XX ",
        "  XXXXXX  ",
    };
    static readonly string[] WoodMask =
    {
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXOXXXXOXX",
        "XXOXXXXOXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
    };
    static readonly string[] MetalMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XXXXXX  ",
    };
    static readonly string[] ClothMask =
    {
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XX X X X X",
        " X X X X  ",
    };
    static readonly string[] StoneMask =
    {
        "  XX  XX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XX  XX  ",
    };
    static readonly string[] HelmetMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXX    XXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "   XXXX   ",
    };
    static readonly string[] PauldronsMask =
    {
        "XXX    XXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XXXXXX  ",
    };
    static readonly string[] GlovesMask =
    {
        " XX XX XX ",
        " XXXXXXXX ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XXXXXX  ",
    };
    static readonly string[] BootsMask =
    {
        "  XXXX    ",
        "  XXXX    ",
        "  XXXX    ",
        "  XXXX    ",
        "  XXXXXXX ",
        "XXXXXXXXXX",
    };
    static readonly string[] AntiHoleBootsMask =
    {
        "  XXXX    ",
        "  XXXX    ",
        "  XXXX    ",
        "  XXXX    ",
        "  XXXXXXX ",
        "XXXXXXXXXX",
        "X X X X X ",
    };
    static readonly string[] BeltMask =
    {
        "          ",
        "XXXXXXXXXX",
        "XXX XX XXX",
        "XXX XX XXX",
        "XXXXXXXXXX",
        "          ",
    };
    static readonly string[] KneepadsMask =
    {
        "  XXXXXX  ",
        " XXXXXXXX ",
        "XX      XX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XXXXXX  ",
    };
    static readonly string[] GlassesMask =
    {
        "          ",
        " XX    XX ",
        "X  X  X  X",
        "X  X  X  X",
        " XX    XX ",
        "          ",
    };

    // The seed the currently-loaded floor was built with - a Safe room's "rest" option saves this
    // alongside player state, so DungeonGenerator.Build(CurrentSeed) recreates the exact same floor.
    public static int CurrentSeed { get; private set; }
    // Which Souls-like floor this is (1 = the first). Persisted alongside the seed so "Continuer"
    // and the Tavernier's save resume on the right floor, not always floor 1.
    public static int CurrentFloor { get; private set; } = 1;
    // Purely visual per-floor theme (see BiomeTheme) - rolled fresh every Build(), not persisted
    // across a save/continue, since it's re-derived from the seed the same way the layout is.
    public static Biome CurrentBiome { get; private set; }

    // Which Monster rooms (by grid cell, any member of RoomController.memberCells) have been fully
    // cleared on the CURRENTLY LOADED floor, and whether its boss is dead - tracked live as
    // RoomController.OnRoomCleared/BossRoomController.OnBossDefeated fire (see SetupMonsterRoom/
    // SetupBossRoom), reset at the top of every Build()/BuildTutorial(). A Safe room's "rest"
    // reads these into the save file (see DialogueManager) so "Continuer" can restore them via
    // Build's clearedRooms/bossDefeated parameters below - without this, resuming a save
    // regenerated the exact same layout (same seed) but with zero memory of progress within it,
    // so every monster and the boss respawned on every reload.
    static readonly HashSet<Vector2Int> clearedRoomsThisFloor = new HashSet<Vector2Int>();
    static bool bossDefeatedThisFloor;
    public static IEnumerable<Vector2Int> ClearedRoomsThisFloor => clearedRoomsThisFloor;
    public static bool BossDefeatedThisFloor => bossDefeatedThisFloor;

    // Which biome each of THIS floor's 3 boss tiers actually got (see PickFloorBiome) - Region's
    // is always CurrentBiome itself (ties the floor's environment to its strongest/defining boss,
    // unchanged from before), Ville/Zone each get a DIFFERENT biome so a floor's 3 boss
    // encounters are 3 distinct families, never the same one 3 times over (2026-09-15 report:
    // "un boss ne doit pas etre present plusieurs fois au meme etage"). Populated here for Region
    // and in GenerateLayout for Ville/Zone (wherever each tier's cell gets decided).
    static Dictionary<BossTier, Biome> bossTierBiomes = new Dictionary<BossTier, Biome>();

    // Every biome a boss tier has used so far THIS RUN (across every floor, not just the current
    // one) - PickFloorBiome refuses to hand out one already in here, so a boss family never comes
    // back on a LATER floor either ("empeche le respawn de ce boss aux etages suivants"). Recycles
    // the whole list once all 7 biomes have been used at least once, rather than getting stuck -
    // there are only 7 families total and a floor now spends up to 3 of them at once, so exhaustion
    // is reached fast (explicit 2026-09-15 design call: recycle the whole pool, exclude as soon as
    // a family APPEARS on a floor, not only once its boss is actually defeated).
    static readonly List<Biome> usedBossBiomes = new List<Biome>();
    // Snapshot of usedBossBiomes as it stood at the very START of the CURRENT floor's Build() call
    // (i.e. NOT including this floor's own 3 picks) - this, not the live list above, is what gets
    // persisted (see SaveManager.Capture/Save's usedBossBiomesBeforeFloor). Restoring exactly this
    // snapshot before Build() re-runs is what lets "Continuer" reproduce this floor's exact same 3
    // boss families instead of rolling a fresh set (same "same seed -> same floor" guarantee the
    // layout/CurrentBiome already relies on).
    static List<Biome> preFloorUsedBossBiomes = new List<Biome>();
    public static IEnumerable<Biome> UsedBossBiomesBeforeCurrentFloor => preFloorUsedBossBiomes;

    static readonly Biome[] AllBiomes = (Biome[])System.Enum.GetValues(typeof(Biome));

    static bool ContainsBiome(IEnumerable<Biome> biomes, Biome target)
    {
        foreach (Biome b in biomes) if (b == target) return true;
        return false;
    }

    // Picks a biome not already given to another tier on THIS floor (excludeThisFloor) and not
    // used on any EARLIER floor this run (usedBossBiomes) - recycling the whole run-wide history
    // once every biome not excluded this floor has already been used, rather than ever finding an
    // empty pool. Adds the pick to usedBossBiomes before returning it.
    static Biome PickFloorBiome(IEnumerable<Biome> excludeThisFloor)
    {
        List<Biome> pool = new List<Biome>();
        foreach (Biome b in AllBiomes) if (!ContainsBiome(excludeThisFloor, b) && !usedBossBiomes.Contains(b)) pool.Add(b);
        if (pool.Count == 0)
        {
            usedBossBiomes.Clear();
            foreach (Biome b in AllBiomes) if (!ContainsBiome(excludeThisFloor, b)) pool.Add(b);
        }
        Biome chosen = pool[Random.Range(0, pool.Count)];
        usedBossBiomes.Add(chosen);
        return chosen;
    }

    public static void Build(int seed, int floor = 1, IEnumerable<Vector2Int> clearedRooms = null, bool bossDefeated = false, IEnumerable<Biome> priorUsedBossBiomes = null)
    {
        CurrentSeed = seed;
        CurrentFloor = floor;
        Random.InitState(seed);
        Physics2D.IgnoreLayerCollision(BlockingLayer, FlyingLayer, true);

        clearedRoomsThisFloor.Clear();
        if (clearedRooms != null) foreach (Vector2Int cell in clearedRooms) clearedRoomsThisFloor.Add(cell);
        bossDefeatedThisFloor = bossDefeated;

        // priorUsedBossBiomes is only ever non-null from "Continuer" (see SaveData.
        // usedBossBiomesBeforeFloor) - restores the run-wide history to exactly what it was BEFORE
        // this floor originally picked its own 3 biomes, so re-running the picks below (same seed,
        // same call order) reproduces the identical set instead of rolling a fresh one. A mid-
        // session Descend() passes null and just keeps accumulating whatever's already here.
        if (priorUsedBossBiomes != null)
        {
            usedBossBiomes.Clear();
            usedBossBiomes.AddRange(priorUsedBossBiomes);
        }
        preFloorUsedBossBiomes = new List<Biome>(usedBossBiomes);

        bossTierBiomes = new Dictionary<BossTier, Biome>();
        Biome biome = PickFloorBiome(bossTierBiomes.Values);
        bossTierBiomes[BossTier.Region] = biome;
        CurrentBiome = biome;
        BiomeTheme biomeTheme = BiomeTheme.Get(biome);

        // Split into two calls around the floorTheme roll just below - that roll consumes
        // UnityEngine.Random, and it sits BETWEEN the enemyPresets/decor block and the markers/item
        // registration block in the original code, so a single call here would consume Random in a
        // different relative order than before (confirmed the hard way: the first attempt at this
        // refactor moved the roll to before all asset creation, which is Random-free on its own,
        // but subtly desynced every Random draw for the rest of the floor - biome pools, boss
        // families, layout, decor placement, all downstream of one shared Random stream - caught by
        // diffing a before/after fingerprint of a full Build() call, not by inspection).
        FloorAssets assets = BuildFloorAssetsPart1(biomeTheme);
        Sprite floorSprite = assets.floorSprite;
        Sprite wallSprite = assets.wallSprite;
        Sprite playerSprite = assets.playerSprite;
        Sprite bossProjectileSprite = assets.bossProjectileSprite;
        Sprite bossSlobberPuddleSprite = assets.bossSlobberPuddleSprite;
        DecorSprites decorSprites = assets.decorSprites;
        Sprite furnitureWoodSprite = assets.furnitureWoodSprite;
        Sprite rugSprite = assets.rugSprite;
        Sprite wallDecorSprite = assets.wallDecorSprite;
        Sprite shopCrateSprite = assets.shopCrateSprite;
        Sprite enemyGlowSprite = assets.enemyGlowSprite;
        Sprite speedUpBadge = assets.speedUpBadge;
        Sprite hpUpBadge = assets.hpUpBadge;
        RoomController.EnemyPresetEntry[] enemyPresets = assets.enemyPresets;

        // A whole floor sometimes commits to a single creature type, so every Monster room draws
        // from it instead of picking its own encounter pattern. Dropped from 0.3 to 0.12
        // (2026-09-21 report: "on a pas assez de monstres differents, j'ai ouvert 5 portes et je
        // me suis tape 5 fois 3 chauves souris") - a 30% chance of the WHOLE floor going
        // single-species was very likely what that report actually hit, on top of
        // EncounterPatterns itself being thin (see its own comment, expanded same request).
        EnemyType? floorTheme = Random.value < 0.12f ? (EnemyType?)(EnemyType)Random.Range(0, 6) : null;

        BuildFloorAssetsPart2(assets, out List<ItemCatalog.Entry> itemEntries);
        Sprite shopMarker = assets.shopMarker;
        Sprite treasureMarker = assets.treasureMarker;
        Sprite chestSprite = assets.chestSprite;
        Sprite secretMarker = assets.secretMarker;
        Sprite gambleMarker = assets.gambleMarker;
        Sprite bossMarker = assets.bossMarker;
        Sprite eventMarker = assets.eventMarker;
        Sprite safeMarker = assets.safeMarker;
        Sprite stairsMarker = assets.stairsMarker;
        Sprite stairsCageSprite = assets.stairsCageSprite;
        Sprite leverSprite = assets.leverSprite;
        Sprite craftingTableSprite = assets.craftingTableSprite;
        Sprite alchemyTableSprite = assets.alchemyTableSprite;
        Sprite projectileSprite = assets.projectileSprite;
        Sprite fistVisualSprite = assets.fistVisualSprite;
        Sprite swordVisualSprite = assets.swordVisualSprite;
        Sprite explosionSprite = assets.explosionSprite;
        Sprite lightningOrbSprite = assets.lightningOrbSprite;
        Sprite lightningBoltSprite = assets.lightningBoltSprite;
        Sprite fireballSprite = assets.fireballSprite;
        Sprite fireLineSprite = assets.fireLineSprite;
        Sprite burnIconSprite = assets.burnIconSprite;
        Sprite doorBarrierSprite = assets.doorBarrierSprite;
        Sprite secretWallSprite = assets.secretWallSprite;
        Sprite outlineRingSprite = assets.outlineRingSprite;
        Sprite npcStrangerSprite = assets.npcStrangerSprite;
        Sprite npcElderSprite = assets.npcElderSprite;
        Sprite npcMerchantSprite = assets.npcMerchantSprite;
        Sprite npcBadgeSprite = assets.npcBadgeSprite;
        Tile floorTile = assets.floorTile;
        Tile wallTile = assets.wallTile;

        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);

        GameObject root = new GameObject("DungeonRoot");

        // Built inactive so ItemCatalog.Awake() (which re-registers into ItemDatabase - the path
        // that matters when a saved scene loads fresh, without Build() running) can't fire before
        // `entries` is assigned below: at real runtime (unlike an Editor edit-mode call),
        // AddComponent/new GameObject fires Awake() synchronously, which would otherwise wipe the
        // ItemDatabase this method just populated directly.
        GameObject itemCatalogGO = new GameObject("ItemCatalog");
        itemCatalogGO.SetActive(false);
        itemCatalogGO.transform.SetParent(root.transform);
        ItemCatalog itemCatalog = itemCatalogGO.AddComponent<ItemCatalog>();
        itemCatalog.entries = itemEntries;
        itemCatalogGO.SetActive(true);

        GameObject gridGO = new GameObject("Grid", typeof(Grid));
        gridGO.transform.SetParent(root.transform);

        GameObject floorGO = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer));
        floorGO.transform.SetParent(gridGO.transform);
        floorGO.GetComponent<TilemapRenderer>().sortingOrder = -1;

        // No CompositeCollider2D: its "Merge" geometry generation silently produces incomplete
        // coverage on a tilemap this large (confirmed only ~20-40% of wall tiles per room actually
        // got collision, the rest let the player walk straight through) - individual per-tile
        // colliders from TilemapCollider2D alone are reliable and cheap enough at this scale.
        GameObject wallsGO = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(Rigidbody2D));
        wallsGO.layer = BlockingLayer;
        wallsGO.transform.SetParent(gridGO.transform);
        // Individual mode (rather than batched Chunk) lets each wall tile sort against the
        // player sprite by Y position, so tall wall tops correctly draw in front of / behind the player.
        wallsGO.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
        wallsGO.GetComponent<TilemapRenderer>().sortingOrder = 0;
        Rigidbody2D wallsBody = wallsGO.GetComponent<Rigidbody2D>();
        wallsBody.bodyType = RigidbodyType2D.Static;

        Tilemap floorMap = floorGO.GetComponent<Tilemap>();
        Tilemap wallsMap = wallsGO.GetComponent<Tilemap>();
        // Anchor wall tiles to the bottom of their cell so the extra sprite height (the "face")
        // pokes upward into the cell above instead of sinking into the floor below.
        wallsMap.tileAnchor = new Vector3(0.5f, 0f, 0f);

        Dictionary<Vector2Int, RoomType> layout = GenerateLayout(out Dictionary<Vector2Int, RectInt> cellGroups, out Dictionary<Vector2Int, BossTier> bossTiers, out Vector2Int restaurantCell, out Vector2Int arcadeCell, out SafeRoomVariant arcadeVariant);

        // Carve the room geometry and cut door openings for every adjacent pair.
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            BuildRoomGeometry(kv.Key.x * StepX, kv.Key.y * StepY, floorMap, wallsMap, floorTile, wallTile);
        }
        // A merged duo/trio/quad room (see MergeMultiCellRooms) then gets its WHOLE bounding
        // rectangle repainted as one room (solid only on the true outer border) - overwrites the
        // per-cell interior walls BuildRoomGeometry just drew. Bug found 2026-09-14: the old
        // approach instead patched each adjacent PAIR's shared wall one seam at a time
        // (OpenFullHorizontalSeam/OpenFullVerticalSeam, removed) - correct for a 2-cell room, but
        // a 2x2 (quadruple) room has a 3x3 pocket at its exact center where neither a horizontal
        // nor a vertical seam call ever reaches (each only clears its own row/column band, never
        // the diagonal gap corner shared by all 4 cells) - left permanently walled off on all 4
        // sides with no floor, so anything spawned there (the boss, always exactly at the group's
        // center - see SetupBossRoom) was sealed in a tiny box. Repainting the full rect at once
        // has no such gap for any shape, so it replaces the seam-by-seam approach entirely.
        var repaintedGroups = new HashSet<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            RectInt group = cellGroups[kv.Key];
            if (group.width * group.height <= 1) continue; // unmerged - BuildRoomGeometry's own border is already correct
            Vector2Int groupKey = new Vector2Int(group.xMin, group.yMin);
            if (!repaintedGroups.Add(groupKey)) continue; // one repaint per group, not per member cell

            Rect worldRect = GroupWorldRect(group);
            int gxMin = Mathf.RoundToInt(worldRect.xMin), gyMin = Mathf.RoundToInt(worldRect.yMin);
            int gw = Mathf.RoundToInt(worldRect.width), gh = Mathf.RoundToInt(worldRect.height);
            for (int x = 0; x < gw; x++)
            {
                for (int y = 0; y < gh; y++)
                {
                    Vector3Int pos = new Vector3Int(gxMin + x, gyMin + y, 0);
                    bool isWall = x == 0 || y == 0 || x == gw - 1 || y == gh - 1;
                    wallsMap.SetTile(pos, isWall ? wallTile : null);
                    floorMap.SetTile(pos, isWall ? null : floorTile);
                }
            }
        }
        // Each room's doors, so a Monster room can later block/unblock its own thresholds. Door
        // TRIGGERS aren't created yet here - that happens once every Monster room's
        // RoomController exists below, so each trigger can be wired to check the lock state of
        // the room it teleports into (see pendingDoorLinks).
        var doorsByRoom = new Dictionary<Vector2Int, List<(Vector2 pos, bool onVerticalWall)>>();
        var pendingDoorLinks = new List<(Vector2 posA, Vector2 inwardA, Vector2Int cellA, Vector2 posB, Vector2 inwardB, Vector2Int cellB, bool onVerticalWall)>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            Vector2Int cell = kv.Key;
            Vector2Int rightCell = cell + Vector2Int.right;
            if (layout.ContainsKey(rightCell))
            {
                if (SameGroup(cellGroups, cell, rightCell))
                {
                    // Two cells of the same merged room - no door, already one continuous floor
                    // (see the group repaint pass above).
                }
                else
                {
                    bool leftIsSecret = kv.Value == RoomType.Secret;
                    bool rightIsSecret = layout[rightCell] == RoomType.Secret;
                    // A secret wall always sits at the dead center of the wall - the wall itself
                    // gives no visual hint either way, but at least a player who suspects a given
                    // wall only needs to bomb the one predictable spot instead of the whole length of it.
                    // Boss/Safe/Shop/Stairs get the same centered treatment for a different reason -
                    // these read as a deliberate gate into a set-piece room, not an organic doorway.
                    bool useGateOffset = leftIsSecret || rightIsSecret || IsGatedRoomType(kv.Value) || IsGatedRoomType(layout[rightCell]);
                    int doorY = (useGateOffset ? CenteredDoorOffset(RoomHeight) : RandomDoorOffset(RoomHeight)) + cell.y * StepY;
                    CarveHorizontalDoor(cell.x * StepX, (cell.x + 1) * StepX, doorY, doorY, floorMap, wallsMap, floorTile);

                    Vector2 leftPos = new Vector2(cell.x * StepX + RoomWidth - 0.5f, doorY + DoorWidth / 2f);
                    Vector2 rightPos = new Vector2((cell.x + 1) * StepX + 0.5f, doorY + DoorWidth / 2f);

                    if (leftIsSecret || rightIsSecret)
                    {
                        CreateSecretDoorLink(
                            leftIsSecret ? leftPos : rightPos, leftIsSecret ? Vector2.left : Vector2.right,
                            leftIsSecret ? rightPos : leftPos, leftIsSecret ? Vector2.right : Vector2.left,
                            true, secretWallSprite, root.transform);
                    }
                    else
                    {
                        AddDoorInfo(doorsByRoom, cell, leftPos, true);
                        AddDoorInfo(doorsByRoom, rightCell, rightPos, true);
                        pendingDoorLinks.Add((leftPos, Vector2.left, cell, rightPos, Vector2.right, rightCell, true));
                    }
                }
            }
            Vector2Int upCell = cell + Vector2Int.up;
            if (layout.ContainsKey(upCell))
            {
                if (SameGroup(cellGroups, cell, upCell))
                {
                    // Same as the horizontal case above - already one continuous floor.
                }
                else
                {
                    bool bottomIsSecret = kv.Value == RoomType.Secret;
                    bool topIsSecret = layout[upCell] == RoomType.Secret;
                    bool useGateOffsetV = bottomIsSecret || topIsSecret || IsGatedRoomType(kv.Value) || IsGatedRoomType(layout[upCell]);
                    int doorX = (useGateOffsetV ? CenteredDoorOffset(RoomWidth) : RandomDoorOffset(RoomWidth)) + cell.x * StepX;
                    CarveVerticalDoor(cell.y * StepY, (cell.y + 1) * StepY, doorX, doorX, floorMap, wallsMap, floorTile);

                    Vector2 bottomPos = new Vector2(doorX + DoorWidth / 2f, cell.y * StepY + RoomHeight - 0.5f);
                    Vector2 topPos = new Vector2(doorX + DoorWidth / 2f, (cell.y + 1) * StepY + 0.5f);

                    if (bottomIsSecret || topIsSecret)
                    {
                        CreateSecretDoorLink(
                            bottomIsSecret ? bottomPos : topPos, bottomIsSecret ? Vector2.down : Vector2.up,
                            bottomIsSecret ? topPos : bottomPos, bottomIsSecret ? Vector2.up : Vector2.down,
                            false, secretWallSprite, root.transform);
                    }
                    else
                    {
                        AddDoorInfo(doorsByRoom, cell, bottomPos, false);
                        AddDoorInfo(doorsByRoom, upCell, topPos, false);
                        pendingDoorLinks.Add((bottomPos, Vector2.down, cell, topPos, Vector2.up, upCell, false));
                    }
                }
            }
        }

        // Tint Boss/Safe room walls (2026-09-16 request) - runs after every door has been carved
        // above, so a tile removed for a doorway is correctly skipped rather than recolored for
        // nothing. Group-aware like the merge repaint pass earlier (a Boss arena can span up to 4
        // cells - see MergeMultiCellRooms - Safe is never merged, so its group rect is always its own
        // single cell) so a merged room gets one consistent tint across its whole footprint instead
        // of a seam at the former cell boundary.
        var tintedGroups = new HashSet<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            Color tint;
            if (kv.Value == RoomType.Boss) tint = BossWallTint;
            else if (kv.Value == RoomType.Safe) tint = SafeWallTint;
            else continue;

            RectInt group = cellGroups[kv.Key];
            Vector2Int groupKey = new Vector2Int(group.xMin, group.yMin);
            if (!tintedGroups.Add(groupKey)) continue; // one pass per group, not per member cell

            Rect worldRect = group.width * group.height > 1
                ? GroupWorldRect(group)
                : new Rect(kv.Key.x * StepX, kv.Key.y * StepY, RoomWidth, RoomHeight);
            int gxMin = Mathf.RoundToInt(worldRect.xMin), gyMin = Mathf.RoundToInt(worldRect.yMin);
            int gw = Mathf.RoundToInt(worldRect.width), gh = Mathf.RoundToInt(worldRect.height);
            for (int x = 0; x < gw; x++)
            {
                for (int y = 0; y < gh; y++)
                {
                    Vector3Int pos = new Vector3Int(gxMin + x, gyMin + y, 0);
                    if (wallsMap.GetTile(pos) == null) continue; // door opening cut into this wall
                    wallsMap.SetTileFlags(pos, TileFlags.None);
                    wallsMap.SetColor(pos, tint);
                }
            }
        }

        // --- Player ---
        Vector2Int startCell = Vector2Int.zero;
        Vector2 startWorld = new Vector2(startCell.x * StepX + RoomWidth / 2f, startCell.y * StepY + RoomHeight / 2f);

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(Stamina), typeof(Mana), typeof(PlayerInventory), typeof(PlayerEquipment), typeof(PlayerLimbs), typeof(PlayerStats), typeof(PlayerSkills), typeof(StatusIconDisplay), typeof(PlayerController));
        player.transform.SetParent(root.transform);
        player.transform.position = startWorld;
        player.tag = "Player";

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        playerRenderer.sprite = playerSprite;
        playerRenderer.sortingOrder = 0;

        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;

        player.GetComponent<CircleCollider2D>().radius = 0.4f;
        PlayerController playerController = player.GetComponent<PlayerController>();
        playerController.projectileSprite = projectileSprite;
        playerController.fistVisualSprite = fistVisualSprite;
        playerController.swordVisualSprite = swordVisualSprite;
        playerController.explosionSprite = explosionSprite;
        playerController.lightningOrbSprite = lightningOrbSprite;
        playerController.lightningBoltSprite = lightningBoltSprite;
        playerController.fireballSprite = fireballSprite;
        playerController.fireLineSprite = fireLineSprite;
        playerController.burnIconSprite = burnIconSprite;
        playerController.movementDebuffIcon = LoadIconPackSprite("Padlock01_Bright");
        playerController.speedBuffIcon = LoadIconPackSprite("Thunder_Bright");
        playerController.adrenalineBuffIcon = LoadIconPackSprite("Potion02_Bright");
        PlayerInventory playerInventory = player.GetComponent<PlayerInventory>();
        PlayerEquipment playerEquipment = player.GetComponent<PlayerEquipment>();
        PlayerSkills playerSkills = player.GetComponent<PlayerSkills>();
        PlayerLimbs playerLimbs = player.GetComponent<PlayerLimbs>();
        // Starting hotbar loadout - the player can rearrange these later via drag & drop.
        playerInventory.hotbarSlots[0] = ItemIds.Shuriken;
        playerInventory.hotbarSlots[1] = ItemIds.Caillou;
        playerInventory.hotbarSlots[2] = ItemIds.Baton;
        playerInventory.hotbarSlots[3] = ItemIds.Bomb;
        PlayerStats playerStats = player.GetComponent<PlayerStats>();
        playerEquipment.stats = playerStats;
        playerEquipment.fireIcon = CreateCircleSprite("Assets/Art/Fx/Fire.png", new Color(0.95f, 0.35f, 0.1f));
        Health playerHealth = player.GetComponent<Health>();
        Stamina playerStamina = player.GetComponent<Stamina>();
        Mana playerMana = player.GetComponent<Mana>();
        // No explicit maxHealth/currentHealth assignment here anymore - PlayerStats.Awake()
        // already set both correctly via PlayerLimbs (see PlayerLimbs.BaseMaxFor/
        // SetConstitutionBonus) by the time this line runs (Awake fires per-AddComponent, and
        // PlayerStats is listed after Health in the Player constructor above).

        // --- Room content (enemies / special-room markers) ---
        var roomEntries = new List<RoomCameraController.RoomEntry>();
        var monsterRoomControllers = new List<RoomController>();
        var bossRoomControllers = new List<BossRoomController>();
        int eliteRoomCount = 0;
        Vector2 stairsCenter = Vector2.zero;
        Vector2Int stairsGridPos = Vector2Int.zero;
        bool hasStairs = false;
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            int originX = kv.Key.x * StepX;
            int originY = kv.Key.y * StepY;
            // `rect` stays this one cell's own bounds (drives exactly-which-cell detection, so
            // MinimapController's walked-over reveal and RoomController's per-cell gating keep
            // firing per member cell same as before). `cameraRect` spans the WHOLE merged group
            // (see OpenFullHorizontalSeam/OpenFullVerticalSeam) so the camera doesn't recentre on
            // a single 22x12 cell the instant the player crosses a former seam inside a duo/trio/
            // quad room - reduces to the same single-cell rect automatically when unmerged.
            roomEntries.Add(new RoomCameraController.RoomEntry
            {
                gridPos = kv.Key,
                rect = new Rect(originX, originY, RoomWidth, RoomHeight),
                cameraRect = GroupWorldRect(cellGroups[kv.Key])
            });

            if (kv.Value == RoomType.Monster)
            {
                // A multi-cell room is only ever set up once, from its anchor (top/bottom-left)
                // cell - the other member cells contribute nothing here beyond the camera-rect
                // entry already added above, which is what lets the camera scroll across them.
                if (!IsGroupAnchor(kv.Key, cellGroups)) continue;

                GatherGroup(kv.Key, cellGroups, doorsByRoom, out List<Vector2Int> memberCells, out var doors, out Vector2 groupSize);

                bool startCleared = memberCells.Exists(c => clearedRoomsThisFloor.Contains(c));
                bool hasElite = SetupMonsterRoom(memberCells, originX, originY, groupSize, root.transform, player.transform,
                    enemyPresets, speedUpBadge, hpUpBadge, enemyGlowSprite, doorBarrierSprite, decorSprites, floorTheme, doors, monsterRoomControllers, startCleared);
                if (hasElite) eliteRoomCount++;
            }
            else if (kv.Value == RoomType.Boss)
            {
                // Same anchor-only rule as Monster - a merged Boss arena (see BossArenaMergeChance)
                // is set up once from its anchor cell too.
                if (!IsGroupAnchor(kv.Key, cellGroups)) continue;

                GatherGroup(kv.Key, cellGroups, doorsByRoom, out List<Vector2Int> memberCells, out var doors, out Vector2 groupSize);

                PopulateRoom(kv.Value, originX, originY, root.transform, player.transform,
                    shopMarker, treasureMarker, secretMarker, gambleMarker, bossMarker, eventMarker, safeMarker, chestSprite);

                // The merge pass above can reposition which cell ends up as the group's technical
                // anchor (bottom-left corner) - the cell GenerateLayout actually assigned a tier to
                // is still guaranteed to be SOME member of this group, just maybe not kv.Key itself.
                Vector2Int tieredCell = memberCells.Find(c => bossTiers.ContainsKey(c));
                BossTier tier = bossTiers.TryGetValue(tieredCell, out BossTier foundTier) ? foundTier : BossTier.Ville;
                // Each tier got its OWN distinct biome/family (see bossTierBiomes, populated
                // alongside bossTiers in GenerateLayout/just below) - CurrentBiome only remains the
                // fallback for the rare tier that failed to place (bossTiers has no entry for it,
                // see PlaceSpecialRoom's no-fallback tiers).
                BossFamily family = BossFamilyFor(bossTierBiomes.TryGetValue(tier, out Biome tierBiome) ? tierBiome : CurrentBiome);
                BossTierStats tierStats = BossTierStatsFor(tier, CurrentFloor);

                SetupBossRoom(kv.Key, memberCells, originX, originY, groupSize, root.transform, player.transform,
                    family, tier, tierStats, bossProjectileSprite, bossSlobberPuddleSprite, doorBarrierSprite, doors, bossRoomControllers, bossDefeatedThisFloor);
            }
            else
            {
                PopulateRoom(kv.Value, originX, originY, root.transform, player.transform,
                    shopMarker, treasureMarker, secretMarker, gambleMarker, bossMarker, eventMarker, safeMarker, chestSprite);

                if (kv.Value == RoomType.Event)
                {
                    Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    SpawnExampleNpc(center + new Vector2(2f, 0f), npcStrangerSprite, npcBadgeSprite, root.transform);
                }

                if (kv.Value == RoomType.Safe)
                {
                    Vector2 roomOrigin = new Vector2(originX, originY);
                    Vector2 center = roomOrigin + new Vector2(RoomWidth / 2f, RoomHeight / 2f);
                    Vector2 npcPos = center + new Vector2(2f, 0f);
                    if (kv.Key == restaurantCell)
                    {
                        SpawnCookNpc(npcPos, npcStrangerSprite, npcBadgeSprite, root.transform);
                        SpawnRestaurantFurniture(roomOrigin, new Vector2(RoomWidth, RoomHeight), npcPos, furnitureWoodSprite, rugSprite, wallDecorSprite, root.transform);
                    }
                    else if (kv.Key == arcadeCell)
                    {
                        List<(Vector2 pos, bool onVerticalWall)> safeDoors = doorsByRoom.TryGetValue(kv.Key, out var sd) ? sd : new List<(Vector2, bool)>();
                        switch (arcadeVariant)
                        {
                            case SafeRoomVariant.FlowerGarden:
                                SpawnFlowerGardenContent(roomOrigin, new Vector2(RoomWidth, RoomHeight), safeDoors, center, wallDecorSprite, root.transform);
                                break;
                            case SafeRoomVariant.MaterialStorage:
                                SpawnMaterialStorageContent(roomOrigin, new Vector2(RoomWidth, RoomHeight), safeDoors, center, shopCrateSprite, wallDecorSprite, root.transform);
                                break;
                            default:
                                SpawnArcadeNpc(npcPos, npcStrangerSprite, npcBadgeSprite, root.transform);
                                SpawnArcadeFurniture(roomOrigin, new Vector2(RoomWidth, RoomHeight), npcPos, furnitureWoodSprite, rugSprite, wallDecorSprite, root.transform);
                                break;
                        }
                    }
                    else
                    {
                        SpawnTavernNpc(npcPos, npcElderSprite, npcBadgeSprite, root.transform);
                        SpawnCraftingTable(center + new Vector2(-2f, 0f), craftingTableSprite, npcBadgeSprite, root.transform);
                        SpawnAlchemyTable(center + new Vector2(2f, 0f), alchemyTableSprite, npcBadgeSprite, root.transform);
                        SpawnTavernFurniture(roomOrigin, new Vector2(RoomWidth, RoomHeight), npcPos, furnitureWoodSprite, rugSprite, wallDecorSprite, root.transform,
                            playerHealth, playerLimbs, playerInventory, playerStats, playerStamina, playerController, playerEquipment);
                    }
                }

                if (kv.Value == RoomType.Shop)
                {
                    Vector2 roomOrigin = new Vector2(originX, originY);
                    Vector2 center = roomOrigin + new Vector2(RoomWidth / 2f, RoomHeight / 2f);
                    SpawnMerchantNpc(center, npcMerchantSprite, npcBadgeSprite, root.transform);
                    SpawnShopFurniture(roomOrigin, new Vector2(RoomWidth, RoomHeight), center, shopCrateSprite, rugSprite, wallDecorSprite, root.transform);
                    // The floor's one optional quest-giver (2026-09-21 request) - tucked in a free
                    // corner of the Shop room, clear of both the crate/shelf cluster and the wall
                    // decor around the Marchand (see SpawnShopFurniture).
                    SpawnQuestNpc(center + new Vector2(-7f, -3f), npcStrangerSprite, npcBadgeSprite, root.transform, CurrentFloor);
                }

                if (kv.Value == RoomType.Stairs)
                {
                    stairsCenter = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    stairsGridPos = kv.Key;
                    hasStairs = true;
                }

                // Stone blocks everywhere except Start (no obstacles blocking the initial pickups),
                // Boss (kept clear for the fight), Safe, Shop and Stairs (all guaranteed hazard-free by design).
                if (kv.Value != RoomType.Start && kv.Value != RoomType.Boss && kv.Value != RoomType.Safe
                    && kv.Value != RoomType.Shop && kv.Value != RoomType.Stairs)
                {
                    Vector2 roomOrigin = new Vector2(originX, originY);
                    Vector2 center = roomOrigin + new Vector2(RoomWidth / 2f, RoomHeight / 2f);
                    List<(Vector2 pos, bool onVerticalWall)> roomDoors = doorsByRoom.TryGetValue(kv.Key, out var rd) ? rd : new List<(Vector2, bool)>();
                    SpawnRoomDecor(roomOrigin, new Vector2(RoomWidth, RoomHeight), roomDoors, new List<Vector2> { center }, decorSprites, root.transform);
                }
            }
        }

        // Now that every Monster/Boss room's controller exists, create the door triggers and wire
        // each one to the lock state of the room it sits in.
        var monsterControllers = new Dictionary<Vector2Int, RoomController>();
        foreach (RoomController rc in monsterRoomControllers)
            foreach (Vector2Int member in rc.memberCells) monsterControllers[member] = rc;
        var bossControllers = new Dictionary<Vector2Int, BossRoomController>();
        foreach (BossRoomController bc in bossRoomControllers) bossControllers[bc.gridPos] = bc;

        foreach (var link in pendingDoorLinks)
        {
            List<DoorTrigger> triggersA = GetExitTriggerList(link.cellA, monsterControllers, bossControllers);
            List<DoorTrigger> triggersB = GetExitTriggerList(link.cellB, monsterControllers, bossControllers);
            List<GameObject> blockersA = GetDoorBlockerList(link.cellA, monsterControllers, bossControllers);
            List<GameObject> blockersB = GetDoorBlockerList(link.cellB, monsterControllers, bossControllers);
            CreateDoorLink(link.posA, link.inwardA, triggersA, blockersA, link.posB, link.inwardB, triggersB, blockersB, root.transform);
        }


        // --- Camera: locked per-room instead of following the player continuously ---
        Camera cam = Camera.main;
        RoomCameraController roomCam = null;
        if (cam != null)
        {
            cam.orthographic = true;
            // Matches the room's own height exactly, so it fills the screen vertically with no
            // letterboxing (a 22-wide room then slightly overscans a 16:9 view horizontally).
            cam.orthographicSize = RoomHeight / 2f;
            cam.transform.position = new Vector3(startWorld.x, startWorld.y, cam.transform.position.z);

            roomCam = cam.GetComponent<RoomCameraController>();
            if (roomCam == null) roomCam = cam.gameObject.AddComponent<RoomCameraController>();
            // This component lives on the persistent Main Camera, reused across every floor - but
            // OnRoomEntered is a bare C# event with no owning object to unsubscribe it, unlike
            // RoomController/BossRoomController's own subscriptions (cleaned up in their OnDestroy).
            // Without clearing it here, the previous floor's inline lambda below (and its captured
            // RoomAnnouncementUI, already destroyed along with the old DungeonRoot/Canvas) stayed
            // subscribed forever - every future OnRoomEntered fired ALL of them, crashing with
            // MissingReferenceException the first time this floor's camera crossed a room boundary.
            roomCam.ClearListeners();
            roomCam.target = player.transform;
            roomCam.rooms = roomEntries.ToArray();

            foreach (RoomController rc in monsterRoomControllers) rc.roomCamera = roomCam;
            foreach (BossRoomController bc in bossRoomControllers) bc.roomCamera = roomCam;

            // Sort same-order sprites by world Y (further up the screen = further away) so the
            // player correctly passes behind tall wall tops and in front of near ones, Isaac-style.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
        }

        // --- Heart HUD ---
        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform);
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        // Shared hover tooltip (item name/description) - see InventorySlotUI.OnPointerEnter.
        GameObject tooltipGO = new GameObject("TooltipUI", typeof(TooltipUI));
        tooltipGO.transform.SetParent(canvasGO.transform, false);

        // Required for UI pointer/drag events (inventory drag & drop) - the project's Active Input
        // Handling is Input System (New) only, so the legacy StandaloneInputModule doesn't work.
        // Reuses one if it already exists (MainMenuController creates one to run its own buttons
        // before a floor exists at all) - a second EventSystem in the scene fights the first one.
        GameObject eventSystemGO = GameObject.Find("EventSystem");
        if (eventSystemGO == null) eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemGO.transform.SetParent(root.transform);

        // Shared by every fillAmount-driven bar below (Health/Stamina/Experience/Boss health) - an
        // Image left with no sprite at all apparently never got its Filled-type geometry to
        // actually redraw on fillAmount changes (confirmed via diagnostic logging: the numeric
        // value was always correct, the bar itself never visibly moved). A plain white sprite,
        // tinted per bar via Image.color exactly like every other UI graphic here, fixes that.
        Sprite uiFillSprite = CreateSolidSprite("Assets/Art/UI/Fill.png", Color.white);

        // --- Health bar (top-left, always visible) - a fill bar + numeric readout, not discrete
        // heart icons (see HeartHUD.cs): per-limb HP (PlayerLimbs) pushes the real total into the
        // tens/hundreds, far past what a handful of heart slots could represent.
        GameObject heartBarGO = new GameObject("HeartBar", typeof(RectTransform), typeof(Image), typeof(HeartHUD));
        heartBarGO.transform.SetParent(canvasGO.transform, false);
        Image heartBarBackground = heartBarGO.GetComponent<Image>();
        heartBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform heartBarRect = heartBarBackground.rectTransform;
        heartBarRect.anchorMin = heartBarRect.anchorMax = new Vector2(0f, 1f);
        heartBarRect.pivot = new Vector2(0f, 1f);
        heartBarRect.anchoredPosition = new Vector2(20f, -20f);
        heartBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject heartFillGO = new GameObject("Fill", typeof(Image));
        heartFillGO.transform.SetParent(heartBarGO.transform, false);
        Image heartFill = heartFillGO.GetComponent<Image>();
        heartFill.sprite = uiFillSprite;
        heartFill.color = new Color(0.85f, 0.15f, 0.2f);
        heartFill.type = Image.Type.Filled;
        heartFill.fillMethod = Image.FillMethod.Horizontal;
        heartFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform heartFillRect = heartFill.rectTransform;
        heartFillRect.anchorMin = Vector2.zero;
        heartFillRect.anchorMax = Vector2.one;
        heartFillRect.offsetMin = new Vector2(1f, 1f);
        heartFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject heartLabelGO = new GameObject("HeartLabel", typeof(Text));
        heartLabelGO.transform.SetParent(canvasGO.transform, false);
        Text heartLabel = heartLabelGO.GetComponent<Text>();
        heartLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        heartLabel.fontSize = 18;
        heartLabel.alignment = TextAnchor.MiddleLeft;
        heartLabel.color = Color.white;
        RectTransform heartLabelRect = heartLabel.rectTransform;
        heartLabelRect.anchorMin = heartLabelRect.anchorMax = new Vector2(0f, 1f);
        heartLabelRect.pivot = new Vector2(0f, 1f);
        heartLabelRect.anchoredPosition = new Vector2(188f, -20f);
        heartLabelRect.sizeDelta = new Vector2(100f, 24f);

        HeartHUD hud = heartBarGO.GetComponent<HeartHUD>();
        hud.target = playerHealth;
        hud.fill = heartFill;
        hud.label = heartLabel;

        // --- Stamina bar (directly under the health bar, always visible) ---
        GameObject staminaBarGO = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(StaminaBarUI));
        staminaBarGO.transform.SetParent(canvasGO.transform, false);
        Image staminaBarBackground = staminaBarGO.GetComponent<Image>();
        staminaBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform staminaBarRect = staminaBarBackground.rectTransform;
        staminaBarRect.anchorMin = staminaBarRect.anchorMax = new Vector2(0f, 1f);
        staminaBarRect.pivot = new Vector2(0f, 1f);
        staminaBarRect.anchoredPosition = new Vector2(20f, -52f);
        staminaBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject staminaFillGO = new GameObject("Fill", typeof(Image));
        staminaFillGO.transform.SetParent(staminaBarGO.transform, false);
        Image staminaFill = staminaFillGO.GetComponent<Image>();
        staminaFill.sprite = uiFillSprite;
        staminaFill.color = new Color(0.75f, 0.7f, 0.15f);
        staminaFill.type = Image.Type.Filled;
        staminaFill.fillMethod = Image.FillMethod.Horizontal;
        staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform staminaFillRect = staminaFill.rectTransform;
        staminaFillRect.anchorMin = Vector2.zero;
        staminaFillRect.anchorMax = Vector2.one;
        staminaFillRect.offsetMin = new Vector2(1f, 1f);
        staminaFillRect.offsetMax = new Vector2(-1f, -1f);

        // Numeric readout next to the bar - a short sprint tap barely dents the bar visually and
        // fully regenerates within under a second, easy to miss; exact numbers make any change
        // to currentStamina unmistakable regardless of how subtle the bar itself looks.
        GameObject staminaLabelGO = new GameObject("StaminaLabel", typeof(Text));
        staminaLabelGO.transform.SetParent(canvasGO.transform, false);
        Text staminaLabel = staminaLabelGO.GetComponent<Text>();
        staminaLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        staminaLabel.fontSize = 18;
        staminaLabel.alignment = TextAnchor.MiddleLeft;
        staminaLabel.color = Color.white;
        RectTransform staminaLabelRect = staminaLabel.rectTransform;
        staminaLabelRect.anchorMin = staminaLabelRect.anchorMax = new Vector2(0f, 1f);
        staminaLabelRect.pivot = new Vector2(0f, 1f);
        staminaLabelRect.anchoredPosition = new Vector2(188f, -52f);
        staminaLabelRect.sizeDelta = new Vector2(80f, 24f);

        StaminaBarUI staminaBar = staminaBarGO.GetComponent<StaminaBarUI>();
        staminaBar.target = playerStamina;
        staminaBar.fill = staminaFill;
        staminaBar.label = staminaLabel;

        // --- Mana bar (directly under the stamina bar, always visible) - drains when casting a
        // spell (see PlayerController.TryCastLightningOrb), fed by Intelligence via PlayerStats. ---
        GameObject manaBarGO = new GameObject("ManaBar", typeof(RectTransform), typeof(Image), typeof(ManaBarUI));
        manaBarGO.transform.SetParent(canvasGO.transform, false);
        Image manaBarBackground = manaBarGO.GetComponent<Image>();
        manaBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform manaBarRect = manaBarBackground.rectTransform;
        manaBarRect.anchorMin = manaBarRect.anchorMax = new Vector2(0f, 1f);
        manaBarRect.pivot = new Vector2(0f, 1f);
        manaBarRect.anchoredPosition = new Vector2(20f, -84f);
        manaBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject manaFillGO = new GameObject("Fill", typeof(Image));
        manaFillGO.transform.SetParent(manaBarGO.transform, false);
        Image manaFill = manaFillGO.GetComponent<Image>();
        manaFill.sprite = uiFillSprite;
        manaFill.color = new Color(0.3f, 0.5f, 0.9f);
        manaFill.type = Image.Type.Filled;
        manaFill.fillMethod = Image.FillMethod.Horizontal;
        manaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform manaFillRect = manaFill.rectTransform;
        manaFillRect.anchorMin = Vector2.zero;
        manaFillRect.anchorMax = Vector2.one;
        manaFillRect.offsetMin = new Vector2(1f, 1f);
        manaFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject manaLabelGO = new GameObject("ManaLabel", typeof(Text));
        manaLabelGO.transform.SetParent(canvasGO.transform, false);
        Text manaLabel = manaLabelGO.GetComponent<Text>();
        manaLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        manaLabel.fontSize = 18;
        manaLabel.alignment = TextAnchor.MiddleLeft;
        manaLabel.color = Color.white;
        RectTransform manaLabelRect = manaLabel.rectTransform;
        manaLabelRect.anchorMin = manaLabelRect.anchorMax = new Vector2(0f, 1f);
        manaLabelRect.pivot = new Vector2(0f, 1f);
        manaLabelRect.anchoredPosition = new Vector2(188f, -84f);
        manaLabelRect.sizeDelta = new Vector2(80f, 24f);

        ManaBarUI manaBar = manaBarGO.GetComponent<ManaBarUI>();
        manaBar.target = playerMana;
        manaBar.fill = manaFill;
        manaBar.label = manaLabel;

        // --- Gold counter (below the hearts) ---
        GameObject goldGO = new GameObject("GoldCounter", typeof(RectTransform), typeof(GoldCounterUI));
        goldGO.transform.SetParent(canvasGO.transform, false);
        RectTransform goldRect = goldGO.GetComponent<RectTransform>();
        goldRect.anchorMin = Vector2.zero;
        goldRect.anchorMax = Vector2.one;
        goldRect.offsetMin = Vector2.zero;
        goldRect.offsetMax = Vector2.zero;

        GoldCounterUI goldCounter = goldGO.GetComponent<GoldCounterUI>();
        goldCounter.inventory = playerInventory;
        goldCounter.yOffset = -116f; // leaves room for the stamina AND mana bars above

        // --- Experience bar (below the gold counter, which is 48px tall) - no XP/level visual
        // existed at all before ---
        GameObject xpBarGO = new GameObject("ExperienceBar", typeof(RectTransform), typeof(Image), typeof(ExperienceBarUI));
        xpBarGO.transform.SetParent(canvasGO.transform, false);
        Image xpBarBackground = xpBarGO.GetComponent<Image>();
        xpBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform xpBarRect = xpBarBackground.rectTransform;
        xpBarRect.anchorMin = xpBarRect.anchorMax = new Vector2(0f, 1f);
        xpBarRect.pivot = new Vector2(0f, 1f);
        xpBarRect.anchoredPosition = new Vector2(20f, -172f);
        xpBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject xpFillGO = new GameObject("Fill", typeof(Image));
        xpFillGO.transform.SetParent(xpBarGO.transform, false);
        Image xpFill = xpFillGO.GetComponent<Image>();
        xpFill.sprite = uiFillSprite;
        xpFill.color = new Color(0.4f, 0.65f, 0.9f);
        xpFill.type = Image.Type.Filled;
        xpFill.fillMethod = Image.FillMethod.Horizontal;
        xpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform xpFillRect = xpFill.rectTransform;
        xpFillRect.anchorMin = Vector2.zero;
        xpFillRect.anchorMax = Vector2.one;
        xpFillRect.offsetMin = new Vector2(1f, 1f);
        xpFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject xpLabelGO = new GameObject("ExperienceLabel", typeof(Text));
        xpLabelGO.transform.SetParent(canvasGO.transform, false);
        Text xpLabel = xpLabelGO.GetComponent<Text>();
        xpLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        xpLabel.fontSize = 18;
        xpLabel.alignment = TextAnchor.MiddleLeft;
        xpLabel.color = Color.white;
        RectTransform xpLabelRect = xpLabel.rectTransform;
        xpLabelRect.anchorMin = xpLabelRect.anchorMax = new Vector2(0f, 1f);
        xpLabelRect.pivot = new Vector2(0f, 1f);
        xpLabelRect.anchoredPosition = new Vector2(188f, -172f);
        xpLabelRect.sizeDelta = new Vector2(100f, 24f);

        ExperienceBarUI xpBar = xpBarGO.GetComponent<ExperienceBarUI>();
        xpBar.target = playerStats;
        xpBar.fill = xpFill;
        xpBar.label = xpLabel;

        // --- Floor timer (top-center countdown) - 10 minutes, then the floor collapses ---
        GameObject floorTimerGO = new GameObject("FloorTimer", typeof(FloorTimer));
        floorTimerGO.transform.SetParent(root.transform);
        FloorTimer floorTimer = floorTimerGO.GetComponent<FloorTimer>();
        floorTimer.duration = FloorDuration;
        // A collapsing floor is an unavoidable death, unlike ordinary damage - bypasses dodge/i-frames.
        floorTimer.OnCollapse += () => { Debug.Log("Le sol s'effondre !"); playerHealth.Kill(); };

        // Floor + biome identity, right above the countdown - a Souls-like floor should announce
        // itself the way Dungeon Crawler Carl's do (named, themed), not just be a color swap.
        GameObject floorInfoLabelGO = new GameObject("FloorInfoLabel", typeof(Text));
        floorInfoLabelGO.transform.SetParent(canvasGO.transform, false);
        Text floorInfoLabel = floorInfoLabelGO.GetComponent<Text>();
        floorInfoLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 22);
        floorInfoLabel.fontSize = 22;
        floorInfoLabel.alignment = TextAnchor.MiddleCenter;
        floorInfoLabel.color = new Color(0.85f, 0.85f, 0.9f);
        floorInfoLabel.text = "Etage " + floor + " - " + biomeTheme.displayName;
        RectTransform floorInfoLabelRect = floorInfoLabel.rectTransform;
        floorInfoLabelRect.anchorMin = floorInfoLabelRect.anchorMax = new Vector2(0.5f, 1f);
        floorInfoLabelRect.pivot = new Vector2(0.5f, 1f);
        floorInfoLabelRect.anchoredPosition = new Vector2(0f, -20f);
        floorInfoLabelRect.sizeDelta = new Vector2(320f, 30f);

        GameObject floorTimerLabelGO = new GameObject("FloorTimerLabel", typeof(Text));
        floorTimerLabelGO.transform.SetParent(canvasGO.transform, false);
        Text floorTimerLabel = floorTimerLabelGO.GetComponent<Text>();
        floorTimerLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 32);
        floorTimerLabel.fontSize = 32;
        floorTimerLabel.alignment = TextAnchor.MiddleCenter;
        floorTimerLabel.color = Color.white;
        RectTransform floorTimerLabelRect = floorTimerLabel.rectTransform;
        floorTimerLabelRect.anchorMin = floorTimerLabelRect.anchorMax = new Vector2(0.5f, 1f);
        floorTimerLabelRect.pivot = new Vector2(0.5f, 1f);
        floorTimerLabelRect.anchoredPosition = new Vector2(0f, -54f);
        floorTimerLabelRect.sizeDelta = new Vector2(160f, 44f);

        FloorTimerUI floorTimerUI = floorTimerLabelGO.AddComponent<FloorTimerUI>();
        floorTimerUI.target = floorTimer;
        floorTimerUI.label = floorTimerLabel;

        // Now that the floor's single Boss room (if any) has its controller and FloorTimer exists,
        // the staircase can wire whichever lock it rolled.
        if (hasStairs)
        {
            SetupStaircase(stairsCenter, stairsGridPos, layout, stairsMarker, stairsCageSprite, leverSprite,
                floorTimer, bossRoomControllers, root.transform);
        }

        // --- Character sheet (full recap of all 8 stats + 4 skill levels, toggled with C) - the
        // ONLY place either is shown (2026-09-16 request: no more always-on HUD columns for them,
        // see the now-deleted StatsUI/SkillsUI) ---
        GameObject characterSheetGO = new GameObject("CharacterSheetUI", typeof(RectTransform), typeof(CharacterSheetUI));
        characterSheetGO.transform.SetParent(canvasGO.transform, false);
        RectTransform characterSheetRect = characterSheetGO.GetComponent<RectTransform>();
        characterSheetRect.anchorMin = Vector2.zero;
        characterSheetRect.anchorMax = Vector2.one;
        characterSheetRect.offsetMin = Vector2.zero;
        characterSheetRect.offsetMax = Vector2.zero;
        CharacterSheetUI characterSheet = characterSheetGO.GetComponent<CharacterSheetUI>();
        characterSheet.stats = playerStats;
        characterSheet.skills = playerSkills;

        // --- Spell book (recap of every known spell, toggled with K) ---
        GameObject spellBookGO = new GameObject("SpellBookUI", typeof(RectTransform), typeof(SpellBookUI));
        spellBookGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBookRect = spellBookGO.GetComponent<RectTransform>();
        spellBookRect.anchorMin = Vector2.zero;
        spellBookRect.anchorMax = Vector2.one;
        spellBookRect.offsetMin = Vector2.zero;
        spellBookRect.offsetMax = Vector2.zero;
        SpellBookUI spellBook = spellBookGO.GetComponent<SpellBookUI>();
        spellBook.controller = playerController;
        spellBook.mana = player.GetComponent<Mana>();
        spellBook.stats = playerStats;

        // --- Hotbar (throwable consumables, slots 1-3 used today) ---
        GameObject hotbarGO = new GameObject("HotbarUI", typeof(RectTransform), typeof(HotbarUI));
        hotbarGO.transform.SetParent(canvasGO.transform, false);
        RectTransform hotbarRect = hotbarGO.GetComponent<RectTransform>();
        hotbarRect.anchorMin = Vector2.zero;
        hotbarRect.anchorMax = Vector2.one;
        hotbarRect.offsetMin = Vector2.zero;
        hotbarRect.offsetMax = Vector2.zero;

        HotbarUI hotbar = hotbarGO.GetComponent<HotbarUI>();
        hotbar.inventory = playerInventory;
        hotbar.slotSize = 56f;
        hotbar.spacing = 64f;
        hotbar.fontSize = 36;

        // --- Spell bar (5 slots, sits just above the hotbar - click a slot then an arrow key to
        // cast, same arm-then-aim pattern as a hotbar throwable) ---
        GameObject spellBarGO = new GameObject("SpellBarUI", typeof(RectTransform), typeof(SpellBarUI));
        spellBarGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBarRect = spellBarGO.GetComponent<RectTransform>();
        spellBarRect.anchorMin = Vector2.zero;
        spellBarRect.anchorMax = Vector2.one;
        spellBarRect.offsetMin = Vector2.zero;
        spellBarRect.offsetMax = Vector2.zero;

        SpellBarUI spellBar = spellBarGO.GetComponent<SpellBarUI>();
        spellBar.controller = playerController;
        spellBar.slotSize = 56f;
        spellBar.spacing = 64f;
        spellBar.rowYOffset = 84f; // clears the hotbar row (20..76) with a small gap

        // --- Inventory screen (grid, toggled with the I key) ---
        GameObject inventoryGO = new GameObject("InventoryUI", typeof(RectTransform), typeof(InventoryUI));
        inventoryGO.transform.SetParent(canvasGO.transform, false);
        RectTransform inventoryRect = inventoryGO.GetComponent<RectTransform>();
        inventoryRect.anchorMin = Vector2.zero;
        inventoryRect.anchorMax = Vector2.one;
        inventoryRect.offsetMin = Vector2.zero;
        inventoryRect.offsetMax = Vector2.zero;

        InventoryUI inventoryUI = inventoryGO.GetComponent<InventoryUI>();
        inventoryUI.inventory = playerInventory;
        inventoryUI.equipment = playerEquipment;
        inventoryUI.player = playerController;
        inventoryUI.limbs = playerLimbs;
        inventoryUI.slotSize = 72f;
        inventoryUI.spacing = 84f;
        inventoryUI.fontSize = 32;

        // The inventory panel's full-screen dimming background sits above the hotbar in the
        // canvas (built earlier, so an earlier/lower sibling) and defaults to blocking raycasts
        // like any other Image - with the panel open, a drag released over the hotbar hit that
        // background first and never reached HotbarUI's own slots underneath, so nothing could
        // ever be dropped there. Bumping the hotbar to a later sibling than the inventory panel
        // (but still before every full-screen overlay built after this point - pause/death/etc.,
        // which should stay on top of everything including the hotbar) fixes the raycast order
        // without touching the panel's own dimming.
        hotbarGO.transform.SetAsLastSibling();

        // --- Minimap (top-right): adjacent rooms half-reveal, entered rooms fully reveal ---
        GameObject minimapGO = new GameObject("Minimap", typeof(RectTransform), typeof(MinimapController));
        minimapGO.transform.SetParent(canvasGO.transform, false);
        RectTransform minimapRect = minimapGO.GetComponent<RectTransform>();
        minimapRect.anchorMin = minimapRect.anchorMax = new Vector2(1f, 1f);
        minimapRect.pivot = new Vector2(1f, 1f);
        minimapRect.anchoredPosition = new Vector2(-20f, -20f);
        minimapRect.sizeDelta = new Vector2(240f, 240f);

        MinimapController minimap = minimapGO.GetComponent<MinimapController>();
        minimap.roomCamera = cam != null ? cam.GetComponent<RoomCameraController>() : null;
        minimap.monsterRooms = monsterRoomControllers.ToArray();
        minimap.allRoomGridPositions = new List<Vector2Int>(layout.Keys);
        minimap.secretRoomGridPositions = new List<Vector2Int>();
        minimap.bossRoomGridPositions = new List<Vector2Int>();
        minimap.shopRoomGridPositions = new List<Vector2Int>();
        minimap.eventRoomGridPositions = new List<Vector2Int>();
        minimap.treasureRoomGridPositions = new List<Vector2Int>();
        minimap.safeRoomGridPositions = new List<Vector2Int>();
        minimap.stairsRoomGridPositions = new List<Vector2Int>();
        // Rooms restored as already-cleared (see clearedRoomsThisFloor) never fire
        // RoomController.OnRoomCleared - that only happens live, and MinimapController hasn't
        // subscribed to it yet at this point in the same frame anyway (its own Start() runs later)
        // - so it needs this list to color them green from the very first frame instead of
        // reading as freshly-undiscovered on a resumed save.
        minimap.preClearedRoomGridPositions = new List<Vector2Int>(clearedRoomsThisFloor);
        foreach (KeyValuePair<Vector2Int, RoomType> kv2 in layout)
        {
            if (kv2.Value == RoomType.Secret) minimap.secretRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Boss) minimap.bossRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Shop) minimap.shopRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Event) minimap.eventRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Treasure) minimap.treasureRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Safe) minimap.safeRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Stairs) minimap.stairsRoomGridPositions.Add(kv2.Key);
        }
        minimap.bossIconSprite = bossMarker;
        minimap.shopIconSprite = shopMarker;
        minimap.eventIconSprite = eventMarker;
        minimap.secretIconSprite = secretMarker;
        minimap.safeIconSprite = safeMarker;
        minimap.stairsIconSprite = stairsMarker;
        minimap.outlineRingSprite = outlineRingSprite;
        minimap.cellSize = 22f;
        minimap.spacing = 5f;
        minimap.maxPanelSize = 320f;

        // --- Dialogue UI (bottom panel + interact prompt + dice roll popup) ---
        // Text sizes doubled (or more) across this whole block for readability, per user request -
        // the panel/positions grew to match so the bigger text still fits.
        Font uiFont = Font.CreateDynamicFontFromOSFont("Arial", 32);

        GameObject dialogueGO = new GameObject("DialogueManager", typeof(RectTransform), typeof(DialogueManager));
        dialogueGO.transform.SetParent(canvasGO.transform, false);
        RectTransform dialogueRect = dialogueGO.GetComponent<RectTransform>();
        dialogueRect.anchorMin = Vector2.zero;
        dialogueRect.anchorMax = Vector2.one;
        dialogueRect.offsetMin = Vector2.zero;
        dialogueRect.offsetMax = Vector2.zero;

        GameObject promptGO = new GameObject("InteractPrompt", typeof(Text));
        promptGO.transform.SetParent(dialogueGO.transform, false);
        Text promptText = promptGO.GetComponent<Text>();
        promptText.font = uiFont;
        promptText.fontSize = 36;
        promptText.alignment = TextAnchor.MiddleCenter;
        promptText.color = Color.white;
        promptText.text = "Appuyez sur E pour parler";
        RectTransform promptRect = promptText.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 170f);
        promptRect.sizeDelta = new Vector2(800f, 60f);
        promptGO.SetActive(false);

        GameObject dialoguePanel = new GameObject("DialoguePanel", typeof(Image));
        dialoguePanel.transform.SetParent(dialogueGO.transform, false);
        dialoguePanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.9f);
        RectTransform panelRect = dialoguePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 20f);
        panelRect.sizeDelta = new Vector2(1200f, 520f);
        dialoguePanel.SetActive(false);

        GameObject npcNameGO = new GameObject("NpcName", typeof(Text));
        npcNameGO.transform.SetParent(dialoguePanel.transform, false);
        Text npcNameText = npcNameGO.GetComponent<Text>();
        npcNameText.font = uiFont;
        npcNameText.fontSize = 40;
        npcNameText.fontStyle = FontStyle.Bold;
        npcNameText.alignment = TextAnchor.UpperLeft;
        npcNameText.color = new Color(0.9f, 0.8f, 0.4f);
        RectTransform npcNameRect = npcNameText.rectTransform;
        npcNameRect.anchorMin = new Vector2(0f, 1f);
        npcNameRect.anchorMax = new Vector2(1f, 1f);
        npcNameRect.pivot = new Vector2(0.5f, 1f);
        npcNameRect.anchoredPosition = new Vector2(0f, -20f);
        npcNameRect.sizeDelta = new Vector2(-40f, 60f);

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(dialoguePanel.transform, false);
        Text bodyText = bodyGO.GetComponent<Text>();
        bodyText.font = uiFont;
        bodyText.fontSize = 32;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.color = Color.white;
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -90f);
        bodyRect.sizeDelta = new Vector2(-40f, 140f);

        GameObject optionsGO = new GameObject("Options", typeof(Text));
        optionsGO.transform.SetParent(dialoguePanel.transform, false);
        Text optionsText = optionsGO.GetComponent<Text>();
        optionsText.font = uiFont;
        optionsText.fontSize = 32;
        optionsText.alignment = TextAnchor.UpperLeft;
        optionsText.color = new Color(0.75f, 0.85f, 1f);
        optionsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        optionsText.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform optionsRect = optionsText.rectTransform;
        optionsRect.anchorMin = new Vector2(0f, 0f);
        optionsRect.anchorMax = new Vector2(1f, 1f);
        optionsRect.pivot = new Vector2(0.5f, 0f);
        optionsRect.offsetMin = new Vector2(20f, 20f);
        optionsRect.offsetMax = new Vector2(-20f, -260f);

        // Same footprint as optionsText above - shown instead of it for a shop NPC (see
        // NpcInteractable.useShopUI/DialogueManager.BuildShopGrid), an empty container the grid's
        // icon slots get parented into at runtime.
        GameObject shopGridGO = new GameObject("ShopGrid", typeof(RectTransform));
        shopGridGO.transform.SetParent(dialoguePanel.transform, false);
        RectTransform shopGridRect = shopGridGO.GetComponent<RectTransform>();
        shopGridRect.anchorMin = new Vector2(0f, 0f);
        shopGridRect.anchorMax = new Vector2(1f, 1f);
        shopGridRect.pivot = new Vector2(0.5f, 0f);
        shopGridRect.offsetMin = new Vector2(20f, 20f);
        shopGridRect.offsetMax = new Vector2(-20f, -260f);
        shopGridGO.SetActive(false);

        GameObject diceGO = new GameObject("DiceRoll", typeof(Image), typeof(DiceRollUI));
        diceGO.transform.SetParent(dialogueGO.transform, false);
        Image diceBackground = diceGO.GetComponent<Image>();
        diceBackground.color = new Color(0.2f, 0.2f, 0.25f, 0.95f);
        RectTransform diceRect = diceBackground.rectTransform;
        diceRect.anchorMin = diceRect.anchorMax = new Vector2(0.5f, 0.5f);
        diceRect.pivot = new Vector2(0.5f, 0.5f);
        diceRect.anchoredPosition = new Vector2(0f, 150f);
        diceRect.sizeDelta = new Vector2(520f, 180f);
        diceGO.SetActive(false);

        GameObject diceTextGO = new GameObject("RollText", typeof(Text));
        diceTextGO.transform.SetParent(diceGO.transform, false);
        Text diceText = diceTextGO.GetComponent<Text>();
        diceText.font = uiFont;
        diceText.fontSize = 56;
        diceText.fontStyle = FontStyle.Bold;
        diceText.alignment = TextAnchor.MiddleCenter;
        diceText.color = Color.white;
        RectTransform diceTextRect = diceText.rectTransform;
        diceTextRect.anchorMin = Vector2.zero;
        diceTextRect.anchorMax = Vector2.one;
        diceTextRect.offsetMin = Vector2.zero;
        diceTextRect.offsetMax = Vector2.zero;

        DiceRollUI diceRollUI = diceGO.GetComponent<DiceRollUI>();
        diceRollUI.rollText = diceText;
        diceRollUI.background = diceBackground;

        DialogueManager dialogueManager = dialogueGO.GetComponent<DialogueManager>();
        dialogueManager.playerStats = playerStats;
        dialogueManager.playerInventory = playerInventory;
        dialogueManager.playerEquipment = playerEquipment;
        dialogueManager.playerController = playerController;
        dialogueManager.playerHealth = playerHealth;
        dialogueManager.playerStamina = playerStamina;
        dialogueManager.diceRoll = diceRollUI;
        dialogueManager.promptGO = promptGO;
        dialogueManager.panel = dialoguePanel;
        dialogueManager.nameText = npcNameText;
        dialogueManager.bodyText = bodyText;
        dialogueManager.optionsText = optionsText;
        dialogueManager.shopGridRoot = shopGridGO;

        // --- Item inspection UI (weight/curse/trap ground items - interact prompt + panel) ---
        GameObject inspectGO = new GameObject("ItemInspectManager", typeof(RectTransform), typeof(ItemInspectManager));
        inspectGO.transform.SetParent(canvasGO.transform, false);
        RectTransform inspectRect = inspectGO.GetComponent<RectTransform>();
        inspectRect.anchorMin = Vector2.zero;
        inspectRect.anchorMax = Vector2.one;
        inspectRect.offsetMin = Vector2.zero;
        inspectRect.offsetMax = Vector2.zero;

        GameObject inspectPromptGO = new GameObject("InspectPrompt", typeof(Text));
        inspectPromptGO.transform.SetParent(inspectGO.transform, false);
        Text inspectPromptText = inspectPromptGO.GetComponent<Text>();
        inspectPromptText.font = uiFont;
        inspectPromptText.fontSize = 36;
        inspectPromptText.alignment = TextAnchor.MiddleCenter;
        inspectPromptText.color = Color.white;
        inspectPromptText.text = "Appuyez sur E pour examiner";
        RectTransform inspectPromptRect = inspectPromptText.rectTransform;
        inspectPromptRect.anchorMin = inspectPromptRect.anchorMax = new Vector2(0.5f, 0f);
        inspectPromptRect.pivot = new Vector2(0.5f, 0f);
        inspectPromptRect.anchoredPosition = new Vector2(0f, 170f);
        inspectPromptRect.sizeDelta = new Vector2(800f, 60f);
        inspectPromptGO.SetActive(false);

        GameObject inspectPanel = new GameObject("InspectPanel", typeof(Image));
        inspectPanel.transform.SetParent(inspectGO.transform, false);
        inspectPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.9f);
        RectTransform inspectPanelRect = inspectPanel.GetComponent<RectTransform>();
        inspectPanelRect.anchorMin = inspectPanelRect.anchorMax = new Vector2(0.5f, 0f);
        inspectPanelRect.pivot = new Vector2(0.5f, 0f);
        inspectPanelRect.anchoredPosition = new Vector2(0f, 20f);
        inspectPanelRect.sizeDelta = new Vector2(1200f, 260f);
        inspectPanel.SetActive(false);

        GameObject inspectNameGO = new GameObject("ItemName", typeof(Text));
        inspectNameGO.transform.SetParent(inspectPanel.transform, false);
        Text inspectNameText = inspectNameGO.GetComponent<Text>();
        inspectNameText.font = uiFont;
        inspectNameText.fontSize = 40;
        inspectNameText.fontStyle = FontStyle.Bold;
        inspectNameText.alignment = TextAnchor.UpperLeft;
        inspectNameText.color = new Color(0.9f, 0.8f, 0.4f);
        RectTransform inspectNameRect = inspectNameText.rectTransform;
        inspectNameRect.anchorMin = new Vector2(0f, 1f);
        inspectNameRect.anchorMax = new Vector2(1f, 1f);
        inspectNameRect.pivot = new Vector2(0.5f, 1f);
        inspectNameRect.anchoredPosition = new Vector2(0f, -20f);
        inspectNameRect.sizeDelta = new Vector2(-40f, 60f);

        GameObject inspectBodyGO = new GameObject("Body", typeof(Text));
        inspectBodyGO.transform.SetParent(inspectPanel.transform, false);
        Text inspectBodyText = inspectBodyGO.GetComponent<Text>();
        inspectBodyText.font = uiFont;
        inspectBodyText.fontSize = 32;
        inspectBodyText.alignment = TextAnchor.UpperLeft;
        inspectBodyText.color = Color.white;
        inspectBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        inspectBodyText.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform inspectBodyRect = inspectBodyText.rectTransform;
        inspectBodyRect.anchorMin = new Vector2(0f, 0f);
        inspectBodyRect.anchorMax = new Vector2(1f, 1f);
        inspectBodyRect.pivot = new Vector2(0.5f, 0f);
        inspectBodyRect.offsetMin = new Vector2(20f, 20f);
        inspectBodyRect.offsetMax = new Vector2(-20f, -90f);

        ItemInspectManager itemInspectManager = inspectGO.GetComponent<ItemInspectManager>();
        itemInspectManager.player = player.transform;
        itemInspectManager.promptGO = inspectPromptGO;
        itemInspectManager.panel = inspectPanel;
        itemInspectManager.nameText = inspectNameText;
        itemInspectManager.bodyText = inspectBodyText;

        // --- Boss victory banner (center screen, hidden by default) ---
        GameObject victoryGO = new GameObject("VictoryBanner", typeof(RectTransform), typeof(VictoryBannerUI));
        victoryGO.transform.SetParent(canvasGO.transform, false);
        RectTransform victoryRect = victoryGO.GetComponent<RectTransform>();
        victoryRect.anchorMin = Vector2.zero;
        victoryRect.anchorMax = Vector2.one;
        victoryRect.offsetMin = Vector2.zero;
        victoryRect.offsetMax = Vector2.zero;

        GameObject victoryTextGO = new GameObject("Text", typeof(Text));
        victoryTextGO.transform.SetParent(victoryGO.transform, false);
        Text victoryText = victoryTextGO.GetComponent<Text>();
        victoryText.font = uiFont;
        victoryText.fontSize = 56;
        victoryText.fontStyle = FontStyle.Bold;
        victoryText.alignment = TextAnchor.MiddleCenter;
        victoryText.color = new Color(0.95f, 0.85f, 0.3f);
        RectTransform victoryTextRect = victoryText.rectTransform;
        victoryTextRect.anchorMin = new Vector2(0.5f, 0.7f);
        victoryTextRect.anchorMax = new Vector2(0.5f, 0.7f);
        victoryTextRect.pivot = new Vector2(0.5f, 0.5f);
        victoryTextRect.sizeDelta = new Vector2(1200f, 120f);
        victoryGO.SetActive(false);

        VictoryBannerUI victoryBanner = victoryGO.GetComponent<VictoryBannerUI>();
        victoryBanner.root = victoryGO;
        victoryBanner.bannerText = victoryText;

        // --- Room-type announcement (top of screen, hidden by default) ---
        GameObject announceGO = new GameObject("RoomAnnouncement", typeof(RectTransform), typeof(RoomAnnouncementUI));
        announceGO.transform.SetParent(canvasGO.transform, false);

        GameObject announceTextGO = new GameObject("Text", typeof(Text));
        announceTextGO.transform.SetParent(announceGO.transform, false);
        Text announceText = announceTextGO.GetComponent<Text>();
        announceText.font = uiFont;
        announceText.fontSize = 32;
        announceText.fontStyle = FontStyle.Bold;
        announceText.alignment = TextAnchor.MiddleCenter;
        announceText.color = Color.white;
        RectTransform announceTextRect = announceText.rectTransform;
        announceTextRect.anchorMin = new Vector2(0.5f, 1f);
        announceTextRect.anchorMax = new Vector2(0.5f, 1f);
        announceTextRect.pivot = new Vector2(0.5f, 1f);
        announceTextRect.anchoredPosition = new Vector2(0f, -160f);
        announceTextRect.sizeDelta = new Vector2(900f, 60f);
        announceGO.SetActive(false);

        RoomAnnouncementUI roomAnnouncement = announceGO.GetComponent<RoomAnnouncementUI>();
        roomAnnouncement.root = announceGO;
        roomAnnouncement.label = announceText;

        // --- Pickup toast (bottom, above the hotbar, hidden by default) - reuses RoomAnnouncementUI
        // (a generic auto-hide text banner) rather than a near-duplicate component, just parked at
        // a different screen position so the two never overlap. Announces exactly what/how much
        // was picked up (see PlayerInventory.OnItemPickedUp) - a freshly grabbed item otherwise
        // changes silently somewhere inside a 20-slot grid the player has to open and hunt through.
        GameObject pickupToastGO = new GameObject("PickupToast", typeof(RectTransform), typeof(RoomAnnouncementUI));
        pickupToastGO.transform.SetParent(canvasGO.transform, false);

        GameObject pickupToastTextGO = new GameObject("Text", typeof(Text));
        pickupToastTextGO.transform.SetParent(pickupToastGO.transform, false);
        Text pickupToastText = pickupToastTextGO.GetComponent<Text>();
        pickupToastText.font = uiFont;
        pickupToastText.fontSize = 26;
        pickupToastText.fontStyle = FontStyle.Bold;
        pickupToastText.alignment = TextAnchor.MiddleCenter;
        pickupToastText.color = new Color(0.9f, 0.9f, 0.6f);
        RectTransform pickupToastTextRect = pickupToastText.rectTransform;
        pickupToastTextRect.anchorMin = new Vector2(0.5f, 0f);
        pickupToastTextRect.anchorMax = new Vector2(0.5f, 0f);
        pickupToastTextRect.pivot = new Vector2(0.5f, 0f);
        pickupToastTextRect.anchoredPosition = new Vector2(0f, 110f);
        pickupToastTextRect.sizeDelta = new Vector2(700f, 50f);
        pickupToastGO.SetActive(false);

        RoomAnnouncementUI pickupToast = pickupToastGO.GetComponent<RoomAnnouncementUI>();
        pickupToast.root = pickupToastGO;
        pickupToast.label = pickupToastText;
        pickupToast.displayDuration = 1.8f;
        playerInventory.OnItemPickedUp += (pickedItemId, pickedAmount) =>
        {
            ItemDefinition pickedDefinition = ItemDatabase.Get(pickedItemId);
            if (pickedDefinition != null) pickupToast.Show("+" + pickedAmount + " " + pickedDefinition.DisplayName);
        };

        // --- Achievement toast (top-center, hidden by default) + voice (2026-09-21 request) ---
        GameObject achievementGO = new GameObject("AchievementToast", typeof(RectTransform), typeof(AchievementToastUI));
        achievementGO.transform.SetParent(canvasGO.transform, false);

        GameObject achievementBgGO = new GameObject("Bg", typeof(Image));
        achievementBgGO.transform.SetParent(achievementGO.transform, false);
        Image achievementBg = achievementBgGO.GetComponent<Image>();
        achievementBg.color = new Color(0.08f, 0.07f, 0.03f, 0.85f);
        RectTransform achievementBgRect = achievementBg.rectTransform;
        achievementBgRect.anchorMin = new Vector2(0.5f, 1f);
        achievementBgRect.anchorMax = new Vector2(0.5f, 1f);
        achievementBgRect.pivot = new Vector2(0.5f, 1f);
        achievementBgRect.anchoredPosition = new Vector2(0f, -20f);
        achievementBgRect.sizeDelta = new Vector2(560f, 84f);

        GameObject achievementTitleGO = new GameObject("Title", typeof(Text));
        achievementTitleGO.transform.SetParent(achievementBgGO.transform, false);
        Text achievementTitleText = achievementTitleGO.GetComponent<Text>();
        achievementTitleText.font = uiFont;
        achievementTitleText.fontSize = 26;
        achievementTitleText.fontStyle = FontStyle.Bold;
        achievementTitleText.alignment = TextAnchor.MiddleCenter;
        achievementTitleText.color = new Color(0.95f, 0.85f, 0.3f);
        RectTransform achievementTitleRect = achievementTitleText.rectTransform;
        achievementTitleRect.anchorMin = new Vector2(0f, 1f);
        achievementTitleRect.anchorMax = new Vector2(1f, 1f);
        achievementTitleRect.pivot = new Vector2(0.5f, 1f);
        achievementTitleRect.anchoredPosition = new Vector2(0f, -6f);
        achievementTitleRect.sizeDelta = new Vector2(-20f, 34f);

        GameObject achievementDescGO = new GameObject("Description", typeof(Text));
        achievementDescGO.transform.SetParent(achievementBgGO.transform, false);
        Text achievementDescText = achievementDescGO.GetComponent<Text>();
        achievementDescText.font = uiFont;
        achievementDescText.fontSize = 20;
        achievementDescText.alignment = TextAnchor.MiddleCenter;
        achievementDescText.color = Color.white;
        RectTransform achievementDescRect = achievementDescText.rectTransform;
        achievementDescRect.anchorMin = new Vector2(0f, 0f);
        achievementDescRect.anchorMax = new Vector2(1f, 1f);
        achievementDescRect.pivot = new Vector2(0.5f, 0.5f);
        achievementDescRect.anchoredPosition = new Vector2(0f, -20f);
        achievementDescRect.sizeDelta = new Vector2(-20f, -44f);
        achievementBgGO.SetActive(false);

        AchievementToastUI achievementToast = achievementGO.GetComponent<AchievementToastUI>();
        achievementToast.root = achievementBgGO;
        achievementToast.titleText = achievementTitleText;
        achievementToast.descriptionText = achievementDescText;

        GameObject achievementManagerGO = new GameObject("AchievementManager", typeof(AudioSource), typeof(AchievementVoice), typeof(AchievementManager));
        achievementManagerGO.transform.SetParent(canvasGO.transform, false);
        AchievementManager achievementManager = achievementManagerGO.GetComponent<AchievementManager>();
        achievementManager.toast = achievementToast;
        achievementManager.voice = achievementManagerGO.GetComponent<AchievementVoice>();

        achievementManager.NotifyFloorReached(floor);
        playerStats.OnExperienceChanged += (xp, xpToNext, level) => achievementManager.NotifyLevelReached(level);
        playerInventory.OnItemPickedUp += achievementManager.NotifyItemPickedUp;

        // --- Boss intro title card (center screen, hidden by default) + voice (2026-09-21 request:
        // "pause dans la salle... camera vers le boss... description orale... puis retour") ---
        GameObject bossIntroGO = new GameObject("BossIntro", typeof(RectTransform), typeof(BossIntroUI));
        bossIntroGO.transform.SetParent(canvasGO.transform, false);

        GameObject bossIntroBgGO = new GameObject("Bg", typeof(Image));
        bossIntroBgGO.transform.SetParent(bossIntroGO.transform, false);
        Image bossIntroBg = bossIntroBgGO.GetComponent<Image>();
        bossIntroBg.color = new Color(0.03f, 0.02f, 0.02f, 0.88f);
        RectTransform bossIntroBgRect = bossIntroBg.rectTransform;
        bossIntroBgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bossIntroBgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bossIntroBgRect.pivot = new Vector2(0.5f, 0.5f);
        bossIntroBgRect.anchoredPosition = new Vector2(0f, 60f);
        bossIntroBgRect.sizeDelta = new Vector2(1000f, 180f);

        GameObject bossIntroNameGO = new GameObject("Name", typeof(Text));
        bossIntroNameGO.transform.SetParent(bossIntroBgGO.transform, false);
        Text bossIntroNameText = bossIntroNameGO.GetComponent<Text>();
        bossIntroNameText.font = uiFont;
        bossIntroNameText.fontSize = 40;
        bossIntroNameText.fontStyle = FontStyle.Bold;
        bossIntroNameText.alignment = TextAnchor.MiddleCenter;
        bossIntroNameText.color = new Color(0.85f, 0.2f, 0.2f);
        RectTransform bossIntroNameRect = bossIntroNameText.rectTransform;
        bossIntroNameRect.anchorMin = new Vector2(0f, 1f);
        bossIntroNameRect.anchorMax = new Vector2(1f, 1f);
        bossIntroNameRect.pivot = new Vector2(0.5f, 1f);
        bossIntroNameRect.anchoredPosition = new Vector2(0f, -14f);
        bossIntroNameRect.sizeDelta = new Vector2(-40f, 56f);

        GameObject bossIntroDescGO = new GameObject("Description", typeof(Text));
        bossIntroDescGO.transform.SetParent(bossIntroBgGO.transform, false);
        Text bossIntroDescText = bossIntroDescGO.GetComponent<Text>();
        bossIntroDescText.font = uiFont;
        bossIntroDescText.fontSize = 22;
        bossIntroDescText.alignment = TextAnchor.MiddleCenter;
        bossIntroDescText.color = Color.white;
        RectTransform bossIntroDescRect = bossIntroDescText.rectTransform;
        bossIntroDescRect.anchorMin = new Vector2(0f, 0f);
        bossIntroDescRect.anchorMax = new Vector2(1f, 1f);
        bossIntroDescRect.pivot = new Vector2(0.5f, 0.5f);
        bossIntroDescRect.anchoredPosition = new Vector2(0f, -34f);
        bossIntroDescRect.sizeDelta = new Vector2(-60f, -80f);
        bossIntroBgGO.SetActive(false);

        BossIntroUI bossIntroUI = bossIntroGO.GetComponent<BossIntroUI>();
        bossIntroUI.root = bossIntroBgGO;
        bossIntroUI.nameText = bossIntroNameText;
        bossIntroUI.descriptionText = bossIntroDescText;

        GameObject bossIntroVoiceGO = new GameObject("BossIntroVoicePlayer", typeof(AudioSource), typeof(BossIntroVoice));
        bossIntroVoiceGO.transform.SetParent(canvasGO.transform, false);
        BossIntroVoice bossIntroVoice = bossIntroVoiceGO.GetComponent<BossIntroVoice>();

        if (roomCam != null)
        {
            RoomType? lastAnnouncedType = null;
            roomCam.OnRoomEntered += enteredGridPos =>
            {
                if (!layout.TryGetValue(enteredGridPos, out RoomType enteredType)) return;
                // Suppresses re-announcing on every cell crossed while wandering a single merged
                // multi-cell room (OnRoomEntered fires once per cell - see RoomCameraController) -
                // only a genuine change of room type re-triggers it. At most one room of each
                // special type exists per floor (see PlaceSpecialRoom), so this never misses a
                // real transition either.
                if (enteredType == lastAnnouncedType) return;
                lastAnnouncedType = enteredType;

                string message = RoomAnnouncementText(enteredType);
                if (message != null) roomAnnouncement.Show(message);
            };
        }

        // --- Death screen (full-screen, hidden until the player dies - see DeathScreenUI) ---
        GameObject deathGO = new GameObject("DeathScreen", typeof(RectTransform), typeof(Image), typeof(DeathScreenUI));
        deathGO.transform.SetParent(canvasGO.transform, false);
        Image deathBg = deathGO.GetComponent<Image>();
        deathBg.color = new Color(0.03f, 0.02f, 0.02f, 0.92f);
        RectTransform deathRect = deathBg.rectTransform;
        deathRect.anchorMin = Vector2.zero;
        deathRect.anchorMax = Vector2.one;
        deathRect.offsetMin = Vector2.zero;
        deathRect.offsetMax = Vector2.zero;

        GameObject deathTitleGO = new GameObject("Title", typeof(Text));
        deathTitleGO.transform.SetParent(deathGO.transform, false);
        Text deathTitle = deathTitleGO.GetComponent<Text>();
        deathTitle.text = "VOUS ETES MORT";
        deathTitle.font = uiFont;
        deathTitle.fontSize = 64;
        deathTitle.fontStyle = FontStyle.Bold;
        deathTitle.alignment = TextAnchor.MiddleCenter;
        deathTitle.color = new Color(0.8f, 0.15f, 0.15f);
        RectTransform deathTitleRect = deathTitle.rectTransform;
        deathTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
        deathTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
        deathTitleRect.pivot = new Vector2(0.5f, 0.5f);
        deathTitleRect.anchoredPosition = new Vector2(0f, 30f);
        deathTitleRect.sizeDelta = new Vector2(1200f, 120f);

        GameObject deathPromptGO = new GameObject("Prompt", typeof(Text));
        deathPromptGO.transform.SetParent(deathGO.transform, false);
        Text deathPrompt = deathPromptGO.GetComponent<Text>();
        deathPrompt.text = "Appuyez sur ESPACE pour retourner au menu";
        deathPrompt.font = uiFont;
        deathPrompt.fontSize = 26;
        deathPrompt.alignment = TextAnchor.MiddleCenter;
        deathPrompt.color = new Color(0.85f, 0.85f, 0.85f);
        RectTransform deathPromptRect = deathPrompt.rectTransform;
        deathPromptRect.anchorMin = new Vector2(0.5f, 0.5f);
        deathPromptRect.anchorMax = new Vector2(0.5f, 0.5f);
        deathPromptRect.pivot = new Vector2(0.5f, 0.5f);
        deathPromptRect.anchoredPosition = new Vector2(0f, -40f);
        deathPromptRect.sizeDelta = new Vector2(900f, 60f);

        deathGO.SetActive(false);

        DeathScreenUI deathScreen = deathGO.GetComponent<DeathScreenUI>();
        deathScreen.root = deathGO;
        deathScreen.mainMenu = Object.FindFirstObjectByType<MainMenuController>();
        playerHealth.OnDeath += deathScreen.Show;

        // --- Pause menu (Echap, hidden by default) - Reprendre/Parametres/Quitter au menu ---
        PauseMenuUI pauseMenu = BuildPauseMenu(canvasGO.transform, uiFont, deathScreen.mainMenu);

        // --- Attribute allocation panel (opened from the Tavernier - see AttributeAllocationUI) ---
        GameObject attrGO = new GameObject("AttributeAllocation", typeof(RectTransform), typeof(Image), typeof(AttributeAllocationUI));
        attrGO.transform.SetParent(canvasGO.transform, false);
        Image attrBg = attrGO.GetComponent<Image>();
        attrBg.color = new Color(0.05f, 0.05f, 0.06f, 0.9f);
        RectTransform attrRect = attrBg.rectTransform;
        attrRect.anchorMin = Vector2.zero;
        attrRect.anchorMax = Vector2.one;
        attrRect.offsetMin = Vector2.zero;
        attrRect.offsetMax = Vector2.zero;

        GameObject attrTitleGO = new GameObject("Title", typeof(Text));
        attrTitleGO.transform.SetParent(attrGO.transform, false);
        Text attrTitle = attrTitleGO.GetComponent<Text>();
        attrTitle.text = "Points d'attribut";
        attrTitle.font = uiFont;
        attrTitle.fontSize = 44;
        attrTitle.fontStyle = FontStyle.Bold;
        attrTitle.alignment = TextAnchor.MiddleCenter;
        attrTitle.color = Color.white;
        RectTransform attrTitleRect = attrTitle.rectTransform;
        attrTitleRect.anchorMin = attrTitleRect.anchorMax = new Vector2(0.5f, 1f);
        attrTitleRect.pivot = new Vector2(0.5f, 1f);
        attrTitleRect.anchoredPosition = new Vector2(0f, -80f);
        attrTitleRect.sizeDelta = new Vector2(800f, 80f);

        GameObject pointsLabelGO = new GameObject("PointsLabel", typeof(Text));
        pointsLabelGO.transform.SetParent(attrGO.transform, false);
        Text pointsLabel = pointsLabelGO.GetComponent<Text>();
        pointsLabel.font = uiFont;
        pointsLabel.fontSize = 28;
        pointsLabel.alignment = TextAnchor.MiddleCenter;
        pointsLabel.color = new Color(0.9f, 0.85f, 0.3f);
        RectTransform pointsLabelRect = pointsLabel.rectTransform;
        pointsLabelRect.anchorMin = pointsLabelRect.anchorMax = new Vector2(0.5f, 1f);
        pointsLabelRect.pivot = new Vector2(0.5f, 1f);
        pointsLabelRect.anchoredPosition = new Vector2(0f, -150f);
        pointsLabelRect.sizeDelta = new Vector2(500f, 50f);

        // Vitesse deliberately excluded (2026-09-15 design request - a slower, more punishing
        // Dark Souls-like pace): the player's own move speed is meant to be fixed unless an item
        // built for it grants Vitesse (see ItemDefinition.RingBonusStat, still fully wired -
        // PlayerStats.vitesse/MoveSpeedMultiplier are untouched, only free-point investment is
        // removed), never something raised at will with level-up points.
        (StatType type, string label)[] statRows =
        {
            (StatType.Force, "Force"), (StatType.Dexterite, "Dexterite"), (StatType.Intelligence, "Intelligence"),
            (StatType.Constitution, "Constitution"), (StatType.Portee, "Portee"),
            (StatType.Charisme, "Charisme"), (StatType.Endurance, "Endurance"),
        };

        AttributeAllocationUI attributeAllocation = attrGO.GetComponent<AttributeAllocationUI>();
        attributeAllocation.stats = playerStats;
        attributeAllocation.pointsLabel = pointsLabel;
        attributeAllocation.statTypes = new StatType[statRows.Length];
        attributeAllocation.valueLabels = new Text[statRows.Length];

        float rowY = -210f;
        for (int i = 0; i < statRows.Length; i++)
        {
            StatType statType = statRows[i].type; // captured per-iteration for the button lambda below
            attributeAllocation.statTypes[i] = statType;

            GameObject rowLabelGO = new GameObject(statRows[i].label + "Label", typeof(Text));
            rowLabelGO.transform.SetParent(attrGO.transform, false);
            Text rowLabel = rowLabelGO.GetComponent<Text>();
            rowLabel.text = statRows[i].label;
            rowLabel.font = uiFont;
            rowLabel.fontSize = 24;
            rowLabel.alignment = TextAnchor.MiddleRight;
            rowLabel.color = Color.white;
            RectTransform rowLabelRect = rowLabel.rectTransform;
            rowLabelRect.anchorMin = rowLabelRect.anchorMax = new Vector2(0.5f, 1f);
            rowLabelRect.pivot = new Vector2(1f, 0.5f);
            rowLabelRect.anchoredPosition = new Vector2(-40f, rowY);
            rowLabelRect.sizeDelta = new Vector2(220f, 44f);

            GameObject valueGO = new GameObject(statRows[i].label + "Value", typeof(Text));
            valueGO.transform.SetParent(attrGO.transform, false);
            Text valueText = valueGO.GetComponent<Text>();
            valueText.font = uiFont;
            valueText.fontSize = 24;
            valueText.alignment = TextAnchor.MiddleCenter;
            valueText.color = Color.white;
            RectTransform valueRect = valueText.rectTransform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(0.5f, 1f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.anchoredPosition = new Vector2(20f, rowY);
            valueRect.sizeDelta = new Vector2(60f, 44f);
            attributeAllocation.valueLabels[i] = valueText;

            RectTransform allocateButtonRect = MainMenuController.CreateButton(attrGO.transform, "+1", uiFont, 0f, () => attributeAllocation.Allocate(statType))
                .GetComponent<RectTransform>();
            allocateButtonRect.anchoredPosition = new Vector2(140f, rowY);
            allocateButtonRect.sizeDelta = new Vector2(60f, 44f);

            rowY -= 60f;
        }

        MainMenuController.CreateButton(attrGO.transform, "Fermer", uiFont, rowY - 20f, attributeAllocation.Hide);

        attrGO.SetActive(false);

        attributeAllocation.root = attrGO;

        // --- Repair panel (opened from the Crafting Table - see RepairUI/DialogueOutcome.
        // opensRepairPanel) - same fixed-rows-live-refresh pattern as the attribute panel above,
        // one row per durability-bearing slot (rings never carry durability).
        GameObject repairGO = new GameObject("RepairPanel", typeof(RectTransform), typeof(Image), typeof(RepairUI));
        repairGO.transform.SetParent(canvasGO.transform, false);
        Image repairBg = repairGO.GetComponent<Image>();
        repairBg.color = new Color(0.05f, 0.05f, 0.06f, 0.9f);
        RectTransform repairRect = repairBg.rectTransform;
        repairRect.anchorMin = Vector2.zero;
        repairRect.anchorMax = Vector2.one;
        repairRect.offsetMin = Vector2.zero;
        repairRect.offsetMax = Vector2.zero;

        GameObject repairTitleGO = new GameObject("Title", typeof(Text));
        repairTitleGO.transform.SetParent(repairGO.transform, false);
        Text repairTitle = repairTitleGO.GetComponent<Text>();
        repairTitle.text = "Reparer / demonter l'equipement";
        repairTitle.font = uiFont;
        repairTitle.fontSize = 40;
        repairTitle.fontStyle = FontStyle.Bold;
        repairTitle.alignment = TextAnchor.MiddleCenter;
        repairTitle.color = Color.white;
        RectTransform repairTitleRect = repairTitle.rectTransform;
        repairTitleRect.anchorMin = repairTitleRect.anchorMax = new Vector2(0.5f, 1f);
        repairTitleRect.pivot = new Vector2(0.5f, 1f);
        repairTitleRect.anchoredPosition = new Vector2(0f, -80f);
        repairTitleRect.sizeDelta = new Vector2(900f, 70f);

        (EquipmentSlotType slot, string label)[] repairSlotRows =
        {
            (EquipmentSlotType.Weapon, "Arme"), (EquipmentSlotType.Head, "Tete"), (EquipmentSlotType.Shoulders, "Epaules"), (EquipmentSlotType.Gloves, "Gants"),
            (EquipmentSlotType.Boots, "Bottes"), (EquipmentSlotType.Neck, "Cou"), (EquipmentSlotType.Belt, "Ceinture"),
            (EquipmentSlotType.Knees, "Genoux"),
        };

        RepairUI repairUI = repairGO.GetComponent<RepairUI>();
        repairUI.equipment = playerEquipment;
        repairUI.inventory = playerInventory;
        repairUI.slots = new EquipmentSlotType[repairSlotRows.Length];
        repairUI.nameLabels = new Text[repairSlotRows.Length];
        repairUI.durabilityLabels = new Text[repairSlotRows.Length];
        repairUI.repairButtons = new Button[repairSlotRows.Length];
        repairUI.dismantleButtons = new Button[repairSlotRows.Length];

        float repairRowY = -170f;
        for (int i = 0; i < repairSlotRows.Length; i++)
        {
            int rowIndex = i; // captured per-iteration for the button lambdas below
            repairUI.slots[i] = repairSlotRows[i].slot;

            GameObject slotLabelGO = new GameObject(repairSlotRows[i].label + "Label", typeof(Text));
            slotLabelGO.transform.SetParent(repairGO.transform, false);
            Text slotLabel = slotLabelGO.GetComponent<Text>();
            slotLabel.text = repairSlotRows[i].label;
            slotLabel.font = uiFont;
            slotLabel.fontSize = 22;
            slotLabel.alignment = TextAnchor.MiddleRight;
            slotLabel.color = Color.white;
            RectTransform slotLabelRect = slotLabel.rectTransform;
            slotLabelRect.anchorMin = slotLabelRect.anchorMax = new Vector2(0.5f, 1f);
            slotLabelRect.pivot = new Vector2(1f, 0.5f);
            slotLabelRect.anchoredPosition = new Vector2(-330f, repairRowY);
            slotLabelRect.sizeDelta = new Vector2(120f, 44f);

            GameObject nameGO = new GameObject(repairSlotRows[i].label + "Name", typeof(Text));
            nameGO.transform.SetParent(repairGO.transform, false);
            Text nameText = nameGO.GetComponent<Text>();
            nameText.font = uiFont;
            nameText.fontSize = 20;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.color = new Color(0.85f, 0.85f, 0.85f);
            RectTransform nameRect = nameText.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 1f);
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.anchoredPosition = new Vector2(-190f, repairRowY);
            nameRect.sizeDelta = new Vector2(280f, 44f);
            repairUI.nameLabels[i] = nameText;

            GameObject durabilityGO = new GameObject(repairSlotRows[i].label + "Durability", typeof(Text));
            durabilityGO.transform.SetParent(repairGO.transform, false);
            Text durabilityText = durabilityGO.GetComponent<Text>();
            durabilityText.font = uiFont;
            durabilityText.fontSize = 20;
            durabilityText.alignment = TextAnchor.MiddleCenter;
            durabilityText.color = new Color(0.9f, 0.85f, 0.3f);
            RectTransform durabilityRect = durabilityText.rectTransform;
            durabilityRect.anchorMin = durabilityRect.anchorMax = new Vector2(0.5f, 1f);
            durabilityRect.pivot = new Vector2(0.5f, 0.5f);
            durabilityRect.anchoredPosition = new Vector2(120f, repairRowY);
            durabilityRect.sizeDelta = new Vector2(120f, 44f);
            repairUI.durabilityLabels[i] = durabilityText;

            Button repairButton = MainMenuController.CreateButton(repairGO.transform, "Reparer", uiFont, 0f, () => repairUI.Repair(rowIndex));
            RectTransform repairButtonRect = repairButton.GetComponent<RectTransform>();
            repairButtonRect.anchoredPosition = new Vector2(280f, repairRowY);
            repairButtonRect.sizeDelta = new Vector2(150f, 44f);
            repairUI.repairButtons[i] = repairButton;

            Button dismantleButton = MainMenuController.CreateButton(repairGO.transform, "Demonter", uiFont, 0f, () => repairUI.Dismantle(rowIndex));
            RectTransform dismantleButtonRect = dismantleButton.GetComponent<RectTransform>();
            dismantleButtonRect.anchoredPosition = new Vector2(450f, repairRowY);
            dismantleButtonRect.sizeDelta = new Vector2(150f, 44f);
            repairUI.dismantleButtons[i] = dismantleButton;

            repairRowY -= 60f;
        }

        MainMenuController.CreateButton(repairGO.transform, "Fermer", uiFont, repairRowY - 20f, repairUI.Hide);

        repairGO.SetActive(false);
        repairUI.root = repairGO;

        // --- Boss health bar (top-center, hidden until a boss binds to it) ---
        GameObject bossBarGO = new GameObject("BossHealthBar", typeof(RectTransform), typeof(Image), typeof(BossHealthBarUI));
        bossBarGO.transform.SetParent(canvasGO.transform, false);
        Image bossBarBackground = bossBarGO.GetComponent<Image>();
        bossBarBackground.color = new Color(0.15f, 0.05f, 0.05f, 0.85f);
        RectTransform bossBarRect = bossBarBackground.rectTransform;
        bossBarRect.anchorMin = bossBarRect.anchorMax = new Vector2(0.5f, 1f);
        bossBarRect.pivot = new Vector2(0.5f, 1f);
        // Below the floor timer/biome label stack (which ends around y=-98) - was at -30, directly
        // overlapping them, since the boss bar predates that feature and nobody checked for
        // collision since it's only ever visible during an actual boss fight.
        bossBarRect.anchoredPosition = new Vector2(0f, -110f);
        bossBarRect.sizeDelta = new Vector2(900f, 40f);

        GameObject bossBarFillGO = new GameObject("Fill", typeof(Image));
        bossBarFillGO.transform.SetParent(bossBarGO.transform, false);
        Image bossBarFill = bossBarFillGO.GetComponent<Image>();
        bossBarFill.sprite = uiFillSprite;
        bossBarFill.color = new Color(0.75f, 0.1f, 0.1f);
        bossBarFill.type = Image.Type.Filled;
        bossBarFill.fillMethod = Image.FillMethod.Horizontal;
        bossBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform bossBarFillRect = bossBarFill.rectTransform;
        bossBarFillRect.anchorMin = Vector2.zero;
        bossBarFillRect.anchorMax = Vector2.one;
        bossBarFillRect.offsetMin = new Vector2(4f, 4f);
        bossBarFillRect.offsetMax = new Vector2(-4f, -4f);

        BossHealthBarUI bossHealthBar = bossBarGO.GetComponent<BossHealthBarUI>();
        bossHealthBar.root = bossBarGO;
        bossHealthBar.fill = bossBarFill;

        // Deactivated only now, after the Fill child and its Image are fully built - creating a
        // child Graphic under an already-inactive parent risked it never getting a first proper
        // mesh/material rebuild once later reactivated by Bind(), leaving fillAmount changes
        // updating the logical value (confirmed correct via the diagnostic log) without ever
        // being redrawn.
        bossBarGO.SetActive(false);

        foreach (BossRoomController bossRoomController in bossRoomControllers)
        {
            bossRoomController.victoryBanner = victoryBanner;
            bossRoomController.healthBar = bossHealthBar;
            bossRoomController.introUI = bossIntroUI;
            bossRoomController.introVoice = bossIntroVoice;
        }

        Debug.Log("DungeonGenerator: floor generated with " + layout.Count + " rooms (" + eliteRoomCount + " with an elite).");
    }

    // Extracted from Build() (2026-09-16 cleanup, finding #9 of the full-codebase review) - every
    // sprite/tile this floor needs plus the full item registration pass (RegisterItem calls),
    // exactly as inlined before, just moved out of Build()'s own body. Pure asset creation/data
    // registration: none of it touches UnityEngine.Random, so extracting it changes nothing about
    // Random consumption order relative to the rest of Build() (see the floorTheme roll, which DOES
    // consume Random and was deliberately left inline in Build() itself, computed just before this
    // call, rather than moved in here).
    // Extracted from Build() (2026-09-16 cleanup, finding #9 of the full-codebase review) - part 1
    // of 2, split around the floorTheme roll that stays inline in Build() itself (see its comment
    // there for why: floorTheme consumes UnityEngine.Random, this method must not, so it can only
    // safely sit either entirely before or entirely after that roll in Build()'s call sequence -
    // this half is everything from the original code BEFORE the roll).
    static FloorAssets BuildFloorAssetsPart1(BiomeTheme biomeTheme)
    {
        FloorAssets assets = new FloorAssets();

        assets.floorSprite = CreateTexturedFloorSprite("Assets/Art/Tiles/Floor.png", biomeTheme.floorColor);
        assets.wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", biomeTheme.wallFaceColor, biomeTheme.wallTopColor, biomeTheme.wallEdgeColor);
        // "PlayerHero" is a real Kenney character tile (see KenneyCharacterSlicer.SlicePlayerSprite
        // + Dungeon/Rebuild Icon Pack Data) - falls back to the old flat circle if that bake step
        // was never run, so a missing/unbaked asset never means an invisible player.
        assets.playerSprite = LoadIconPackSprite("PlayerHero") ?? CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));
        Sprite zombieSprite = CreateMaskedSprite("Assets/Art/Enemies/Zombie.png", ZombieMask, new Color(0.25f, 0.4f, 0.2f));
        Sprite chauveSourisSprite = CreateMaskedSprite("Assets/Art/Enemies/ChauveSouris.png", ChauveSourisMask, new Color(0.3f, 0.15f, 0.35f));
        Sprite larveSprite = CreateMaskedSprite("Assets/Art/Enemies/Larve.png", LarveMask, new Color(0.8f, 0.85f, 0.5f));
        Sprite momieSprite = CreateMaskedSprite("Assets/Art/Enemies/Momie.png", MomieMask, new Color(0.82f, 0.76f, 0.6f));
        Sprite sanglierSprite = CreateMaskedSprite("Assets/Art/Enemies/Sanglier.png", SanglierMask, new Color(0.32f, 0.2f, 0.12f));
        Sprite skinwalkerSprite = CreateMaskedSprite("Assets/Art/Enemies/Skinwalker.png", SkinwalkerMask, new Color(0.3f, 0.24f, 0.34f));
        Sprite sorcierSprite = CreateMaskedSprite("Assets/Art/Enemies/Sorcier.png", SorcierMask, new Color(0.35f, 0.15f, 0.5f));
        Sprite sorcierProjectileSprite = CreateCircleSprite("Assets/Art/Fx/SorcierProjectile.png", new Color(0.55f, 0.85f, 0.95f));
        assets.bossProjectileSprite = CreateCircleSprite("Assets/Art/Fx/BossProjectile.png", new Color(0.85f, 0.2f, 0.15f));
        assets.bossSlobberPuddleSprite = CreateCircleSprite("Assets/Art/Fx/BossSlobberPuddle.png", new Color(0.45f, 0.55f, 0.2f));
        Sprite stoneBlockSprite = CreateSolidSprite("Assets/Art/Decor/StoneBlock.png", new Color(0.42f, 0.4f, 0.38f));
        Sprite woodDebrisSprite = CreateSolidSprite("Assets/Art/Decor/WoodDebris.png", new Color(0.55f, 0.4f, 0.25f));
        Sprite metalDebrisSprite = CreateSolidSprite("Assets/Art/Decor/MetalDebris.png", new Color(0.5f, 0.53f, 0.58f));
        Sprite barrelSprite = CreateSolidSprite("Assets/Art/Decor/Barrel.png", new Color(0.5f, 0.25f, 0.15f));
        Sprite fuelPuddleSprite = CreateCircleSprite("Assets/Art/Decor/FuelPuddle.png", new Color(0.12f, 0.1f, 0.08f));
        Sprite floorTrapSprite = CreateCircleSprite("Assets/Art/Decor/FloorTrap.png", new Color(0.2f, 0.08f, 0.08f));
        Sprite holeSprite = CreateCircleSprite("Assets/Art/Decor/Hole.png", new Color(0.03f, 0.03f, 0.04f));
        // Backrooms-only decor (see SpawnBackroomsDecor) - purely decorative, no colliders, so an
        // absurdly-scaled chair or a door standing in the open floor is just visual "loufoque"
        // flavor, zero gameplay risk.
        Sprite backroomsChairSprite = CreateSolidSprite("Assets/Art/Decor/BackroomsChair.png", new Color(0.55f, 0.4f, 0.15f));
        Sprite backroomsPillarSprite = CreateSolidSprite("Assets/Art/Decor/BackroomsPillar.png", new Color(0.75f, 0.68f, 0.35f));
        Sprite backroomsDoorSprite = CreateSolidSprite("Assets/Art/Decor/BackroomsDoor.png", new Color(0.45f, 0.32f, 0.12f));
        Sprite backroomsStainSprite = CreateCircleSprite("Assets/Art/Decor/BackroomsStain.png", new Color(0.35f, 0.3f, 0.08f, 0.6f));
        // Tavern/shop furniture (see SpawnTavernFurniture/SpawnShopFurniture) - same "one plain
        // sprite, stretched/rotated/tinted per prop" trick as the Backrooms decor above, so a
        // handful of squares becomes a bar counter, tables, chairs, crates and shelves.
        assets.furnitureWoodSprite = CreateSolidSprite("Assets/Art/Decor/FurnitureWood.png", new Color(0.42f, 0.28f, 0.16f));
        assets.rugSprite = CreateSolidSprite("Assets/Art/Decor/Rug.png", new Color(0.55f, 0.15f, 0.15f));
        assets.wallDecorSprite = CreateSolidSprite("Assets/Art/Decor/WallDecor.png", new Color(0.35f, 0.3f, 0.55f));
        assets.shopCrateSprite = CreateSolidSprite("Assets/Art/Decor/ShopCrate.png", new Color(0.5f, 0.38f, 0.22f));
        assets.decorSprites = new DecorSprites
        {
            stoneBlock = stoneBlockSprite,
            woodDebris = woodDebrisSprite,
            metalDebris = metalDebrisSprite,
            barrel = barrelSprite,
            fuelPuddle = fuelPuddleSprite,
            floorTrap = floorTrapSprite,
            hole = holeSprite,
            backroomsChair = backroomsChairSprite,
            backroomsPillar = backroomsPillarSprite,
            backroomsDoor = backroomsDoorSprite,
            backroomsStain = backroomsStainSprite,
        };
        assets.enemyGlowSprite = CreateGlowSprite("Assets/Art/Fx/EnemyGlow.png");
        assets.speedUpBadge = LoadIconPackSprite("ThunderStrike_Bright");
        assets.hpUpBadge = LoadIconPackSprite("Heart02_Bright");

        // XP rewards halved-ish across the board (was 3/2/1, boss was 20) - explicit request to
        // slow down leveling now that level-ups also grant attribute points to allocate (see
        // PlayerStats.AddExperience/AttributePointsPerLevel) - free levels were coming too easily.
        //
        // maxHealth/contactDamage fully re-rebalanced 2026-09-20, then maxHealth retuned again
        // 2026-09-21 ("une chauve souris devrait mourrir en 3 coups sans arme, 1 ou 2 coup d'epee
        // maximum") once real playtesting showed the 2026-09-20 numbers still took far more hits
        // than intended - the real culprit was EnemyLimbLayout's part count (see its own comment:
        // a coupon-collector tax on top of raw HP, independent of the number picked here), fixed
        // there. These HP values now assume that fix: Larve/ChauveSouris are single-part blobs, so
        // hits-to-kill is a plain ceil(HP/damage) - fist=6/sword=14 (PlayerController) against 18
        // HP gives exactly 3 fist / 2 sword for ChauveSouris, 12 HP gives 2 fist / 1 sword for the
        // even-weaker Larve. Zombie stays the tanky bruiser (4-part Grunt layout, some coupon-
        // collector tax still intended) but was eased from 70 to 50 all the same.
        // Momie/Sanglier/Skinwalker added 2026-09-21 alongside the above ("on a pas assez de
        // monstres differents... rajoute quelques mobs") - same ceil(HP/damage) reasoning per their
        // own EnemyLimbLayout entry. Momie shares Zombie's 4-part Grunt (tanky undead shambler,
        // slightly beefier - 55 vs 50); Sanglier/Skinwalker are single-part Blobs like ChauveSouris/
        // Larve (fast charger, eerie hard-hitting predator) so their hit count stays deterministic.
        assets.enemyPresets = new RoomController.EnemyPresetEntry[]
        {
            new RoomController.EnemyPresetEntry { type = EnemyType.Zombie, sprite = zombieSprite, maxHealth = 50, contactDamage = 12, isFlying = false, xpReward = 2 },
            new RoomController.EnemyPresetEntry { type = EnemyType.ChauveSouris, sprite = chauveSourisSprite, maxHealth = 18, contactDamage = 8, isFlying = true, xpReward = 1 },
            new RoomController.EnemyPresetEntry { type = EnemyType.Larve, sprite = larveSprite, maxHealth = 12, contactDamage = 4, isFlying = false, xpReward = 1 },
            new RoomController.EnemyPresetEntry { type = EnemyType.Momie, sprite = momieSprite, maxHealth = 55, contactDamage = 13, isFlying = false, xpReward = 2 },
            new RoomController.EnemyPresetEntry { type = EnemyType.Sanglier, sprite = sanglierSprite, maxHealth = 20, contactDamage = 10, isFlying = false, xpReward = 1 },
            new RoomController.EnemyPresetEntry { type = EnemyType.Skinwalker, sprite = skinwalkerSprite, maxHealth = 24, contactDamage = 14, isFlying = false, xpReward = 2 },
            // Sorcier (2026-09-21, "mobs type sorcier avec une ia fuyarde") - low HP/contact damage,
            // a fragile Blob like ChauveSouris/Larve (see EnemyLimbLayout), since its real threat is
            // the ranged spell (EnemyController.sorcierProjectileDamage below), not melee.
            new RoomController.EnemyPresetEntry { type = EnemyType.Sorcier, sprite = sorcierSprite, maxHealth = 16, contactDamage = 5, isFlying = false, xpReward = 2, projectileSprite = sorcierProjectileSprite },
        };

        return assets;
    }

    // Part 2 of 2 (see BuildFloorAssetsPart1 above) - everything from the original code AFTER the
    // floorTheme roll, including the full item-registration pass. Mutates the SAME FloorAssets
    // instance Part1 returned (a class, passed by reference) rather than returning a second one.
    static void BuildFloorAssetsPart2(FloorAssets assets, out List<ItemCatalog.Entry> itemEntries)
    {
        assets.shopMarker = CreateMaskedSprite("Assets/Art/Markers/Shop.png", ChestMask, new Color(0.75f, 0.55f, 0.15f));
        assets.treasureMarker = CreateSolidSprite("Assets/Art/Markers/Treasure.png", new Color(0.85f, 0.7f, 0.2f));
        assets.chestSprite = CreateMaskedSprite("Assets/Art/Items/Chest.png", ChestMask, new Color(0.5f, 0.32f, 0.15f));
        assets.secretMarker = CreateMaskedSprite("Assets/Art/Markers/Secret.png", QuestionMask, new Color(0.85f, 0.85f, 0.9f));
        assets.gambleMarker = CreateSolidSprite("Assets/Art/Markers/Gamble.png", new Color(0.85f, 0.35f, 0.15f));
        assets.bossMarker = CreateMaskedSprite("Assets/Art/Markers/Boss.png", SkullMask, new Color(0.9f, 0.9f, 0.92f));
        assets.eventMarker = CreateMaskedSprite("Assets/Art/Markers/Event.png", ExclamationMask, new Color(0.55f, 0.25f, 0.85f));
        assets.safeMarker = LoadIconPackSprite("Shield_Bright");
        assets.stairsMarker = LoadIconPackSprite("Exit_Bright");
        assets.stairsCageSprite = CreateMaskedSprite("Assets/Art/Fx/StairsCage.png", CageMask, new Color(0.16f, 0.15f, 0.18f));
        // Bright saturated gold - deliberately unlike every decor tint on this floor (stone/wood/
        // metal/barrel are all muted earth tones, see DecorSprites) so it never blends in with
        // ordinary scenery, on top of the distinct LeverMask shape above.
        assets.leverSprite = CreateMaskedSprite("Assets/Art/Decor/Lever.png", LeverMask, new Color(0.85f, 0.7f, 0.15f));
        assets.craftingTableSprite = CreateSolidSprite("Assets/Art/Decor/CraftingTable.png", new Color(0.45f, 0.32f, 0.2f));
        // Distinct teal-green from the crafting table's wood-brown (2026-09-21 request: separate
        // Table d'Alchimie for consumables vs Table de Craft for equipment).
        assets.alchemyTableSprite = CreateSolidSprite("Assets/Art/Decor/AlchemyTable.png", new Color(0.25f, 0.55f, 0.45f));

        assets.projectileSprite = CreateCircleSprite("Assets/Art/Projectile.png", new Color(0.6f, 0.85f, 0.95f));
        Sprite swordPickupSprite = CreateMaskedSprite("Assets/Art/Items/Sword.png", SwordMask, new Color(0.75f, 0.78f, 0.82f));
        Sprite staffPickupSprite = CreateMaskedSprite("Assets/Art/Items/Staff.png", StaffMask, new Color(0.5f, 0.25f, 0.65f));
        Sprite goldSprite = LoadIconPackSprite("Coin_Bright");
        Sprite shurikenSprite = CreateMaskedSprite("Assets/Art/Items/Shuriken.png", ShurikenMask, new Color(0.6f, 0.6f, 0.65f));
        Sprite caillouSprite = CreateCircleSprite("Assets/Art/Items/Caillou.png", new Color(0.45f, 0.42f, 0.4f));
        Sprite batonSprite = CreateMaskedSprite("Assets/Art/Items/Baton.png", BatonMask, new Color(0.5f, 0.35f, 0.2f));

        assets.fistVisualSprite = CreateCircleSprite("Assets/Art/Fx/FistHit.png", new Color(0.95f, 0.95f, 0.9f));
        assets.swordVisualSprite = CreateRectSprite("Assets/Art/Fx/SwordSlash.png", new Color(0.85f, 0.9f, 0.95f));
        // Electric cyan/violet, deliberately unlike the plain Projectile tint above so the orb
        // reads as a distinct spell rather than another staff bolt.
        assets.lightningOrbSprite = CreateCircleSprite("Assets/Art/Fx/LightningOrb.png", new Color(0.55f, 0.85f, 1f));
        assets.lightningBoltSprite = CreateRectSprite("Assets/Art/Fx/LightningBolt.png", new Color(0.75f, 0.9f, 1f));
        assets.fireballSprite = CreateCircleSprite("Assets/Art/Fx/Fireball.png", new Color(0.95f, 0.45f, 0.1f));
        assets.fireLineSprite = CreateCircleSprite("Assets/Art/Fx/FireTrail.png", new Color(0.85f, 0.35f, 0.1f, 0.85f));
        assets.burnIconSprite = CreateCircleSprite("Assets/Art/Fx/BurnIcon.png", new Color(0.95f, 0.55f, 0.15f));

        Sprite bombSprite = CreateCircleSprite("Assets/Art/Items/Bomb.png", new Color(0.15f, 0.15f, 0.17f));
        assets.explosionSprite = CreateCircleSprite("Assets/Art/Fx/Explosion.png", new Color(0.95f, 0.55f, 0.15f));
        assets.decorSprites.explosion = assets.explosionSprite;

        // Registered immediately (usable right away by SpawnItemPickup etc. below) and also baked
        // into an ItemCatalog placed in the scene, so the same data re-registers itself into
        // ItemDatabase when the game is actually played later, without DungeonBootstrap running.
        ItemDatabase.Clear();
        itemEntries = new List<ItemCatalog.Entry>();
        RegisterItem(itemEntries, ItemIds.Gold, "Or", ItemCategory.Autre, 100, goldSprite, rarity: 1);
        RegisterItem(itemEntries, ItemIds.Shuriken, "Shuriken", ItemCategory.Consommable, 10, shurikenSprite, isThrowable: true, rarity: 1);
        RegisterItem(itemEntries, ItemIds.Caillou, "Caillou", ItemCategory.Consommable, 10, caillouSprite, isThrowable: true, rarity: 1);
        RegisterItem(itemEntries, ItemIds.Baton, "Baton", ItemCategory.Consommable, 10, batonSprite, isThrowable: true, rarity: 1);
        RegisterItem(itemEntries, ItemIds.Bomb, "Bombe", ItemCategory.Consommable, 10, bombSprite, isThrowable: true, rarity: 2);

        Sprite swordItemSprite = swordPickupSprite;
        Sprite staffItemSprite = staffPickupSprite;
        RegisterItem(itemEntries, ItemIds.Sword, "Epee", ItemCategory.Equipement, 1, swordItemSprite,
            "Une epee standard, equilibree.", isEquipment: true, equipmentSlot: EquipmentSlotType.Weapon,
            isWeapon: true, weaponType: PlayerController.WeaponType.Sword, maxDurability: 40, material: MaterialType.Metal, rarity: 2);
        RegisterItem(itemEntries, ItemIds.Staff, "Baton Magique", ItemCategory.Equipement, 1, staffItemSprite,
            "Un baton qui canalise des projectiles magiques.", isEquipment: true, equipmentSlot: EquipmentSlotType.Weapon,
            isWeapon: true, weaponType: PlayerController.WeaponType.Staff, maxDurability: 40, material: MaterialType.Bois, rarity: 2);

        // Special ground-only items showcasing weight/curse/trap - placed rarely by SpawnRoomDecor,
        // never in the common LootTable drop pool.
        Sprite anvilSprite = CreateMaskedSprite("Assets/Art/Items/Anvil.png", AnvilMask, new Color(0.2f, 0.2f, 0.22f));
        Sprite cursedSwordSprite = CreateMaskedSprite("Assets/Art/Items/CursedSword.png", CursedSwordMask, new Color(0.35f, 0.1f, 0.4f));
        Sprite trapSackSprite = CreateMaskedSprite("Assets/Art/Items/TrapSack.png", TrapSackMask, new Color(0.5f, 0.4f, 0.3f));
        RegisterItem(itemEntries, ItemIds.Anvil, "Enclume", ItemCategory.Autre, 1, anvilSprite,
            "Une lourde enclume de forgeron.", weight: 5, rarity: 2);
        // "L'Epee Maudite du Soldat Dechu" - one specific cursed weapon (not a generic label; a
        // future cursed item can be named however it likes, curse status is never spelled out in
        // its own text, see ItemInspectManager). Always crits (+50% damage) but a swing that
        // connects with nothing costs 10% of max HP - see PlayerController.MeleeAttack.
        RegisterItem(itemEntries, ItemIds.CursedSword, "Epee Maudite du Soldat Dechu", ItemCategory.Equipement, 1, cursedSwordSprite,
            "Une lame ancienne. Une presence malveillante semble y sommeiller.",
            isCursed: true, hasCursedWeapon: true, cursedWeaponType: PlayerController.WeaponType.Sword,
            maxDurability: 40, material: MaterialType.Metal, rarity: 6);
        RegisterItem(itemEntries, ItemIds.TrapSack, "Sac Abandonne", ItemCategory.Consommable, 1, trapSackSprite,
            "Un petit sac abandonne. Qui l'aurait laisse la ?", isTrap: true, rarity: 2);

        Sprite potionSprite = CreateCircleSprite("Assets/Art/Items/Potion.png", new Color(0.8f, 0.15f, 0.35f));
        RegisterItem(itemEntries, ItemIds.HealthPotion, "Petite Potion de Vie", ItemCategory.Consommable, 5, potionSprite,
            "Restaure 20 PV.", healAmount: 20, rarity: 1);

        // New potions (2026-09-21 request: Table d'Alchimie) - each a plain reskin of the potion
        // silhouette, distinguished by tint like the boss materials above.
        Sprite greaterPotionSprite = CreateCircleSprite("Assets/Art/Items/GreaterPotion.png", new Color(0.85f, 0.1f, 0.15f));
        RegisterItem(itemEntries, ItemIds.GreaterHealthPotion, "Grande Potion de Vie", ItemCategory.Consommable, 5, greaterPotionSprite,
            "Restaure 50 PV.", healAmount: 50, rarity: 2);
        Sprite speedPotionSprite = CreateCircleSprite("Assets/Art/Items/SpeedPotion.png", new Color(0.15f, 0.75f, 0.85f));
        RegisterItem(itemEntries, ItemIds.SpeedPotion, "Potion de Vitesse", ItemCategory.Consommable, 5, speedPotionSprite,
            "Augmente la vitesse de deplacement de 10% pendant 2 minutes.", speedBuffMultiplier: 1.1f, speedBuffDuration: 120f, rarity: 2);
        Sprite adrenalinePotionSprite = CreateCircleSprite("Assets/Art/Items/AdrenalinePotion.png", new Color(0.9f, 0.55f, 0.1f));
        RegisterItem(itemEntries, ItemIds.AdrenalinePotion, "Potion d'Adrenaline", ItemCategory.Consommable, 5, adrenalinePotionSprite,
            "Double la vitesse de recuperation d'endurance pendant 2 minutes.", staminaRegenBuffMultiplier: 2f, staminaRegenBuffDuration: 120f, rarity: 2);

        Sprite cerberusCollarSprite = CreateMaskedSprite("Assets/Art/Items/CerberusCollar.png", CerberusCollarMask, new Color(0.75f, 0.6f, 0.15f));
        RegisterItem(itemEntries, ItemIds.CerberusCollar, "Collier Infernal du Cerbere", ItemCategory.Equipement, 1, cerberusCollarSprite,
            "Un collier de bronze encore chaud, arrache au Cerbere. Un trophee de votre victoire.",
            isEquipment: true, equipmentSlot: EquipmentSlotType.Neck, rarity: 5);

        // The other 6 boss family trophies (see BossFamilyFor) - plain collectible drops, not
        // equipment (only the Cerberus Collar was singled out for that earlier). No other use yet
        // (no quest/crafting system consumes them) - ObjetDeQuete fits a pure proof-of-victory
        // collectible better than Ressource (2026-09-21 inventory categories request).
        Sprite anacondaScaleSprite = CreateCircleSprite("Assets/Art/Items/AnacondaScale.png", new Color(0.2f, 0.55f, 0.2f));
        RegisterItem(itemEntries, ItemIds.AnacondaScale, "Ecaille d'Anaconda Royale", ItemCategory.ObjetDeQuete, 1, anacondaScaleSprite,
            "Une ecaille massive, encore luisante. Un trophee de votre victoire.", rarity: 5);
        Sprite entHeartshardSprite = CreateCircleSprite("Assets/Art/Items/EntHeartshard.png", new Color(0.35f, 0.28f, 0.12f));
        RegisterItem(itemEntries, ItemIds.EntHeartshard, "Eclat de Coeur d'Ent", ItemCategory.ObjetDeQuete, 1, entHeartshardSprite,
            "Un fragment de bois anime, encore chaud de seve. Un trophee de votre victoire.", rarity: 5);
        Sprite golemCoreSprite = CreateCircleSprite("Assets/Art/Items/GolemCore.png", new Color(0.55f, 0.56f, 0.6f));
        RegisterItem(itemEntries, ItemIds.GolemCore, "Noyau du Golem d'Acier", ItemCategory.ObjetDeQuete, 1, golemCoreSprite,
            "Le noyau qui animait un golem de fer et d'acier. Un trophee de votre victoire.", rarity: 5);
        Sprite krakenTentacleSprite = CreateCircleSprite("Assets/Art/Items/KrakenTentacle.png", new Color(0.1f, 0.25f, 0.45f));
        RegisterItem(itemEntries, ItemIds.KrakenTentacle, "Tentacule Petrifiee du Kraken", ItemCategory.ObjetDeQuete, 1, krakenTentacleSprite,
            "Une ventouse geante, figee net. Un trophee de votre victoire.", rarity: 5);
        Sprite eagleCogSprite = CreateCircleSprite("Assets/Art/Items/EagleCog.png", new Color(0.75f, 0.7f, 0.55f));
        RegisterItem(itemEntries, ItemIds.EagleCog, "Rouage de l'Aigle Mecanique", ItemCategory.ObjetDeQuete, 1, eagleCogSprite,
            "Un rouage dore, encore tiede des mecanismes de l'aigle. Un trophee de votre victoire.", rarity: 5);
        Sprite wandererFragmentSprite = CreateCircleSprite("Assets/Art/Items/WandererFragment.png", new Color(0.65f, 0.6f, 0.25f));
        RegisterItem(itemEntries, ItemIds.WandererFragment, "Fragment de l'Arpenteur", ItemCategory.ObjetDeQuete, 1, wandererFragmentSprite,
            "Un morceau de moquette jaune, etrangement lourd. Un trophee de votre victoire.", rarity: 5);

        // Boss crafting materials - 3 per family (see BossFamilyFor.resourceDropIds), destined for
        // future potion/equipment recipes (2026-09-21 request, none exist yet). Stack like any
        // other Ressource material (20, rarity 3 - rarer than plain gathered materials since only
        // a boss drops them, but well below the family trophy's own rarity 5).
        RegisterItem(itemEntries, ItemIds.CerbereClaw, "Griffe de Cerbere", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/CerbereClaw.png", new Color(0.15f, 0.13f, 0.13f)),
            "Une griffe massive, encore acree.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.CerbereFang, "Croc de Cerbere", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/CerbereFang.png", new Color(0.9f, 0.88f, 0.8f)),
            "Un croc long comme l'avant-bras, encore tranchant.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.CerbereFur, "Poils de Cerbere", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/CerbereFur.png", new Color(0.25f, 0.18f, 0.15f)),
            "Une touffe de poils rugueux, encore chauds.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.AnacondaFang, "Croc d'Anaconda", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AnacondaFang.png", new Color(0.92f, 0.9f, 0.82f)),
            "Un croc recourbe, encore charge de venin.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.AnacondaEye, "Oeil d'Anaconda", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AnacondaEye.png", new Color(0.85f, 0.75f, 0.15f)),
            "Un oeil reptilien a la pupille verticale.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.AnacondaSkin, "Peau d'Anaconda", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AnacondaSkin.png", new Color(0.25f, 0.5f, 0.22f)),
            "Un morceau de peau epaisse et souple.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.EntBark, "Ecorce d'Ent", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/EntBark.png", new Color(0.32f, 0.24f, 0.14f)),
            "Un fragment d'ecorce dure comme la pierre.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.EntRoot, "Racine d'Ent", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/EntRoot.png", new Color(0.45f, 0.35f, 0.2f)),
            "Une racine noueuse, encore fraiche.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.EntSap, "Seve d'Ent", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/EntSap.png", new Color(0.75f, 0.55f, 0.15f)),
            "Une seve doree et poisseuse, tiede au toucher.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.GolemPlate, "Plaque d'Acier du Golem", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/GolemPlate.png", new Color(0.55f, 0.56f, 0.6f)),
            "Une plaque de blindage arrachee au golem.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.GolemRivet, "Rivet Rouille du Golem", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/GolemRivet.png", new Color(0.5f, 0.3f, 0.15f)),
            "Un gros rivet couvert de rouille.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.GolemCable, "Cable Effiloche du Golem", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/GolemCable.png", new Color(0.2f, 0.2f, 0.22f)),
            "Un cable metallique effiloche, encore sous tension.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.KrakenEye, "Oeil de Kraken", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/KrakenEye.png", new Color(0.85f, 0.35f, 0.2f)),
            "Un oeil globuleux de la taille d'un poing.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.KrakenSucker, "Ventouse de Kraken", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/KrakenSucker.png", new Color(0.55f, 0.25f, 0.3f)),
            "Une ventouse geante, encore visqueuse.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.KrakenInk, "Encre de Kraken", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/KrakenInk.png", new Color(0.08f, 0.08f, 0.12f)),
            "Une fiole d'encre noire et epaisse.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.AigleFeather, "Plume d'Acier de l'Aigle", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AigleFeather.png", new Color(0.8f, 0.78f, 0.7f)),
            "Une plume metallique, aiguisee sur les bords.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.AigleTalon, "Serre d'Acier de l'Aigle", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AigleTalon.png", new Color(0.6f, 0.62f, 0.65f)),
            "Une serre articulee, toujours aussi acree.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.AigleSensor, "Capteur Optique de l'Aigle", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/AigleSensor.png", new Color(0.2f, 0.7f, 0.85f)),
            "Une lentille optique qui luit encore faiblement.", rarity: 3);

        RegisterItem(itemEntries, ItemIds.ArpenteurShadow, "Lambeau d'Ombre de l'Arpenteur", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/ArpenteurShadow.png", new Color(0.15f, 0.13f, 0.18f)),
            "Un lambeau d'ombre qui refuse de se dissiper.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.ArpenteurDust, "Poussiere Jaune de l'Arpenteur", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/ArpenteurDust.png", new Color(0.75f, 0.68f, 0.2f)),
            "Une poussiere jaunatre a l'odeur de moquette.", rarity: 3);
        RegisterItem(itemEntries, ItemIds.ArpenteurEcho, "Echo Silencieux de l'Arpenteur", ItemCategory.Ressource, 20,
            CreateCircleSprite("Assets/Art/Items/ArpenteurEcho.png", new Color(0.55f, 0.5f, 0.6f)),
            "Un echo fige qui ne fait plus aucun bruit.", rarity: 3);

        // Boss equipment crafts (2026-09-21 request: "met des craft de boss, par exemple la patoune
        // de cerbere... tu vois le style") - one themed piece per family, built at the Table de
        // Craft from that family's resourceDropIds plus a base equipment item (see
        // SpawnCraftingTable). Rarity 4 - above a plain shop ring/armor piece (2-3), below a boss's
        // own unique trophy (5).
        RegisterItem(itemEntries, ItemIds.CerberePaw, "Patoune de Cerbere", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/CerberePaw.png", new Color(0.2f, 0.15f, 0.13f)),
            "Une patte griffue montee sur une garde, aussi tranchante qu'une lame.",
            isEquipment: true, equipmentSlot: EquipmentSlotType.Weapon, isWeapon: true, weaponType: PlayerController.WeaponType.Sword,
            maxDurability: 50, material: MaterialType.Metal, rarity: 4);
        RegisterItem(itemEntries, ItemIds.AnacondaFangDagger, "Dague Crochet d'Anaconda", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/AnacondaFangDagger.png", new Color(0.3f, 0.55f, 0.25f)),
            "Un crochet venimeux monte en dague, toujours suintant.",
            isEquipment: true, equipmentSlot: EquipmentSlotType.Weapon, isWeapon: true, weaponType: PlayerController.WeaponType.Sword,
            maxDurability: 50, material: MaterialType.Metal, rarity: 4);
        RegisterItem(itemEntries, ItemIds.EntBarkPauldrons, "Epaulieres d'Ecorce d'Ent", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/EntBarkPauldrons.png", new Color(0.35f, 0.26f, 0.15f)),
            "Protege le torse (+2 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Shoulders, armorValue: 2,
            maxDurability: 30, material: MaterialType.Bois, rarity: 4);
        RegisterItem(itemEntries, ItemIds.GolemPlateGauntlets, "Gantelets de Plaque du Golem", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/GolemPlateGauntlets.png", new Color(0.5f, 0.52f, 0.56f)),
            "Protege les bras (+2 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Gloves, armorValue: 2,
            maxDurability: 30, material: MaterialType.Metal, rarity: 4);
        RegisterItem(itemEntries, ItemIds.KrakenInkNecklace, "Collier d'Encre du Kraken", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/KrakenInkNecklace.png", new Color(0.1f, 0.1f, 0.15f)),
            "Protege le torse (+2 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Neck, armorValue: 2,
            maxDurability: 20, material: MaterialType.Tissu, rarity: 4);
        RegisterItem(itemEntries, ItemIds.AigleFeatherBoots, "Bottes de Plume d'Acier", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/AigleFeatherBoots.png", new Color(0.75f, 0.73f, 0.68f)),
            "Protege les jambes (+2 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Boots, armorValue: 2,
            maxDurability: 40, material: MaterialType.Metal, rarity: 4);
        RegisterItem(itemEntries, ItemIds.ArpenteurShadowRing, "Anneau d'Ombre de l'Arpenteur", ItemCategory.Equipement, 1,
            CreateCircleSprite("Assets/Art/Items/ArpenteurShadowRing.png", new Color(0.2f, 0.18f, 0.25f)),
            "Se porte a n'importe quel doigt (+1 Dexterite).", isEquipment: true, equipmentSlot: EquipmentSlotType.RingLeft,
            ringBonusStat: StatType.Dexterite, rarity: 4);

        // Crafting materials - guaranteed drops from the matching decor material (see
        // SpawnRoomDecor/DestructibleObject.guaranteedDropItemId), spent at the Safe room's
        // crafting table (SpawnCraftingTable).
        Sprite woodMaterialSprite = CreateMaskedSprite("Assets/Art/Items/Wood.png", WoodMask, new Color(0.55f, 0.4f, 0.25f));
        Sprite metalMaterialSprite = CreateMaskedSprite("Assets/Art/Items/Metal.png", MetalMask, new Color(0.5f, 0.53f, 0.58f));
        Sprite stoneMaterialSprite = CreateMaskedSprite("Assets/Art/Items/Stone.png", StoneMask, new Color(0.42f, 0.4f, 0.38f));
        RegisterItem(itemEntries, ItemIds.Wood, "Bois", ItemCategory.Ressource, 20, woodMaterialSprite, "Du bois recupere sur des debris.", rarity: 1);
        RegisterItem(itemEntries, ItemIds.Metal, "Metal", ItemCategory.Ressource, 20, metalMaterialSprite, "Du metal recupere sur des debris.", rarity: 1);
        RegisterItem(itemEntries, ItemIds.Stone, "Pierre", ItemCategory.Ressource, 20, stoneMaterialSprite, "De la pierre recuperee sur un bloc.", rarity: 1);

        // Corpse-only material (see CorpseLoot.cs) - no destructible-decor source, only found on
        // NPC-type bodies.
        Sprite clothMaterialSprite = CreateMaskedSprite("Assets/Art/Items/Cloth.png", ClothMask, new Color(0.75f, 0.7f, 0.55f));
        RegisterItem(itemEntries, ItemIds.Cloth, "Tissu", ItemCategory.Ressource, 20, clothMaterialSprite, "Un morceau de tissu recupere sur une depouille.", rarity: 1);

        // Generic crafting ingredient (2026-09-21 request: Table de Craft) - made from Tissu, not
        // tied to any biome/boss, used by several boss-equipment recipes below.
        Sprite cordeSprite = CreateMaskedSprite("Assets/Art/Items/Corde.png", ClothMask, new Color(0.65f, 0.5f, 0.3f));
        RegisterItem(itemEntries, ItemIds.Corde, "Corde", ItemCategory.Ressource, 20, cordeSprite, "Une corde solide, tressee a partir de tissu.", rarity: 1);

        // Flora (see DecorType.Flower) - crafting-only ingredients, no heal/effect of their own,
        // spent at the Table de Craft for a Potion de Soin (see SpawnCraftingTable).
        Sprite flowerRedSprite = CreateCircleSprite("Assets/Art/Items/FlowerRed.png", new Color(0.85f, 0.15f, 0.25f));
        RegisterItem(itemEntries, ItemIds.FlowerRed, "Fleur Ecarlate", ItemCategory.Ressource, 20, flowerRedSprite, "Une fleur aux petales rouge vif.", rarity: 1);
        Sprite flowerBlueSprite = CreateCircleSprite("Assets/Art/Items/FlowerBlue.png", new Color(0.25f, 0.4f, 0.85f));
        RegisterItem(itemEntries, ItemIds.FlowerBlue, "Fleur Azur", ItemCategory.Ressource, 20, flowerBlueSprite, "Une fleur bleue au parfum leger.", rarity: 1);
        Sprite herbSprite = CreateCircleSprite("Assets/Art/Items/Herb.png", new Color(0.3f, 0.65f, 0.3f));
        RegisterItem(itemEntries, ItemIds.Herb, "Herbe Argentee", ItemCategory.Ressource, 20, herbSprite, "Une touffe d'herbe aux reflets argentes.", rarity: 1);
        Sprite mushroomSprite = CreateCircleSprite("Assets/Art/Items/Mushroom.png", new Color(0.8f, 0.65f, 0.2f));
        RegisterItem(itemEntries, ItemIds.Mushroom, "Champignon Dore", ItemCategory.Ressource, 20, mushroomSprite, "Un champignon a la teinte doree.", rarity: 2);

        // Equipment - one plain protective piece per slot (see EquipmentSlotType/PlayerEquipment),
        // sold at the Shop. Each grants +1 armor on the body part(s) its slot maps to (see
        // PlayerLimbs.GetArmor) - Torso is covered by Shoulders+Belt+Neck at once, Arms/Legs share
        // a single Gloves/Boots+Knees slot each, matching this project's one-slot-per-type layout.
        // Head/Boots/Knees are the "heavy" pieces (Metal, sturdier, fire-resistant); Shoulders/
        // Gloves/Neck/Belt are the "soft" pieces (Tissu - cloth/leather, lower durability, and the
        // only material that can catch fire, see PlayerEquipment.IgniteFlammable).
        Sprite ironHelmetSprite = CreateMaskedSprite("Assets/Art/Items/IronHelmet.png", HelmetMask, new Color(0.55f, 0.56f, 0.6f));
        Sprite leatherPauldronsSprite = CreateMaskedSprite("Assets/Art/Items/LeatherPauldrons.png", PauldronsMask, new Color(0.45f, 0.32f, 0.18f));
        Sprite combatGlovesSprite = CreateMaskedSprite("Assets/Art/Items/CombatGloves.png", GlovesMask, new Color(0.35f, 0.25f, 0.15f));
        Sprite walkingBootsSprite = CreateMaskedSprite("Assets/Art/Items/WalkingBoots.png", BootsMask, new Color(0.3f, 0.2f, 0.12f));
        Sprite simpleNecklaceSprite = CreateCircleSprite("Assets/Art/Items/SimpleNecklace.png", new Color(0.8f, 0.75f, 0.3f));
        Sprite leatherBeltSprite = CreateMaskedSprite("Assets/Art/Items/LeatherBelt.png", BeltMask, new Color(0.4f, 0.28f, 0.16f));
        Sprite leatherKneepadsSprite = CreateMaskedSprite("Assets/Art/Items/LeatherKneepads.png", KneepadsMask, new Color(0.42f, 0.3f, 0.17f));
        Sprite simpleRingSprite = CreateCircleSprite("Assets/Art/Items/SimpleRing.png", new Color(0.85f, 0.8f, 0.4f));
        RegisterItem(itemEntries, ItemIds.IronHelmet, "Casque de Fer", ItemCategory.Equipement, 1, ironHelmetSprite,
            "Protege la tete (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Head, armorValue: 1,
            maxDurability: 40, material: MaterialType.Metal, rarity: 2);
        RegisterItem(itemEntries, ItemIds.LeatherPauldrons, "Epaulieres de Cuir", ItemCategory.Equipement, 1, leatherPauldronsSprite,
            "Protege le torse (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Shoulders, armorValue: 1,
            maxDurability: 20, material: MaterialType.Tissu, rarity: 2);
        RegisterItem(itemEntries, ItemIds.CombatGloves, "Gants de Combat", ItemCategory.Equipement, 1, combatGlovesSprite,
            "Protege les bras (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Gloves, armorValue: 1,
            maxDurability: 20, material: MaterialType.Tissu, rarity: 2);
        RegisterItem(itemEntries, ItemIds.WalkingBoots, "Bottes de Marche", ItemCategory.Equipement, 1, walkingBootsSprite,
            "Protege les jambes (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Boots, armorValue: 1,
            maxDurability: 30, material: MaterialType.Metal, rarity: 2);
        RegisterItem(itemEntries, ItemIds.SimpleNecklace, "Collier Simple", ItemCategory.Equipement, 1, simpleNecklaceSprite,
            "Protege le torse (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Neck, armorValue: 1,
            maxDurability: 15, material: MaterialType.Tissu, rarity: 2);
        RegisterItem(itemEntries, ItemIds.LeatherBelt, "Ceinture de Cuir", ItemCategory.Equipement, 1, leatherBeltSprite,
            "Protege le torse (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Belt, armorValue: 1,
            maxDurability: 20, material: MaterialType.Tissu, rarity: 2);
        RegisterItem(itemEntries, ItemIds.LeatherKneepads, "Genouilleres de Cuir", ItemCategory.Equipement, 1, leatherKneepadsSprite,
            "Protege les jambes (+1 armure).", isEquipment: true, equipmentSlot: EquipmentSlotType.Knees, armorValue: 1,
            maxDurability: 30, material: MaterialType.Metal, rarity: 2);
        RegisterItem(itemEntries, ItemIds.SimpleRing, "Anneau Simple", ItemCategory.Equipement, 1, simpleRingSprite,
            "Se porte a n'importe quel doigt (+1 Endurance).", isEquipment: true, equipmentSlot: EquipmentSlotType.RingLeft,
            ringBonusStat: StatType.Endurance, rarity: 2);

        // Real shop wares (see SpawnMerchantNpc) - 8 stat rings (one random one sold per floor),
        // anti-hole boots (immune to the Hole debuff - see PlayerController.ApplyMovementDebuff),
        // vision glasses (reveals a secret room's bombable wall - see SecretWallBlocker).
        (string id, string name, StatType stat, Color color)[] ringDefs =
        {
            (ItemIds.RingForce, "Anneau de Force", StatType.Force, new Color(0.75f, 0.2f, 0.15f)),
            (ItemIds.RingDexterite, "Anneau de Dexterite", StatType.Dexterite, new Color(0.2f, 0.65f, 0.25f)),
            (ItemIds.RingIntelligence, "Anneau d'Intelligence", StatType.Intelligence, new Color(0.2f, 0.35f, 0.85f)),
            (ItemIds.RingVitesse, "Anneau de Vitesse", StatType.Vitesse, new Color(0.2f, 0.75f, 0.8f)),
            (ItemIds.RingConstitution, "Anneau de Constitution", StatType.Constitution, new Color(0.85f, 0.5f, 0.15f)),
            (ItemIds.RingPortee, "Anneau de Portee", StatType.Portee, new Color(0.55f, 0.25f, 0.75f)),
            (ItemIds.RingCharisme, "Anneau de Charisme", StatType.Charisme, new Color(0.85f, 0.4f, 0.65f)),
            (ItemIds.RingEndurance, "Anneau d'Endurance", StatType.Endurance, new Color(0.85f, 0.75f, 0.2f)),
        };
        foreach (var ring in ringDefs)
        {
            Sprite ringSprite = CreateCircleSprite("Assets/Art/Items/" + ring.id + ".png", ring.color);
            RegisterItem(itemEntries, ring.id, ring.name, ItemCategory.Equipement, 1, ringSprite,
                "+1 " + ring.stat + " tant qu'il est equipe.", isEquipment: true,
                equipmentSlot: EquipmentSlotType.RingLeft, ringBonusStat: ring.stat, rarity: 3);
        }

        Sprite antiHoleBootsSprite = CreateMaskedSprite("Assets/Art/Items/AntiHoleBoots.png", AntiHoleBootsMask, new Color(0.35f, 0.28f, 0.15f));
        RegisterItem(itemEntries, ItemIds.AntiHoleBoots, "Bottes Anti-Trous", ItemCategory.Equipement, 1, antiHoleBootsSprite,
            "Immunise contre le ralentissement des trous au sol.", isEquipment: true, equipmentSlot: EquipmentSlotType.Boots, rarity: 3);

        Sprite visionGlassesSprite = CreateMaskedSprite("Assets/Art/Items/VisionGlasses.png", GlassesMask, new Color(0.5f, 0.7f, 0.85f));
        RegisterItem(itemEntries, ItemIds.VisionGlasses, "Lunettes de Vision", ItemCategory.Equipement, 1, visionGlassesSprite,
            "Revele les murs dissimulant une salle secrete.", isEquipment: true, equipmentSlot: EquipmentSlotType.Head, rarity: 3);

        assets.doorBarrierSprite = CreateSolidSprite("Assets/Art/Fx/DoorBarrier.png", new Color(0.6f, 0.15f, 0.15f));
        // Same face color as the wall itself, so a secret room's bombable wall blends in - no
        // visual hint, on purpose (detection items are a separate future feature).
        assets.secretWallSprite = CreateSolidSprite("Assets/Art/Fx/SecretWall.png", new Color(0.10f, 0.09f, 0.11f));
        assets.outlineRingSprite = CreateRingSprite("Assets/Art/Markers/OutlineRing.png", TilePixelSize, 2, Color.white);
        // Real Kenney character tiles (see KenneyCharacterSlicer.SlicePlayerSprite), one per NPC
        // role, each with its own color tint applied at spawn time (SpawnExampleNpc etc. below) so
        // even a reused silhouette still reads as a distinct character - same fallback convention
        // as PlayerHero if the slice/bake step was never run.
        assets.npcStrangerSprite = LoadIconPackSprite("NpcStranger") ?? CreateCircleSprite("Assets/Art/Npc.png", new Color(0.35f, 0.55f, 0.75f));
        assets.npcElderSprite = LoadIconPackSprite("NpcElder") ?? assets.npcStrangerSprite;
        assets.npcMerchantSprite = LoadIconPackSprite("NpcMerchant") ?? assets.npcStrangerSprite;
        // Ground items are flat colored circles/squares too (no dedicated art beyond a handful of
        // icon-pack sprites) - without a marker, an NPC reads as just another item on the floor.
        // A floating "!" above the head (same convention as EnemyController's elite badges) fixes
        // that at a glance without needing new art.
        assets.npcBadgeSprite = LoadIconPackSprite("Quest01_Bright");

        assets.floorTile = CreateTileAsset("Assets/Art/Tiles/FloorTile.asset", assets.floorSprite, Tile.ColliderType.None);
        assets.wallTile = CreateTileAsset("Assets/Art/Tiles/WallTile.asset", assets.wallSprite, Tile.ColliderType.Grid);
    }

    // Called by Staircase.OnTriggerEnter2D once its lock is open - a fresh floor+1 with a new
    // random seed, carrying the player's full state across via the same Capture/Apply pair a
    // to-disk save uses (see SaveManager), just kept in memory instead of hitting the file.
    public static void Descend()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        SaveData carry = SaveManager.Capture(CurrentSeed, CurrentFloor + 1,
            player.GetComponent<PlayerInventory>(), player.GetComponent<PlayerStats>(),
            player.GetComponent<Health>(), player.GetComponent<Stamina>(), player.GetComponent<PlayerController>(),
            player.GetComponent<PlayerEquipment>(), limbs: player.GetComponent<PlayerLimbs>());

        Build(Random.Range(int.MinValue, int.MaxValue), CurrentFloor + 1);

        GameObject newPlayer = GameObject.FindWithTag("Player");
        if (newPlayer == null) return;
        // `carry` still holds the OLD floor's position (captured above, before Build() ran) -
        // overwrite it with wherever Build() just placed the new player (its Start room spawn)
        // before Apply() blindly restores `carry.playerPosition` onto them. Without this, a
        // player descending stairs would land on the new floor at their old floor's raw
        // coordinates instead of its actual spawn point (found live while adding SaveData.
        // playerPosition for the "Continuer" position fix, 2026-09-15).
        carry.playerPosition = newPlayer.transform.position;
        SaveManager.Apply(carry,
            newPlayer.GetComponent<PlayerInventory>(), newPlayer.GetComponent<PlayerStats>(),
            newPlayer.GetComponent<Health>(), newPlayer.GetComponent<Stamina>(), newPlayer.GetComponent<PlayerController>(),
            newPlayer.GetComponent<PlayerEquipment>(), newPlayer.GetComponent<PlayerLimbs>());
    }

    const int TutorialRoomWidth = RoomWidth;
    const int TutorialRoomHeight = RoomHeight;

    // A single hand-built room, not the procedural generator - kill the one Zombie, the Guide
    // appears and the portal (a Staircase, reused as-is) unblocks, and walking onto it Descend()s
    // straight into floor 1 with CurrentFloor starting at 0 here. Deliberately skips everything a
    // real floor has that isn't needed yet: no items exist, so no inventory/hotbar/minimap/gold UI;
    // no floor timer/stairs lock either, since this room isn't a real floor.
    public static void BuildTutorial(int seed)
    {
        Random.InitState(seed);
        Physics2D.IgnoreLayerCollision(BlockingLayer, FlyingLayer, true);
        CurrentSeed = seed;
        CurrentFloor = 0;
        clearedRoomsThisFloor.Clear();
        bossDefeatedThisFloor = false;
        // A brand new run's boss-family history starts empty - without this, a leftover
        // usedBossBiomes from a PREVIOUS run in the same process (e.g. died, back to the main
        // menu, New Game again) would wrongly exclude families for this fresh run's floor 1.
        usedBossBiomes.Clear();
        preFloorUsedBossBiomes.Clear();

        Sprite floorSprite = CreateTexturedFloorSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", new Color(0.10f, 0.09f, 0.11f), new Color(0.34f, 0.31f, 0.36f), new Color(0.55f, 0.52f, 0.58f));
        Tile floorTile = CreateTileAsset("Assets/Art/Tiles/FloorTile.asset", floorSprite, Tile.ColliderType.None);
        Tile wallTile = CreateTileAsset("Assets/Art/Tiles/WallTile.asset", wallSprite, Tile.ColliderType.Grid);

        // "PlayerHero" is a real Kenney character tile (see KenneyCharacterSlicer.SlicePlayerSprite
        // + Dungeon/Rebuild Icon Pack Data) - falls back to the old flat circle if that bake step
        // was never run, so a missing/unbaked asset never means an invisible player.
        Sprite playerSprite = LoadIconPackSprite("PlayerHero") ?? CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));
        Sprite zombieSprite = CreateMaskedSprite("Assets/Art/Enemies/Zombie.png", ZombieMask, new Color(0.25f, 0.4f, 0.2f));
        Sprite projectileSprite = CreateCircleSprite("Assets/Art/Projectile.png", new Color(0.6f, 0.85f, 0.95f));
        Sprite fistVisualSprite = CreateCircleSprite("Assets/Art/Fx/FistHit.png", new Color(0.95f, 0.95f, 0.9f));
        Sprite swordVisualSprite = CreateRectSprite("Assets/Art/Fx/SwordSlash.png", new Color(0.85f, 0.9f, 0.95f));
        Sprite explosionSprite = CreateCircleSprite("Assets/Art/Fx/Explosion.png", new Color(0.95f, 0.55f, 0.15f));
        Sprite lightningOrbSprite = CreateCircleSprite("Assets/Art/Fx/LightningOrb.png", new Color(0.55f, 0.85f, 1f));
        Sprite lightningBoltSprite = CreateRectSprite("Assets/Art/Fx/LightningBolt.png", new Color(0.75f, 0.9f, 1f));
        Sprite fireballSprite = CreateCircleSprite("Assets/Art/Fx/Fireball.png", new Color(0.95f, 0.45f, 0.1f));
        Sprite fireLineSprite = CreateCircleSprite("Assets/Art/Fx/FireTrail.png", new Color(0.85f, 0.35f, 0.1f, 0.85f));
        Sprite burnIconSprite = CreateCircleSprite("Assets/Art/Fx/BurnIcon.png", new Color(0.95f, 0.55f, 0.15f));
        Sprite npcSprite = LoadIconPackSprite("NpcGuide") ?? CreateCircleSprite("Assets/Art/Npc.png", new Color(0.35f, 0.55f, 0.75f));
        Sprite doorBarrierSprite = CreateSolidSprite("Assets/Art/Fx/DoorBarrier.png", new Color(0.6f, 0.15f, 0.15f));
        Sprite stairsMarker = LoadIconPackSprite("Exit_Bright");

        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);
        GameObject root = new GameObject("DungeonRoot");

        GameObject gridGO = new GameObject("Grid", typeof(Grid));
        gridGO.transform.SetParent(root.transform);

        GameObject floorGO = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer));
        floorGO.transform.SetParent(gridGO.transform);
        floorGO.GetComponent<TilemapRenderer>().sortingOrder = -1;

        GameObject wallsGO = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(Rigidbody2D));
        wallsGO.layer = BlockingLayer;
        wallsGO.transform.SetParent(gridGO.transform);
        wallsGO.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
        wallsGO.GetComponent<TilemapRenderer>().sortingOrder = 0;
        wallsGO.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

        Tilemap floorMap = floorGO.GetComponent<Tilemap>();
        Tilemap wallsMap = wallsGO.GetComponent<Tilemap>();
        wallsMap.tileAnchor = new Vector3(0.5f, 0f, 0f);

        BuildRoomGeometry(0, 0, floorMap, wallsMap, floorTile, wallTile);

        Vector2 roomOrigin = Vector2.zero;
        Vector2 roomCenter = new Vector2(TutorialRoomWidth / 2f, TutorialRoomHeight / 2f);
        Vector2 startWorld = roomOrigin + new Vector2(3f, TutorialRoomHeight / 2f);

        // --- Player ---
        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(Stamina), typeof(Mana), typeof(PlayerInventory), typeof(PlayerStats), typeof(PlayerSkills), typeof(StatusIconDisplay), typeof(PlayerController));
        player.transform.SetParent(root.transform);
        player.transform.position = startWorld;
        player.tag = "Player";

        player.GetComponent<SpriteRenderer>().sprite = playerSprite;
        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        player.GetComponent<CircleCollider2D>().radius = 0.4f;

        PlayerController playerController = player.GetComponent<PlayerController>();
        playerController.projectileSprite = projectileSprite;
        playerController.fistVisualSprite = fistVisualSprite;
        playerController.swordVisualSprite = swordVisualSprite;
        playerController.explosionSprite = explosionSprite;
        playerController.lightningOrbSprite = lightningOrbSprite;
        playerController.lightningBoltSprite = lightningBoltSprite;
        playerController.fireballSprite = fireballSprite;
        playerController.fireLineSprite = fireLineSprite;
        playerController.burnIconSprite = burnIconSprite;

        Health playerHealth = player.GetComponent<Health>();
        // Rescaled 2026-09-21 alongside the tutorial Zombie preset just below (was 6, from before
        // the main damage-system rebalance - see assets.enemyPresets in BuildFloorAssetsPart1) -
        // this tutorial player has no PlayerLimbs (see its GameObject constructor above), so
        // there's no 205-total-HP pool to match here, just its own small self-contained scale, big
        // enough that a first-time player's very first fight isn't a coin flip.
        playerHealth.maxHealth = 30;
        playerHealth.currentHealth = playerHealth.maxHealth;

        // --- Camera: fixed on the one room, no per-room scrolling needed ---
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = TutorialRoomHeight / 2f;
            cam.transform.position = new Vector3(roomCenter.x, roomCenter.y, cam.transform.position.z);
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
        }

        // --- UI: Canvas + EventSystem + the one HUD piece that matters here (hearts) ---
        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform);
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject eventSystemGO = GameObject.Find("EventSystem");
        if (eventSystemGO == null) eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemGO.transform.SetParent(root.transform);

        Sprite uiFillSprite = CreateSolidSprite("Assets/Art/UI/Fill.png", Color.white);

        GameObject heartBarGO = new GameObject("HeartBar", typeof(RectTransform), typeof(Image), typeof(HeartHUD));
        heartBarGO.transform.SetParent(canvasGO.transform, false);
        Image heartBarBackground = heartBarGO.GetComponent<Image>();
        heartBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform heartBarRect = heartBarBackground.rectTransform;
        heartBarRect.anchorMin = heartBarRect.anchorMax = new Vector2(0f, 1f);
        heartBarRect.pivot = new Vector2(0f, 1f);
        heartBarRect.anchoredPosition = new Vector2(20f, -20f);
        heartBarRect.sizeDelta = new Vector2(160f, 16f);

        GameObject heartFillGO = new GameObject("Fill", typeof(Image));
        heartFillGO.transform.SetParent(heartBarGO.transform, false);
        Image heartFill = heartFillGO.GetComponent<Image>();
        heartFill.sprite = uiFillSprite;
        heartFill.color = new Color(0.85f, 0.15f, 0.2f);
        heartFill.type = Image.Type.Filled;
        heartFill.fillMethod = Image.FillMethod.Horizontal;
        heartFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform heartFillRect = heartFill.rectTransform;
        heartFillRect.anchorMin = Vector2.zero;
        heartFillRect.anchorMax = Vector2.one;
        heartFillRect.offsetMin = new Vector2(1f, 1f);
        heartFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject heartLabelGO = new GameObject("HeartLabel", typeof(Text));
        heartLabelGO.transform.SetParent(canvasGO.transform, false);
        Text heartLabel = heartLabelGO.GetComponent<Text>();
        heartLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        heartLabel.fontSize = 18;
        heartLabel.alignment = TextAnchor.MiddleLeft;
        heartLabel.color = Color.white;
        RectTransform heartLabelRect = heartLabel.rectTransform;
        heartLabelRect.anchorMin = heartLabelRect.anchorMax = new Vector2(0f, 1f);
        heartLabelRect.pivot = new Vector2(0f, 1f);
        heartLabelRect.anchoredPosition = new Vector2(188f, -20f);
        heartLabelRect.sizeDelta = new Vector2(100f, 16f);

        HeartHUD hud = heartBarGO.GetComponent<HeartHUD>();
        hud.target = playerHealth;
        hud.fill = heartFill;
        hud.label = heartLabel;

        // Without this, Escape never calls UIWindowStack.CloseTop() on this floor, so the Guide's
        // panel (TutorialNpc, see SpawnTutorialNpc) could only ever be closed with E.
        Font tutorialUiFont = Font.CreateDynamicFontFromOSFont("Arial", 32);
        MainMenuController tutorialMainMenu = Object.FindFirstObjectByType<MainMenuController>();
        BuildPauseMenu(canvasGO.transform, tutorialUiFont, tutorialMainMenu);

        // --- Death screen (2026-09-16 fix: the tutorial never had one at all - dying here just set
        // isDead and froze the character with nothing on screen, no path back to the main menu short
        // of forcing Play Mode itself to restart, which - with this project's Enter Play Mode
        // Options set to skip domain reload - doesn't even reliably land back at the menu either.
        // Same setup as Build()'s own death screen below, just reusing this floor's own canvas/font.
        GameObject tutorialDeathGO = new GameObject("DeathScreen", typeof(RectTransform), typeof(Image), typeof(DeathScreenUI));
        tutorialDeathGO.transform.SetParent(canvasGO.transform, false);
        Image tutorialDeathBg = tutorialDeathGO.GetComponent<Image>();
        tutorialDeathBg.color = new Color(0.03f, 0.02f, 0.02f, 0.92f);
        RectTransform tutorialDeathRect = tutorialDeathBg.rectTransform;
        tutorialDeathRect.anchorMin = Vector2.zero;
        tutorialDeathRect.anchorMax = Vector2.one;
        tutorialDeathRect.offsetMin = Vector2.zero;
        tutorialDeathRect.offsetMax = Vector2.zero;

        GameObject tutorialDeathTitleGO = new GameObject("Title", typeof(Text));
        tutorialDeathTitleGO.transform.SetParent(tutorialDeathGO.transform, false);
        Text tutorialDeathTitle = tutorialDeathTitleGO.GetComponent<Text>();
        tutorialDeathTitle.text = "VOUS ETES MORT";
        tutorialDeathTitle.font = tutorialUiFont;
        tutorialDeathTitle.fontSize = 64;
        tutorialDeathTitle.fontStyle = FontStyle.Bold;
        tutorialDeathTitle.alignment = TextAnchor.MiddleCenter;
        tutorialDeathTitle.color = new Color(0.8f, 0.15f, 0.15f);
        RectTransform tutorialDeathTitleRect = tutorialDeathTitle.rectTransform;
        tutorialDeathTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
        tutorialDeathTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
        tutorialDeathTitleRect.pivot = new Vector2(0.5f, 0.5f);
        tutorialDeathTitleRect.anchoredPosition = new Vector2(0f, 30f);
        tutorialDeathTitleRect.sizeDelta = new Vector2(1200f, 120f);

        GameObject tutorialDeathPromptGO = new GameObject("Prompt", typeof(Text));
        tutorialDeathPromptGO.transform.SetParent(tutorialDeathGO.transform, false);
        Text tutorialDeathPrompt = tutorialDeathPromptGO.GetComponent<Text>();
        tutorialDeathPrompt.text = "Appuyez sur ESPACE pour retourner au menu";
        tutorialDeathPrompt.font = tutorialUiFont;
        tutorialDeathPrompt.fontSize = 26;
        tutorialDeathPrompt.alignment = TextAnchor.MiddleCenter;
        tutorialDeathPrompt.color = new Color(0.85f, 0.85f, 0.85f);
        RectTransform tutorialDeathPromptRect = tutorialDeathPrompt.rectTransform;
        tutorialDeathPromptRect.anchorMin = new Vector2(0.5f, 0.5f);
        tutorialDeathPromptRect.anchorMax = new Vector2(0.5f, 0.5f);
        tutorialDeathPromptRect.pivot = new Vector2(0.5f, 0.5f);
        tutorialDeathPromptRect.anchoredPosition = new Vector2(0f, -40f);
        tutorialDeathPromptRect.sizeDelta = new Vector2(900f, 60f);

        tutorialDeathGO.SetActive(false);

        DeathScreenUI tutorialDeathScreen = tutorialDeathGO.GetComponent<DeathScreenUI>();
        tutorialDeathScreen.root = tutorialDeathGO;
        tutorialDeathScreen.mainMenu = tutorialMainMenu;
        playerHealth.OnDeath += tutorialDeathScreen.Show;

        // --- Character sheet (full recap of all 8 stats, toggled with C) - available here too so
        // C works consistently from floor 0 onward, same as every other HUD key ---
        GameObject characterSheetGO = new GameObject("CharacterSheetUI", typeof(RectTransform), typeof(CharacterSheetUI));
        characterSheetGO.transform.SetParent(canvasGO.transform, false);
        RectTransform characterSheetRect = characterSheetGO.GetComponent<RectTransform>();
        characterSheetRect.anchorMin = Vector2.zero;
        characterSheetRect.anchorMax = Vector2.one;
        characterSheetRect.offsetMin = Vector2.zero;
        characterSheetRect.offsetMax = Vector2.zero;
        characterSheetGO.GetComponent<CharacterSheetUI>().stats = player.GetComponent<PlayerStats>();

        // --- Spell book (recap of every known spell, toggled with K) - available here too so K
        // works consistently from floor 0 onward, same as C for the character sheet ---
        GameObject spellBookGO = new GameObject("SpellBookUI", typeof(RectTransform), typeof(SpellBookUI));
        spellBookGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBookRect = spellBookGO.GetComponent<RectTransform>();
        spellBookRect.anchorMin = Vector2.zero;
        spellBookRect.anchorMax = Vector2.one;
        spellBookRect.offsetMin = Vector2.zero;
        spellBookRect.offsetMax = Vector2.zero;
        SpellBookUI spellBook = spellBookGO.GetComponent<SpellBookUI>();
        spellBook.controller = playerController;
        spellBook.mana = player.GetComponent<Mana>();
        spellBook.stats = player.GetComponent<PlayerStats>();

        // --- Spell bar (5 slots, click then an arrow key to cast) - no item hotbar in the
        // tutorial, so this sits directly at the bottom instead of stacked above it ---
        GameObject spellBarGO = new GameObject("SpellBarUI", typeof(RectTransform), typeof(SpellBarUI));
        spellBarGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBarRect = spellBarGO.GetComponent<RectTransform>();
        spellBarRect.anchorMin = Vector2.zero;
        spellBarRect.anchorMax = Vector2.one;
        spellBarRect.offsetMin = Vector2.zero;
        spellBarRect.offsetMax = Vector2.zero;
        SpellBarUI spellBar = spellBarGO.GetComponent<SpellBarUI>();
        spellBar.controller = playerController;

        // --- The one Zombie standing between the player and the portal ---
        GameObject roomGO = new GameObject("TutorialRoom", typeof(RoomController));
        roomGO.transform.SetParent(root.transform);

        RoomController.EnemyPresetEntry[] presets =
        {
            // Rescaled 2026-09-21 ("le mob de depart a bien plus de vie mnt" - the real game's own
            // Zombie preset jumped from 4 to 70 HP in the big damage-system rebalance, see
            // BuildFloorAssetsPart1's assets.enemyPresets): 4 HP died in a single fist hit once
            // fistDamage went from 1 to 6, trivializing the player's very first fight. 24 HP takes
            // ~4 fist hits again, matching the original pre-rebalance feel at the new numbers -
            // this tutorial Zombie has no MonsterLeveling scaling applied (fixed preset, not
            // rolled), so this value is the whole story, not just a base to build on.
            new RoomController.EnemyPresetEntry { type = EnemyType.Zombie, sprite = zombieSprite, maxHealth = 24, contactDamage = 6, isFlying = false, xpReward = 2 },
        };
        RoomController.EnemySpawn[] recipe =
        {
            new RoomController.EnemySpawn { localOffset = new Vector2(TutorialRoomWidth - 7f, TutorialRoomHeight / 2f), type = EnemyType.Zombie, modifier = EliteModifier.None },
        };

        RoomController controller = roomGO.GetComponent<RoomController>();
        controller.gridPos = Vector2Int.zero;
        controller.memberCells = new[] { Vector2Int.zero };
        controller.roomOrigin = roomOrigin;
        controller.roomSize = new Vector2(TutorialRoomWidth, TutorialRoomHeight);
        controller.player = player.transform;
        controller.presets = presets;
        controller.recipe = recipe;

        // --- The portal: physically blocked (plain solid barrier, not a DoorBlocker - a bomb
        // shouldn't let anyone skip the one tutorial fight) until the Zombie dies ---
        Vector2 portalPos = roomOrigin + new Vector2(TutorialRoomWidth - 3f, TutorialRoomHeight / 2f);

        GameObject blocker = new GameObject("TutorialPortalBlocker", typeof(SpriteRenderer), typeof(BoxCollider2D));
        blocker.transform.SetParent(roomGO.transform);
        blocker.transform.position = portalPos;
        SpriteRenderer blockerRenderer = blocker.GetComponent<SpriteRenderer>();
        blockerRenderer.sprite = doorBarrierSprite;
        blockerRenderer.sortingOrder = 1;
        blocker.GetComponent<BoxCollider2D>().size = Vector2.one;
        controller.doorBlockers.Add(blocker);

        GameObject portalGO = new GameObject("TutorialPortal", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Staircase));
        portalGO.transform.SetParent(roomGO.transform);
        portalGO.transform.position = portalPos;
        SpriteRenderer portalRenderer = portalGO.GetComponent<SpriteRenderer>();
        portalRenderer.sprite = stairsMarker;
        portalRenderer.sortingOrder = 0;
        CircleCollider2D portalTrigger = portalGO.GetComponent<CircleCollider2D>();
        portalTrigger.isTrigger = true;
        portalTrigger.radius = 0.8f;
        portalGO.GetComponent<Staircase>().lockType = StairsLockType.Open;

        // --- The Guide: appears only once the Zombie is dead ---
        Vector2 npcPos = roomOrigin + new Vector2(TutorialRoomWidth - 7f, TutorialRoomHeight / 2f + 2.5f);
        controller.OnRoomCleared += _ => SpawnTutorialNpc(npcPos, npcSprite, root.transform);
    }

    // Sandbox for trying out new moves/items without a real floor's pressure (2026-09-18 request:
    // "un punching ball, un monstre immobile, avec une grande quantite de vie"). A single hand-
    // built room, same shape as BuildTutorial, but with the FULL player (PlayerEquipment/
    // PlayerLimbs included, unlike the tutorial's trimmed-down one) so gear/limb mechanics test
    // exactly like a real floor, plus a real ItemCatalog (via BuildFloorAssetsPart1/2, the same
    // pipeline Build() uses) so every item's icon/rarity/stats are authentic rather than a
    // hand-rolled subset. No RoomController/door-lock/portal - nothing here needs to be "cleared".
    public static void BuildTrainingRoom(int seed)
    {
        Random.InitState(seed);
        Physics2D.IgnoreLayerCollision(BlockingLayer, FlyingLayer, true);
        CurrentSeed = seed;
        CurrentFloor = 0;
        clearedRoomsThisFloor.Clear();
        bossDefeatedThisFloor = false;
        usedBossBiomes.Clear();
        preFloorUsedBossBiomes.Clear();

        BiomeTheme biomeTheme = BiomeTheme.Get(Biome.Forest);
        FloorAssets assets = BuildFloorAssetsPart1(biomeTheme);
        BuildFloorAssetsPart2(assets, out List<ItemCatalog.Entry> itemEntries);

        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);
        GameObject root = new GameObject("DungeonRoot");

        // Same "build inactive, assign entries, then activate" dance as Build() - see its own
        // comment on ItemCatalog for why (Awake() re-registering into ItemDatabase before entries
        // is assigned would wipe it).
        GameObject itemCatalogGO = new GameObject("ItemCatalog");
        itemCatalogGO.SetActive(false);
        itemCatalogGO.transform.SetParent(root.transform);
        ItemCatalog itemCatalog = itemCatalogGO.AddComponent<ItemCatalog>();
        itemCatalog.entries = itemEntries;
        itemCatalogGO.SetActive(true);

        GameObject gridGO = new GameObject("Grid", typeof(Grid));
        gridGO.transform.SetParent(root.transform);

        GameObject floorGO = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer));
        floorGO.transform.SetParent(gridGO.transform);
        floorGO.GetComponent<TilemapRenderer>().sortingOrder = -1;

        GameObject wallsGO = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(Rigidbody2D));
        wallsGO.layer = BlockingLayer;
        wallsGO.transform.SetParent(gridGO.transform);
        wallsGO.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
        wallsGO.GetComponent<TilemapRenderer>().sortingOrder = 0;
        wallsGO.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

        Tilemap floorMap = floorGO.GetComponent<Tilemap>();
        Tilemap wallsMap = wallsGO.GetComponent<Tilemap>();
        wallsMap.tileAnchor = new Vector3(0.5f, 0f, 0f);

        BuildRoomGeometry(0, 0, floorMap, wallsMap, assets.floorTile, assets.wallTile);

        Vector2 roomOrigin = Vector2.zero;
        Vector2 roomCenter = new Vector2(RoomWidth / 2f, RoomHeight / 2f);
        Vector2 startWorld = roomOrigin + new Vector2(3f, RoomHeight / 2f);

        // --- Player: full component set (PlayerEquipment/PlayerLimbs included) - see Build()'s
        // own player constructor, this is the exact same shape. ---
        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(Stamina), typeof(Mana), typeof(PlayerInventory), typeof(PlayerEquipment), typeof(PlayerLimbs), typeof(PlayerStats), typeof(PlayerSkills), typeof(StatusIconDisplay), typeof(PlayerController));
        player.transform.SetParent(root.transform);
        player.transform.position = startWorld;
        player.tag = "Player";

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        playerRenderer.sprite = assets.playerSprite;
        playerRenderer.sortingOrder = 0;

        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        player.GetComponent<CircleCollider2D>().radius = 0.4f;

        PlayerController playerController = player.GetComponent<PlayerController>();
        playerController.projectileSprite = assets.projectileSprite;
        playerController.fistVisualSprite = assets.fistVisualSprite;
        playerController.swordVisualSprite = assets.swordVisualSprite;
        playerController.explosionSprite = assets.explosionSprite;
        playerController.lightningOrbSprite = assets.lightningOrbSprite;
        playerController.lightningBoltSprite = assets.lightningBoltSprite;
        playerController.fireballSprite = assets.fireballSprite;
        playerController.fireLineSprite = assets.fireLineSprite;
        playerController.burnIconSprite = assets.burnIconSprite;
        playerController.movementDebuffIcon = LoadIconPackSprite("Padlock01_Bright");
        playerController.speedBuffIcon = LoadIconPackSprite("Thunder_Bright");
        playerController.adrenalineBuffIcon = LoadIconPackSprite("Potion02_Bright");

        PlayerInventory playerInventory = player.GetComponent<PlayerInventory>();
        PlayerEquipment playerEquipment = player.GetComponent<PlayerEquipment>();
        PlayerStats playerStats = player.GetComponent<PlayerStats>();
        PlayerSkills playerSkills = player.GetComponent<PlayerSkills>();
        PlayerLimbs playerLimbs = player.GetComponent<PlayerLimbs>();
        playerEquipment.stats = playerStats;
        playerEquipment.fireIcon = CreateCircleSprite("Assets/Art/Fx/Fire.png", new Color(0.95f, 0.35f, 0.1f));
        // No explicit maxHealth/currentHealth here either - see Build()'s own comment, PlayerStats.
        // Awake() already sets both correctly via PlayerLimbs.
        Health playerHealth = player.GetComponent<Health>();
        Stamina playerStamina = player.GetComponent<Stamina>();
        Mana playerMana = player.GetComponent<Mana>();

        // Bumped from the real game's 20 (2026-09-21 request, item spawner) - the loadout below
        // already fills all 20 default slots on its own (several stacks split across 2 slots by
        // their own maxStack), which left ItemSpawnerUI's "click to receive" silently discarding
        // everything (see PlayerInventory.Add's own "lost" comment) the moment the room loaded.
        // GrowSlotCount, not a plain field assignment - Awake() already ran synchronously when
        // typeof(PlayerInventory) was added above and built `slots` off the default 20.
        playerInventory.GrowSlotCount(40);

        // Starting loadout: a Sword already equipped (melee combos testable immediately) plus a
        // stack of everything else worth trying - thrown items on the hotbar, gear/rings left in
        // the inventory to equip by hand.
        playerEquipment.Set(EquipmentSlotType.Weapon, 0, ItemIds.Sword);
        playerInventory.Add(ItemIds.Staff, 1);
        playerInventory.Add(ItemIds.CursedSword, 1);
        playerInventory.Add(ItemIds.Bomb, 20);
        playerInventory.Add(ItemIds.Shuriken, 20);
        playerInventory.Add(ItemIds.Caillou, 20);
        playerInventory.Add(ItemIds.Baton, 20);
        playerInventory.Add(ItemIds.HealthPotion, 10);
        playerInventory.Add(ItemIds.IronHelmet, 1);
        playerInventory.Add(ItemIds.LeatherPauldrons, 1);
        playerInventory.Add(ItemIds.CombatGloves, 1);
        playerInventory.Add(ItemIds.WalkingBoots, 1);
        playerInventory.Add(ItemIds.SimpleNecklace, 1);
        playerInventory.Add(ItemIds.LeatherBelt, 1);
        playerInventory.Add(ItemIds.LeatherKneepads, 1);
        playerInventory.Add(ItemIds.SimpleRing, 1);
        playerInventory.hotbarSlots[0] = ItemIds.Bomb;
        playerInventory.hotbarSlots[1] = ItemIds.Shuriken;
        playerInventory.hotbarSlots[2] = ItemIds.Caillou;
        playerInventory.hotbarSlots[3] = ItemIds.Baton;
        playerInventory.hotbarSlots[4] = ItemIds.HealthPotion;

        // --- Camera: fixed on the one room ---
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = RoomHeight / 2f;
            cam.transform.position = new Vector3(roomCenter.x, roomCenter.y, cam.transform.position.z);
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
        }

        // --- UI: Canvas + EventSystem ---
        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform);
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject tooltipGO = new GameObject("TooltipUI", typeof(TooltipUI));
        tooltipGO.transform.SetParent(canvasGO.transform, false);

        GameObject eventSystemGO = GameObject.Find("EventSystem");
        if (eventSystemGO == null) eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemGO.transform.SetParent(root.transform);

        Sprite uiFillSprite = CreateSolidSprite("Assets/Art/UI/Fill.png", Color.white);

        // --- Health bar ---
        GameObject heartBarGO = new GameObject("HeartBar", typeof(RectTransform), typeof(Image), typeof(HeartHUD));
        heartBarGO.transform.SetParent(canvasGO.transform, false);
        Image heartBarBackground = heartBarGO.GetComponent<Image>();
        heartBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform heartBarRect = heartBarBackground.rectTransform;
        heartBarRect.anchorMin = heartBarRect.anchorMax = new Vector2(0f, 1f);
        heartBarRect.pivot = new Vector2(0f, 1f);
        heartBarRect.anchoredPosition = new Vector2(20f, -20f);
        heartBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject heartFillGO = new GameObject("Fill", typeof(Image));
        heartFillGO.transform.SetParent(heartBarGO.transform, false);
        Image heartFill = heartFillGO.GetComponent<Image>();
        heartFill.sprite = uiFillSprite;
        heartFill.color = new Color(0.85f, 0.15f, 0.2f);
        heartFill.type = Image.Type.Filled;
        heartFill.fillMethod = Image.FillMethod.Horizontal;
        heartFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform heartFillRect = heartFill.rectTransform;
        heartFillRect.anchorMin = Vector2.zero;
        heartFillRect.anchorMax = Vector2.one;
        heartFillRect.offsetMin = new Vector2(1f, 1f);
        heartFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject heartLabelGO = new GameObject("HeartLabel", typeof(Text));
        heartLabelGO.transform.SetParent(canvasGO.transform, false);
        Text heartLabel = heartLabelGO.GetComponent<Text>();
        heartLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        heartLabel.fontSize = 18;
        heartLabel.alignment = TextAnchor.MiddleLeft;
        heartLabel.color = Color.white;
        RectTransform heartLabelRect = heartLabel.rectTransform;
        heartLabelRect.anchorMin = heartLabelRect.anchorMax = new Vector2(0f, 1f);
        heartLabelRect.pivot = new Vector2(0f, 1f);
        heartLabelRect.anchoredPosition = new Vector2(188f, -20f);
        heartLabelRect.sizeDelta = new Vector2(100f, 24f);

        HeartHUD hud = heartBarGO.GetComponent<HeartHUD>();
        hud.target = playerHealth;
        hud.fill = heartFill;
        hud.label = heartLabel;

        // --- Stamina bar ---
        GameObject staminaBarGO = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(StaminaBarUI));
        staminaBarGO.transform.SetParent(canvasGO.transform, false);
        Image staminaBarBackground = staminaBarGO.GetComponent<Image>();
        staminaBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform staminaBarRect = staminaBarBackground.rectTransform;
        staminaBarRect.anchorMin = staminaBarRect.anchorMax = new Vector2(0f, 1f);
        staminaBarRect.pivot = new Vector2(0f, 1f);
        staminaBarRect.anchoredPosition = new Vector2(20f, -52f);
        staminaBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject staminaFillGO = new GameObject("Fill", typeof(Image));
        staminaFillGO.transform.SetParent(staminaBarGO.transform, false);
        Image staminaFill = staminaFillGO.GetComponent<Image>();
        staminaFill.sprite = uiFillSprite;
        staminaFill.color = new Color(0.75f, 0.7f, 0.15f);
        staminaFill.type = Image.Type.Filled;
        staminaFill.fillMethod = Image.FillMethod.Horizontal;
        staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform staminaFillRect = staminaFill.rectTransform;
        staminaFillRect.anchorMin = Vector2.zero;
        staminaFillRect.anchorMax = Vector2.one;
        staminaFillRect.offsetMin = new Vector2(1f, 1f);
        staminaFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject staminaLabelGO = new GameObject("StaminaLabel", typeof(Text));
        staminaLabelGO.transform.SetParent(canvasGO.transform, false);
        Text staminaLabel = staminaLabelGO.GetComponent<Text>();
        staminaLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        staminaLabel.fontSize = 18;
        staminaLabel.alignment = TextAnchor.MiddleLeft;
        staminaLabel.color = Color.white;
        RectTransform staminaLabelRect = staminaLabel.rectTransform;
        staminaLabelRect.anchorMin = staminaLabelRect.anchorMax = new Vector2(0f, 1f);
        staminaLabelRect.pivot = new Vector2(0f, 1f);
        staminaLabelRect.anchoredPosition = new Vector2(188f, -52f);
        staminaLabelRect.sizeDelta = new Vector2(80f, 24f);

        StaminaBarUI staminaBar = staminaBarGO.GetComponent<StaminaBarUI>();
        staminaBar.target = playerStamina;
        staminaBar.fill = staminaFill;
        staminaBar.label = staminaLabel;

        // --- Mana bar ---
        GameObject manaBarGO = new GameObject("ManaBar", typeof(RectTransform), typeof(Image), typeof(ManaBarUI));
        manaBarGO.transform.SetParent(canvasGO.transform, false);
        Image manaBarBackground = manaBarGO.GetComponent<Image>();
        manaBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform manaBarRect = manaBarBackground.rectTransform;
        manaBarRect.anchorMin = manaBarRect.anchorMax = new Vector2(0f, 1f);
        manaBarRect.pivot = new Vector2(0f, 1f);
        manaBarRect.anchoredPosition = new Vector2(20f, -84f);
        manaBarRect.sizeDelta = new Vector2(160f, 24f);

        GameObject manaFillGO = new GameObject("Fill", typeof(Image));
        manaFillGO.transform.SetParent(manaBarGO.transform, false);
        Image manaFill = manaFillGO.GetComponent<Image>();
        manaFill.sprite = uiFillSprite;
        manaFill.color = new Color(0.3f, 0.5f, 0.9f);
        manaFill.type = Image.Type.Filled;
        manaFill.fillMethod = Image.FillMethod.Horizontal;
        manaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform manaFillRect = manaFill.rectTransform;
        manaFillRect.anchorMin = Vector2.zero;
        manaFillRect.anchorMax = Vector2.one;
        manaFillRect.offsetMin = new Vector2(1f, 1f);
        manaFillRect.offsetMax = new Vector2(-1f, -1f);

        GameObject manaLabelGO = new GameObject("ManaLabel", typeof(Text));
        manaLabelGO.transform.SetParent(canvasGO.transform, false);
        Text manaLabel = manaLabelGO.GetComponent<Text>();
        manaLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        manaLabel.fontSize = 18;
        manaLabel.alignment = TextAnchor.MiddleLeft;
        manaLabel.color = Color.white;
        RectTransform manaLabelRect = manaLabel.rectTransform;
        manaLabelRect.anchorMin = manaLabelRect.anchorMax = new Vector2(0f, 1f);
        manaLabelRect.pivot = new Vector2(0f, 1f);
        manaLabelRect.anchoredPosition = new Vector2(188f, -84f);
        manaLabelRect.sizeDelta = new Vector2(80f, 24f);

        ManaBarUI manaBar = manaBarGO.GetComponent<ManaBarUI>();
        manaBar.target = playerMana;
        manaBar.fill = manaFill;
        manaBar.label = manaLabel;

        // --- Title label (top-center) ---
        Font uiFont = Font.CreateDynamicFontFromOSFont("Arial", 32);
        GameObject titleLabelGO = new GameObject("TrainingRoomLabel", typeof(Text));
        titleLabelGO.transform.SetParent(canvasGO.transform, false);
        Text titleLabel = titleLabelGO.GetComponent<Text>();
        titleLabel.text = "Salle d'entrainement - testez vos coups, sorts et objets sur le punching ball";
        titleLabel.font = uiFont;
        titleLabel.fontSize = 22;
        titleLabel.alignment = TextAnchor.MiddleCenter;
        titleLabel.color = new Color(0.85f, 0.85f, 0.9f);
        RectTransform titleLabelRect = titleLabel.rectTransform;
        titleLabelRect.anchorMin = titleLabelRect.anchorMax = new Vector2(0.5f, 1f);
        titleLabelRect.pivot = new Vector2(0.5f, 1f);
        titleLabelRect.anchoredPosition = new Vector2(0f, -20f);
        titleLabelRect.sizeDelta = new Vector2(900f, 30f);

        // --- Pause menu + death screen (same shape as BuildTutorial's) ---
        MainMenuController trainingMainMenu = Object.FindFirstObjectByType<MainMenuController>();
        BuildPauseMenu(canvasGO.transform, uiFont, trainingMainMenu);

        GameObject deathGO = new GameObject("DeathScreen", typeof(RectTransform), typeof(Image), typeof(DeathScreenUI));
        deathGO.transform.SetParent(canvasGO.transform, false);
        Image deathBg = deathGO.GetComponent<Image>();
        deathBg.color = new Color(0.03f, 0.02f, 0.02f, 0.92f);
        RectTransform deathRect = deathBg.rectTransform;
        deathRect.anchorMin = Vector2.zero;
        deathRect.anchorMax = Vector2.one;
        deathRect.offsetMin = Vector2.zero;
        deathRect.offsetMax = Vector2.zero;

        GameObject deathTitleGO = new GameObject("Title", typeof(Text));
        deathTitleGO.transform.SetParent(deathGO.transform, false);
        Text deathTitle = deathTitleGO.GetComponent<Text>();
        deathTitle.text = "VOUS ETES MORT";
        deathTitle.font = uiFont;
        deathTitle.fontSize = 64;
        deathTitle.fontStyle = FontStyle.Bold;
        deathTitle.alignment = TextAnchor.MiddleCenter;
        deathTitle.color = new Color(0.8f, 0.15f, 0.15f);
        RectTransform deathTitleRect = deathTitle.rectTransform;
        deathTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
        deathTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
        deathTitleRect.pivot = new Vector2(0.5f, 0.5f);
        deathTitleRect.anchoredPosition = new Vector2(0f, 30f);
        deathTitleRect.sizeDelta = new Vector2(1200f, 120f);

        GameObject deathPromptGO = new GameObject("Prompt", typeof(Text));
        deathPromptGO.transform.SetParent(deathGO.transform, false);
        Text deathPrompt = deathPromptGO.GetComponent<Text>();
        deathPrompt.text = "Appuyez sur ESPACE pour retourner au menu";
        deathPrompt.font = uiFont;
        deathPrompt.fontSize = 26;
        deathPrompt.alignment = TextAnchor.MiddleCenter;
        deathPrompt.color = new Color(0.85f, 0.85f, 0.85f);
        RectTransform deathPromptRect = deathPrompt.rectTransform;
        deathPromptRect.anchorMin = new Vector2(0.5f, 0.5f);
        deathPromptRect.anchorMax = new Vector2(0.5f, 0.5f);
        deathPromptRect.pivot = new Vector2(0.5f, 0.5f);
        deathPromptRect.anchoredPosition = new Vector2(0f, -40f);
        deathPromptRect.sizeDelta = new Vector2(900f, 60f);

        deathGO.SetActive(false);

        DeathScreenUI deathScreen = deathGO.GetComponent<DeathScreenUI>();
        deathScreen.root = deathGO;
        deathScreen.mainMenu = trainingMainMenu;
        playerHealth.OnDeath += deathScreen.Show;

        // --- Character sheet / spell book / spell bar / hotbar / inventory - the full set, so
        // every new move/item is reachable exactly like on a real floor ---
        GameObject characterSheetGO = new GameObject("CharacterSheetUI", typeof(RectTransform), typeof(CharacterSheetUI));
        characterSheetGO.transform.SetParent(canvasGO.transform, false);
        RectTransform characterSheetRect = characterSheetGO.GetComponent<RectTransform>();
        characterSheetRect.anchorMin = Vector2.zero;
        characterSheetRect.anchorMax = Vector2.one;
        characterSheetRect.offsetMin = Vector2.zero;
        characterSheetRect.offsetMax = Vector2.zero;
        CharacterSheetUI characterSheet = characterSheetGO.GetComponent<CharacterSheetUI>();
        characterSheet.stats = playerStats;
        characterSheet.skills = playerSkills;

        GameObject spellBookGO = new GameObject("SpellBookUI", typeof(RectTransform), typeof(SpellBookUI));
        spellBookGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBookRect = spellBookGO.GetComponent<RectTransform>();
        spellBookRect.anchorMin = Vector2.zero;
        spellBookRect.anchorMax = Vector2.one;
        spellBookRect.offsetMin = Vector2.zero;
        spellBookRect.offsetMax = Vector2.zero;
        SpellBookUI spellBook = spellBookGO.GetComponent<SpellBookUI>();
        spellBook.controller = playerController;
        spellBook.mana = playerMana;
        spellBook.stats = playerStats;

        GameObject hotbarGO = new GameObject("HotbarUI", typeof(RectTransform), typeof(HotbarUI));
        hotbarGO.transform.SetParent(canvasGO.transform, false);
        RectTransform hotbarRect = hotbarGO.GetComponent<RectTransform>();
        hotbarRect.anchorMin = Vector2.zero;
        hotbarRect.anchorMax = Vector2.one;
        hotbarRect.offsetMin = Vector2.zero;
        hotbarRect.offsetMax = Vector2.zero;
        HotbarUI hotbar = hotbarGO.GetComponent<HotbarUI>();
        hotbar.inventory = playerInventory;
        hotbar.slotSize = 56f;
        hotbar.spacing = 64f;
        hotbar.fontSize = 36;

        GameObject spellBarGO = new GameObject("SpellBarUI", typeof(RectTransform), typeof(SpellBarUI));
        spellBarGO.transform.SetParent(canvasGO.transform, false);
        RectTransform spellBarRect = spellBarGO.GetComponent<RectTransform>();
        spellBarRect.anchorMin = Vector2.zero;
        spellBarRect.anchorMax = Vector2.one;
        spellBarRect.offsetMin = Vector2.zero;
        spellBarRect.offsetMax = Vector2.zero;
        SpellBarUI spellBar = spellBarGO.GetComponent<SpellBarUI>();
        spellBar.controller = playerController;
        spellBar.slotSize = 56f;
        spellBar.spacing = 64f;
        spellBar.rowYOffset = 84f;

        GameObject inventoryGO = new GameObject("InventoryUI", typeof(RectTransform), typeof(InventoryUI));
        inventoryGO.transform.SetParent(canvasGO.transform, false);
        RectTransform inventoryRect = inventoryGO.GetComponent<RectTransform>();
        inventoryRect.anchorMin = Vector2.zero;
        inventoryRect.anchorMax = Vector2.one;
        inventoryRect.offsetMin = Vector2.zero;
        inventoryRect.offsetMax = Vector2.zero;
        InventoryUI inventoryUI = inventoryGO.GetComponent<InventoryUI>();
        inventoryUI.inventory = playerInventory;
        inventoryUI.equipment = playerEquipment;
        inventoryUI.player = playerController;
        inventoryUI.limbs = playerLimbs;
        inventoryUI.slotSize = 72f;
        inventoryUI.spacing = 84f;
        inventoryUI.fontSize = 32;

        // Same raycast-order fix as Build() - see its own comment on this exact line.
        hotbarGO.transform.SetAsLastSibling();

        // --- The punching ball: stationary (never given a target, so EnemyController.FixedUpdate
        // never moves it), absurd HP so it never actually dies mid-session, no contact damage (it
        // doesn't hit back), no XP (it's not a real kill) ---
        Sprite dummySprite = CreateCircleSprite("Assets/Art/Enemies/PunchingBall.png", new Color(0.55f, 0.35f, 0.18f));
        GameObject dummyGO = new GameObject("PunchingBall", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(StatusIconDisplay), typeof(EnemyController));
        dummyGO.transform.SetParent(root.transform);
        dummyGO.transform.position = roomOrigin + new Vector2(RoomWidth * 0.7f, RoomHeight / 2f);

        SpriteRenderer dummyRenderer = dummyGO.GetComponent<SpriteRenderer>();
        dummyRenderer.sprite = dummySprite;
        dummyRenderer.sortingOrder = 0;
        dummyGO.AddComponent<SpriteOutline>();

        Rigidbody2D dummyBody = dummyGO.GetComponent<Rigidbody2D>();
        dummyBody.gravityScale = 0f;
        dummyBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        dummyGO.GetComponent<CircleCollider2D>().radius = 0.5f;

        Health dummyHealth = dummyGO.GetComponent<Health>();
        dummyHealth.maxHealth = 999999;
        dummyHealth.currentHealth = dummyHealth.maxHealth;

        EnemyController dummyController = dummyGO.GetComponent<EnemyController>();
        dummyController.enemyType = EnemyType.Zombie; // only affects which BodyPart a hit rolls onto
        dummyController.moveSpeed = 0f;
        dummyController.contactDamage = 0;
        dummyController.xpReward = 0;

        // Floating red damage numbers (2026-09-19 request) - training-room-only, see
        // DamageNumberDisplay's own comment for why this never gets wired onto a real enemy.
        DamageNumberDisplay dummyDamageNumbers = dummyGO.AddComponent<DamageNumberDisplay>();
        dummyDamageNumbers.target = dummyHealth;

        // --- Mob/item spawner tools (2026-09-21 request) - pick any monster or any item from a
        // list and get it instantly, rather than waiting on real floor RNG/room recipes. Wired up
        // only here, never on a real floor - see TrainingSpawnerUI.cs. ---
        GameObject mobSpawnerGO = new GameObject("MobSpawnerUI", typeof(MobSpawnerUI));
        mobSpawnerGO.transform.SetParent(canvasGO.transform, false);
        MobSpawnerUI mobSpawner = mobSpawnerGO.GetComponent<MobSpawnerUI>();
        mobSpawner.player = player.transform;
        mobSpawner.presets = assets.enemyPresets;
        mobSpawner.spawnParent = root.transform;

        GameObject itemSpawnerGO = new GameObject("ItemSpawnerUI", typeof(ItemSpawnerUI));
        itemSpawnerGO.transform.SetParent(canvasGO.transform, false);
        ItemSpawnerUI itemSpawner = itemSpawnerGO.GetComponent<ItemSpawnerUI>();
        itemSpawner.inventory = playerInventory;
    }

    // Builds one enemy exactly like RoomController.SpawnEnemies would, standalone - no
    // RoomController/recipe/room-bounds needed since the training room is a single fixed space
    // (see MobSpawnerUI). Targets the player immediately so a spawned mob starts acting right away,
    // same as a real room once ArmEnemies fires.
    public static void SpawnTrainingMob(EnemyType type, Vector2 position, Transform target, RoomController.EnemyPresetEntry[] presets, Transform parent)
    {
        RoomController.EnemyPresetEntry preset = presets[0];
        foreach (RoomController.EnemyPresetEntry candidate in presets)
        {
            if (candidate.type == type) { preset = candidate; break; }
        }

        int floor = Mathf.Max(1, CurrentFloor);
        int level = MonsterLeveling.RollLevel(floor);

        GameObject enemy = new GameObject(type + " Niv." + level,
            typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(EnemyLimbs), typeof(StatusIconDisplay), typeof(EnemyController));
        if (preset.isFlying) enemy.layer = FlyingLayer;
        enemy.transform.SetParent(parent);
        enemy.transform.position = position;

        SpriteRenderer renderer = enemy.GetComponent<SpriteRenderer>();
        renderer.sprite = preset.sprite;
        renderer.sortingOrder = 0;
        enemy.AddComponent<SpriteOutline>();

        Rigidbody2D body = enemy.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        enemy.GetComponent<CircleCollider2D>().radius = 0.4f;

        float scaledMoveSpeed = 0f;
        int scaledContactDamage = preset.contactDamage;
        MonsterLeveling.ApplyLevelStats(type, level, floor, ref scaledMoveSpeed, ref scaledContactDamage);

        Health health = enemy.GetComponent<Health>();
        health.maxHealth = preset.maxHealth + MonsterLeveling.ConstitutionBonusForFloor(floor);
        health.currentHealth = health.maxHealth;

        EnemyController controller = enemy.GetComponent<EnemyController>();
        controller.enemyType = type;
        controller.level = level;
        controller.moveSpeed = scaledMoveSpeed;
        controller.contactDamage = scaledContactDamage;
        controller.isFlying = preset.isFlying;
        controller.projectileSprite = preset.projectileSprite;
        controller.xpReward = 0; // pas un vrai kill de floor, comme le punching ball

        enemy.GetComponent<EnemyLimbs>().Configure(EnemyLimbLayout.For(type), health.maxHealth);
        controller.SetTarget(target);
    }

    // PauseMenuUI lives on its OWN always-active GameObject, separate from the visual panel it
    // toggles: a MonoBehaviour's Update() never runs while its own GameObject is inactive, so
    // putting the Escape-listening component directly on the panel it hides by default meant
    // Escape could never be detected in the first place - the pause menu was unreachable no
    // matter what. Shared by Build() and BuildTutorial() - the tutorial floor used to have no
    // PauseMenuUI at all, which meant UIWindowStack.CloseTop() was never called there, so nothing
    // (e.g. the Guide's TutorialNpc panel) could ever be closed with Escape on that floor.
    static PauseMenuUI BuildPauseMenu(Transform canvasParent, Font uiFont, MainMenuController mainMenu)
    {
        GameObject pauseControllerGO = new GameObject("PauseMenuController", typeof(PauseMenuUI));
        pauseControllerGO.transform.SetParent(canvasParent, false);
        PauseMenuUI pauseMenu = pauseControllerGO.GetComponent<PauseMenuUI>();

        GameObject pauseGO = new GameObject("PauseMenu", typeof(RectTransform), typeof(Image));
        pauseGO.transform.SetParent(canvasParent, false);
        Image pauseBg = pauseGO.GetComponent<Image>();
        pauseBg.color = new Color(0.05f, 0.05f, 0.06f, 0.9f);
        // Same fix as MainMenuController's own "Panel" background - see its comment (2026-09-21
        // bug report). This one shares the exact same shape (a full-screen decorative backdrop
        // behind its own SettingsPanel), so it's exposed to the identical raycast-priority issue.
        pauseBg.raycastTarget = false;
        RectTransform pauseRect = pauseBg.rectTransform;
        pauseRect.anchorMin = Vector2.zero;
        pauseRect.anchorMax = Vector2.one;
        pauseRect.offsetMin = Vector2.zero;
        pauseRect.offsetMax = Vector2.zero;

        GameObject pauseTitleGO = new GameObject("Title", typeof(Text));
        pauseTitleGO.transform.SetParent(pauseGO.transform, false);
        Text pauseTitle = pauseTitleGO.GetComponent<Text>();
        pauseTitle.text = "Pause";
        pauseTitle.font = uiFont;
        pauseTitle.fontSize = 48;
        pauseTitle.fontStyle = FontStyle.Bold;
        pauseTitle.alignment = TextAnchor.MiddleCenter;
        pauseTitle.color = Color.white;
        RectTransform pauseTitleRect = pauseTitle.rectTransform;
        pauseTitleRect.anchorMin = pauseTitleRect.anchorMax = new Vector2(0.5f, 1f);
        pauseTitleRect.pivot = new Vector2(0.5f, 1f);
        pauseTitleRect.anchoredPosition = new Vector2(0f, -100f);
        pauseTitleRect.sizeDelta = new Vector2(800f, 100f);

        MainMenuController.CreateButton(pauseGO.transform, "Reprendre", uiFont, -260f, pauseMenu.Resume);
        MainMenuController.CreateButton(pauseGO.transform, "Parametres", uiFont, -340f, pauseMenu.ToggleSettings);
        MainMenuController.CreateButton(pauseGO.transform, "Quitter au menu principal", uiFont, -420f, pauseMenu.QuitToMenu);
        GameObject pauseSettingsPanel = MainMenuController.BuildSettingsPanel(pauseGO.transform, uiFont);

        pauseGO.SetActive(false);

        pauseMenu.root = pauseGO;
        pauseMenu.settingsPanel = pauseSettingsPanel;
        pauseMenu.mainMenu = mainMenu;
        return pauseMenu;
    }

    static void SpawnTutorialNpc(Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("TutorialGuide", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(AudioSource), typeof(TutorialVoice), typeof(TutorialNpc));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0.75f, 0.88f, 1f); // cool, reassuring - a guide, not a threat
        renderer.sortingOrder = 0;

        go.GetComponent<CircleCollider2D>().radius = 1.2f;

        TutorialNpc npc = go.GetComponent<TutorialNpc>();
        npc.npcName = "Le Guide";
        npc.bodyText =
            "Bienvenue dans la Fosse, crawler.\n\n" +
            "Chaque etage est chronometre : 10 minutes pour trouver l'escalier et descendre, " +
            "sans quoi le sol s'effondre et vous tue. Certains escaliers se trouvent juste en " +
            "explorant, d'autres exigent de battre un boss, d'actionner un levier ou d'attendre.\n\n" +
            "Combattez, pillez, equipez-vous, et descendez aussi loin que possible. " +
            "Le portail derriere moi vous mene au premier etage.";
        // Reads the paragraph above aloud (2026-09-21 request: "pour la toute premiere game, lors
        // du tuto... un speech explicatif du donjon par l'ia") - see Tools/generate_tutorial_voice.ps1.
        npc.voice = go.GetComponent<TutorialVoice>();
    }

    // Rolls this floor's lock flavour and builds the physical staircase - the sole way down.
    // BossKill/Lever/Timed each gate the same blocker; Open has none (still has to be found, since
    // Stairs is hidden on the minimap until visited just like Secret - see MinimapController).
    static void SetupStaircase(Vector2 center, Vector2Int gridPos, Dictionary<Vector2Int, RoomType> layout,
        Sprite stairsSprite, Sprite cageSprite, Sprite leverSprite, FloorTimer floorTimer,
        List<BossRoomController> bossRoomControllers, Transform parent)
    {
        GameObject go = new GameObject("Staircase", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Staircase));
        go.transform.SetParent(parent);
        go.transform.position = center;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = stairsSprite;
        renderer.sortingOrder = 0;

        CircleCollider2D trigger = go.GetComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.8f;

        // Deliberately NOT a DoorBlocker (see ExplosionUtility) - a bomb must never be able to pop
        // a locked staircase open early, unlike an ordinary locked room door. Uses CageMask (see
        // its declaration) instead of the plain solid doorBarrierSprite reused everywhere else -
        // its transparent gaps let the staircase icon underneath stay visible while locked, instead
        // of fully hiding it behind a plain colored square.
        GameObject blocker = new GameObject("StaircaseBlocker", typeof(SpriteRenderer), typeof(BoxCollider2D));
        blocker.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        blocker.transform.SetParent(go.transform, false);
        SpriteRenderer blockerRenderer = blocker.GetComponent<SpriteRenderer>();
        blockerRenderer.sprite = cageSprite;
        blockerRenderer.sortingOrder = 1;
        blocker.GetComponent<BoxCollider2D>().size = Vector2.one * 1.4f;

        Staircase staircase = go.GetComponent<Staircase>();
        staircase.blocker = blocker;

        // Floor 1 always gets the same themed puzzle instead of a random lock - "sans meme avoir
        // besoin de taper un boss" (2026-09-15 request, Dungeon Crawler Carl-style per-floor
        // identity): 4 levers scattered across the floor, ALL of them required (see
        // Staircase.leversRequired/NotifyLeverPulled), never Open/Timed/BossKill. The floor's 3
        // boss rooms still exist for loot/XP, just never gate progress here. Every other floor
        // keeps today's uniform random pick, unchanged.
        StairsLockType lockType;
        int leverCount = 1;
        if (CurrentFloor == 1)
        {
            lockType = StairsLockType.Lever;
            leverCount = FirstFloorLeverCount;
        }
        else
        {
            lockType = (StairsLockType)Random.Range(0, 4);
            // Boss is unconditionally placed every floor, but fall back safely rather than risk a
            // permanent soft-lock if that ever stops being true.
            if (lockType == StairsLockType.BossKill && bossRoomControllers.Count == 0) lockType = StairsLockType.Open;
        }
        staircase.lockType = lockType;

        switch (lockType)
        {
            case StairsLockType.Timed:
                staircase.floorTimer = floorTimer;
                staircase.unlockAtElapsedSeconds = floorTimer.duration * TimedStairsUnlockFraction;
                break;
            case StairsLockType.BossKill:
                // Relaxed back to "any ONE of the 3 bosses" (was "all 3 dead") per explicit
                // request in project_xp_monster_leveling_backlog: the point of a BossKill lock is
                // to gate progress on beating A boss, not the whole floor - requiring all 3 fought
                // against the stated goal of letting players skip a full clear to go faster.
                foreach (BossRoomController bossRoom in bossRoomControllers)
                {
                    bossRoom.OnBossDefeated += staircase.Unlock;
                }
                break;
            case StairsLockType.Lever:
                staircase.leversRequired = leverCount;
                List<Vector2Int> excludedLeverRooms = new List<Vector2Int> { gridPos };
                for (int i = 0; i < leverCount; i++)
                {
                    Vector2Int leverRoom = FindLeverRoom(layout, excludedLeverRooms);
                    excludedLeverRooms.Add(leverRoom);
                    Vector2 leverCenter = new Vector2(leverRoom.x * StepX + RoomWidth / 2f, leverRoom.y * StepY + RoomHeight / 2f);
                    SpawnLever(leverCenter, leverSprite, staircase, parent);
                }
                break;
        }
    }

    const int FirstFloorLeverCount = 4;

    // Picks a plain room elsewhere on the floor to host a lever - prefers an Empty room (no
    // monsters guarding it) and only falls back to a Monster room if the floor has none.
    // excludeGridPositions always contains at least the staircase's own room, plus (for a
    // multi-lever floor) every room a previous lever already claimed, so no two levers - or a
    // lever and the staircase - ever land in the same room.
    static Vector2Int FindLeverRoom(Dictionary<Vector2Int, RoomType> layout, List<Vector2Int> excludeGridPositions)
    {
        var emptyCandidates = new List<Vector2Int>();
        var monsterCandidates = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            if (excludeGridPositions.Contains(kv.Key)) continue;
            if (kv.Value == RoomType.Empty) emptyCandidates.Add(kv.Key);
            else if (kv.Value == RoomType.Monster) monsterCandidates.Add(kv.Key);
        }
        List<Vector2Int> candidates = emptyCandidates.Count > 0 ? emptyCandidates : monsterCandidates;
        // Last-resort fallback (an extremely cramped floor with nowhere left) - reuse the
        // staircase's own room rather than crash on an empty candidate list.
        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : excludeGridPositions[0];
    }

    static void SpawnLever(Vector2 position, Sprite sprite, Staircase target, Transform parent)
    {
        GameObject go = new GameObject("Lever", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Lever));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;
        go.AddComponent<SpriteOutline>(); // same visibility treatment as mobs, see RoomController.SpawnEnemies

        go.GetComponent<Lever>().target = target;
    }

    // Chance a still-plain Monster cell tries to absorb neighboring free cells into one bigger
    // room instead of staying single-cell - purely to let some encounters use a bigger arena for
    // more spectacular formations (see MergeMultiCellRooms).
    // Higher than it looks like it should be: a cramped, tree-shaped layout rejects most
    // candidate placements outright (IsMergeCandidateValid), so this is the roll to ATTEMPT a
    // merge, not the odds of actually getting one - empirically only ~1 in 4 attempts succeeds.
    const float MultiCellRoomChance = 0.65f;
    // Biggest tier first - a cramped, tree-shaped layout rarely has room for an 8-cell block, so a
    // roll that aims big cascades down through progressively smaller shapes (never below 2 cells)
    // instead of giving up outright, so most attempts land SOME merge rather than none at all.
    static readonly Vector2Int[][] MultiCellShapeTiers =
    {
        new[] { new Vector2Int(2, 4), new Vector2Int(4, 2) }, // 8 cells
        new[] { new Vector2Int(2, 3), new Vector2Int(3, 2) }, // 6 cells
        new[] { new Vector2Int(2, 2) }, // 4 cells
        new[] { new Vector2Int(1, 2), new Vector2Int(2, 1) }, // 2 cells
    };

    // A Souls-like boss arena should almost always read as bigger than an ordinary room - much
    // higher chance than Monster's opportunistic roll. Capped at 4 cells rather than reusing the
    // 6/8-cell tiers: a single boss (no formation to spread out, unlike Monster rooms) risks
    // reading as empty in an arena that big - revisit the cap if that turns out wrong in play.
    // 2-cell fallback for a cramped layout.
    const float BossArenaMergeChance = 0.85f;
    static readonly Vector2Int[][] BossArenaShapeTiers =
    {
        new[] { new Vector2Int(2, 2) }, // 4 cells
        new[] { new Vector2Int(1, 2), new Vector2Int(2, 1) }, // 2 cells
    };

    static Dictionary<Vector2Int, RoomType> GenerateLayout(out Dictionary<Vector2Int, RectInt> cellGroups, out Dictionary<Vector2Int, BossTier> bossTiers, out Vector2Int restaurantCell, out Vector2Int arcadeCell, out SafeRoomVariant arcadeVariant)
    {
        var rooms = new Dictionary<Vector2Int, RoomType>();
        Vector2Int start = Vector2Int.zero;
        rooms[start] = RoomType.Start;

        var frontier = new List<Vector2Int> { start };
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        int guard = 0;
        while (rooms.Count < TargetNormalRooms && guard < 2000)
        {
            guard++;
            Vector2Int current = frontier[Random.Range(0, frontier.Count)];
            Vector2Int dir = dirs[Random.Range(0, dirs.Length)];
            Vector2Int next = current + dir;
            if (rooms.ContainsKey(next)) continue;

            int neighborCount = 0;
            foreach (Vector2Int d in dirs) if (rooms.ContainsKey(next + d)) neighborCount++;
            if (neighborCount > 1) continue; // keep the layout tree-like, no merged loops

            rooms[next] = RoomType.Monster; // placeholder, reclassified below
            frontier.Add(next);
        }

        foreach (Vector2Int cell in new List<Vector2Int>(rooms.Keys))
        {
            if (rooms[cell] == RoomType.Start) continue;
            rooms[cell] = Random.value < 0.25f ? RoomType.Empty : RoomType.Monster;
        }

        // Boss is always the farthest room from Start (in door-hops, not Euclidean distance), and
        // Secret is always the farthest room from Boss - which naturally lands it on the opposite
        // side of the layout without needing a coordinate mirror that might not exist as a room.
        // Both are restricted to ordinary Monster/Empty cells, and computed before Treasure/Shop/
        // Event/Gamble are placed below: those are new dead-end branches that can attach onto any
        // existing cell (including a freshly-picked Boss/Secret), and doing this search first, on
        // the base tree, is what guarantees both are still genuine dead ends afterward - it doesn't
        // cost accuracy, since bonus rooms are excluded from candidacy either way so they never
        // affect which Monster/Empty cell is actually farthest.
        RoomType[] normalTypes = { RoomType.Monster, RoomType.Empty };
        Vector2Int bossCell = FindFarthestRoom(rooms, start, normalTypes);
        rooms[bossCell] = RoomType.Boss;
        int bossDistance = ComputeDistances(rooms, start)[bossCell];

        Vector2Int secretCell = FindFarthestRoom(rooms, bossCell, normalTypes);
        rooms[secretCell] = RoomType.Secret;

        // Three bosses per floor now (explicit request), each a different power tier of the same
        // biome-themed family (see BossFamilyFor/BossTierStatsFor) - the single farthest cell above
        // is always the hardest (Region), matching "extremely hard, furthest away". The other two
        // are ordinary dead-end placements like every other bonus room, so they land somewhere
        // reachable well before the player is strong enough for the real fight at the end.
        bossTiers = new Dictionary<Vector2Int, BossTier> { [bossCell] = BossTier.Region };
        // Ville/Zone each get their own biome/family, distinct from Region's and from each other
        // (see PickFloorBiome/bossTierBiomes, populated for Region back in Build) - a floor's 3
        // boss encounters are 3 different species, never the same one 3 times over.
        if (PlaceSpecialRoom(rooms, RoomType.Boss, secretCell, start, bossDistance, out Vector2Int villeBossCell))
        {
            bossTiers[villeBossCell] = BossTier.Ville;
            bossTierBiomes[BossTier.Ville] = PickFloorBiome(bossTierBiomes.Values);
        }
        if (PlaceSpecialRoom(rooms, RoomType.Boss, secretCell, start, bossDistance, out Vector2Int zoneBossCell))
        {
            bossTiers[zoneBossCell] = BossTier.Zone;
            bossTierBiomes[BossTier.Zone] = PickFloorBiome(bossTierBiomes.Values);
        }

        // Stairs goes FIRST and is the only one of these with a hard fallback: PlaceSpecialRoom's
        // three distance thresholds all draw from the same shrinking pool of "dead end" cells, and
        // previously Stairs was placed LAST - after Treasure/Shop/Event/Gamble/Safe had already
        // picked over every candidate, it could come up with nothing at all and the floor would
        // generate with no way down. With the floor timer, that isn't a missed convenience like a
        // absent Shop/Safe - it's an unwinnable floor that can only end in a forced death. Going
        // first gives it the best odds of the three PlaceSpecialRoom thresholds succeeding on their
        // own; ForcePlaceStairs is the guarantee if even that fails.
        if (!PlaceSpecialRoom(rooms, RoomType.Stairs, secretCell, start, bossDistance))
        {
            ForcePlaceStairs(rooms, secretCell);
        }

        // Safe rooms go right after Stairs now (used to go dead last, after Treasure/Shop/Event/
        // Gamble had already picked over the dead-end pool) - reported 2026-09-16: even with the
        // SafeRoomMinGridDistance hard filter below, 3 Safe rooms placed last were too often left
        // choosing between whatever cramped leftover dead ends remained, none of them actually 5+
        // cells apart. Going second gives them first pick of the map's best-spread dead ends, while
        // the pool is still large - restaurantCell/arcadeCell record which cell is which so the
        // population pass in Build() knows which of the three to furnish differently (the first
        // stays the Tavern - see SpawnTavernNpc/SpawnCookNpc/SpawnArcadeNpc). If a placement fails
        // (rare, no fallback - same as Treasure/Shop/Event/Gamble), PlaceSpecialRoom sets its cell to
        // default (0,0) - harmless, since every Safe-room check below also requires
        // kv.Value == RoomType.Safe and Start already owns (0,0).
        PlaceSpecialRoom(rooms, RoomType.Safe, secretCell, start, bossDistance, SafeRoomMinGridDistance);
        PlaceSpecialRoom(rooms, RoomType.Safe, secretCell, start, bossDistance, out restaurantCell, SafeRoomMinGridDistance);
        PlaceSpecialRoom(rooms, RoomType.Safe, secretCell, start, bossDistance, out arcadeCell, SafeRoomMinGridDistance);

        // Never anchored on the Secret room (so its one connection - the only one its bombable wall
        // assumes - is never touched again), and never allowed to reach as far from Start as the
        // Boss room (so it stays unambiguously the single farthest room on the floor).
        PlaceSpecialRoom(rooms, RoomType.Treasure, secretCell, start, bossDistance);
        PlaceSpecialRoom(rooms, RoomType.Shop, secretCell, start, bossDistance);
        if (Random.value < 0.5f) PlaceSpecialRoom(rooms, RoomType.Event, secretCell, start, bossDistance); // 1-in-2 chance per floor
        // Also kept clear of the Safe trio (reported 2026-09-16: a Gamble room landing glued to a
        // Tavern/Restaurant/Arcade "town" cluster) - all 3 Safe rooms already exist by this point
        // (placed right after Stairs, above), so this is one-directional: Gamble moves away from
        // them, same SafeRoomMinGridDistance threshold as Safe-vs-Safe.
        PlaceSpecialRoom(rooms, RoomType.Gamble, secretCell, start, bossDistance, SafeRoomMinGridDistance, avoidTypes: new[] { RoomType.Gamble, RoomType.Safe });
        // Which content that third Safe room actually gets (2026-09-16: Arcade is no longer the
        // only option - see SafeRoomVariant/SpawnFlowerGardenContent/SpawnMaterialStorageContent).
        arcadeVariant = (SafeRoomVariant)Random.Range(0, 3);

        // Every cell defaults to its own 1x1 group; the merge passes below (run last, once every
        // other room type is already placed) may absorb some cells' free neighbors into a bigger
        // shared group. Boss goes first (and is weighted much higher) so a Souls-like arena gets
        // first pick of whatever free cells surround it, before Monster's opportunistic merge -
        // MergeMultiCellRooms already iterates every cell of the given type, so it needs no change
        // now that there are 3 Boss cells instead of 1 - each independently rolls its own chance.
        cellGroups = new Dictionary<Vector2Int, RectInt>();
        foreach (Vector2Int cell in rooms.Keys) cellGroups[cell] = new RectInt(cell.x, cell.y, 1, 1);
        MergeMultiCellRooms(rooms, cellGroups, RoomType.Boss, BossArenaMergeChance, BossArenaShapeTiers);
        MergeMultiCellRooms(rooms, cellGroups, RoomType.Monster, MultiCellRoomChance, MultiCellShapeTiers);

        return rooms;
    }

    // Opportunistically grows every still-single-cell room of `targetType` into a bigger
    // rectangular room by claiming currently-free neighboring cells - generic over which type is
    // growing (Monster's opportunistic spectacle, Boss's near-guaranteed Souls-like arena) so both
    // reuse the same placement/validity logic instead of two near-duplicate implementations.
    // IsMergeCandidateValid already refuses any cell bordering an unrelated existing room from
    // outside the new rect, so this can never silently graft a surprise connection onto Secret (or
    // anything else) regardless of which type is doing the merging.
    static void MergeMultiCellRooms(Dictionary<Vector2Int, RoomType> rooms, Dictionary<Vector2Int, RectInt> cellGroups,
        RoomType targetType, float chance, Vector2Int[][] shapeTiers)
    {
        var anchors = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in rooms) if (kv.Value == targetType) anchors.Add(kv.Key);
        Shuffle(anchors);

        foreach (Vector2Int anchor in anchors)
        {
            if (cellGroups[anchor].width * cellGroups[anchor].height > 1) continue; // already absorbed
            if (Random.value > chance) continue;

            int startTier = Random.Range(0, shapeTiers.Length);
            for (int tier = startTier; tier < shapeTiers.Length; tier++)
            {
                if (TryPlaceMergedRoom(rooms, cellGroups, anchor, shapeTiers[tier])) break;
                // This tier's shapes don't fit around this anchor at all - fall back to a smaller
                // one rather than leaving the anchor single-cell just because the biggest roll missed.
            }
        }
    }

    // Tries every shape in the tier (random order), each as all 4 rectangle corners (random
    // order) so a cell whose only free neighbors are, say, up-and-left can still find a valid
    // orientation. Claims the first orientation whose extra cells are all free and touch nothing
    // outside the new rectangle. Absorbed cells take the anchor's own type (Monster stays Monster,
    // Boss stays Boss) - see the population loop's IsGroupAnchor guard, which is what keeps a
    // multi-cell Boss room from spawning a second boss out of its extra cells.
    static bool TryPlaceMergedRoom(Dictionary<Vector2Int, RoomType> rooms, Dictionary<Vector2Int, RectInt> cellGroups, Vector2Int anchor, Vector2Int[] shapes)
    {
        RoomType anchorType = rooms[anchor];
        var shapeOrder = new List<Vector2Int>(shapes);
        Shuffle(shapeOrder);

        foreach (Vector2Int shape in shapeOrder)
        {
            int w = shape.x, h = shape.y;
            var corners = new List<int> { 0, 1, 2, 3 };
            Shuffle(corners);

            foreach (int corner in corners)
            {
                int originX = corner == 1 || corner == 3 ? anchor.x - w + 1 : anchor.x;
                int originY = corner == 2 || corner == 3 ? anchor.y - h + 1 : anchor.y;
                var rect = new RectInt(originX, originY, w, h);

                if (!IsMergeCandidateValid(rooms, rect, anchor)) continue;

                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        Vector2Int cell = new Vector2Int(originX + x, originY + y);
                        rooms[cell] = anchorType;
                        cellGroups[cell] = rect;
                    }
                }
                return true;
            }
        }
        return false;
    }

    // The anchor's own pre-existing neighbors are fine (that's how the merged room connects to
    // the rest of the dungeon) - every OTHER cell in the candidate rect must be currently free and
    // touch nothing already in `rooms` outside the rect, or claiming it would silently graft a
    // surprise door onto some unrelated existing room.
    static bool IsMergeCandidateValid(Dictionary<Vector2Int, RoomType> rooms, RectInt rect, Vector2Int anchor)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = 0; x < rect.width; x++)
        {
            for (int y = 0; y < rect.height; y++)
            {
                Vector2Int cell = new Vector2Int(rect.xMin + x, rect.yMin + y);
                if (cell == anchor) continue;
                if (rooms.ContainsKey(cell)) return false;

                foreach (Vector2Int d in dirs)
                {
                    Vector2Int neighbor = cell + d;
                    if (!rooms.ContainsKey(neighbor)) continue;
                    bool neighborInRect = neighbor.x >= rect.xMin && neighbor.x < rect.xMax && neighbor.y >= rect.yMin && neighbor.y < rect.yMax;
                    if (!neighborInRect) return false;
                }
            }
        }
        return true;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static bool IsGroupAnchor(Vector2Int cell, Dictionary<Vector2Int, RectInt> cellGroups) => cellGroups[cell].xMin == cell.x && cellGroups[cell].yMin == cell.y;

    // World-space rect covering an entire merged group (same size formula as GatherGroup's
    // groupSize) - reduces to the plain single-cell rect when the group is unmerged (1x1).
    static Rect GroupWorldRect(RectInt group)
    {
        return new Rect(
            group.xMin * StepX,
            group.yMin * StepY,
            group.width * RoomWidth + (group.width - 1) * Gap,
            group.height * RoomHeight + (group.height - 1) * Gap);
    }

    // Shared by the Monster and Boss population branches: collects every cell in the anchor's
    // merged group, every door on any of those cells, and the group's total world-space size.
    static void GatherGroup(Vector2Int anchor, Dictionary<Vector2Int, RectInt> cellGroups,
        Dictionary<Vector2Int, List<(Vector2 pos, bool onVerticalWall)>> doorsByRoom,
        out List<Vector2Int> memberCells, out List<(Vector2 pos, bool onVerticalWall)> doors, out Vector2 groupSize)
    {
        RectInt group = cellGroups[anchor];
        memberCells = new List<Vector2Int>();
        doors = new List<(Vector2 pos, bool onVerticalWall)>();
        for (int gx = 0; gx < group.width; gx++)
        {
            for (int gy = 0; gy < group.height; gy++)
            {
                Vector2Int member = new Vector2Int(group.xMin + gx, group.yMin + gy);
                memberCells.Add(member);
                if (doorsByRoom.TryGetValue(member, out var memberDoors)) doors.AddRange(memberDoors);
            }
        }
        groupSize = new Vector2(
            group.width * RoomWidth + (group.width - 1) * Gap,
            group.height * RoomHeight + (group.height - 1) * Gap);
    }

    static bool SameGroup(Dictionary<Vector2Int, RectInt> cellGroups, Vector2Int a, Vector2Int b)
    {
        RectInt ra = cellGroups[a], rb = cellGroups[b];
        return ra.xMin == rb.xMin && ra.yMin == rb.yMin;
    }

    // Returns the farthest cell whose type is one of `candidateTypes`, preferring a dead end (only
    // 1 neighbor) - since Boss/Secret are chosen before Treasure/Shop/Event/Gamble are placed (see
    // GenerateLayout), requiring a dead end here is what keeps Secret's single connection (which
    // its bombable wall assumes) from growing a second one once those bonus rooms are added -
    // PlaceSpecialRoom is also told never to attach one onto Secret specifically (protectedAnchor)
    // or to reach as far from Start as Boss (maxDistanceFromStart), so neither is disturbed
    // afterward. Falls back to any matching cell (dead end or not) in the rare case no dead end of
    // the right type exists. Ties broken at random.
    static Vector2Int FindFarthestRoom(Dictionary<Vector2Int, RoomType> rooms, Vector2Int from, RoomType[] candidateTypes)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        Dictionary<Vector2Int, int> dist = ComputeDistances(rooms, from);

        return PickFarthestMatch(rooms, dist, dirs, candidateTypes, requireDeadEnd: true)
            ?? PickFarthestMatch(rooms, dist, dirs, candidateTypes, requireDeadEnd: false).Value;
    }

    // BFS distance in room-hops from `from`, over the graph of cells already in `rooms` (two cells
    // are adjacent if both exist - the same rule that decides where a door gets carved later).
    static Dictionary<Vector2Int, int> ComputeDistances(Dictionary<Vector2Int, RoomType> rooms, Vector2Int from)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var dist = new Dictionary<Vector2Int, int> { [from] = 0 };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int d in dirs)
            {
                Vector2Int next = current + d;
                if (!rooms.ContainsKey(next) || dist.ContainsKey(next)) continue;
                dist[next] = dist[current] + 1;
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    static Vector2Int? PickFarthestMatch(Dictionary<Vector2Int, RoomType> rooms, Dictionary<Vector2Int, int> dist, Vector2Int[] dirs, RoomType[] candidateTypes, bool requireDeadEnd)
    {
        int best = -1;
        var farthest = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, int> kv in dist)
        {
            if (System.Array.IndexOf(candidateTypes, rooms[kv.Key]) < 0) continue;
            if (requireDeadEnd)
            {
                int neighborCount = 0;
                foreach (Vector2Int d in dirs) if (rooms.ContainsKey(kv.Key + d)) neighborCount++;
                if (neighborCount != 1) continue;
            }
            if (kv.Value > best) { best = kv.Value; farthest.Clear(); farthest.Add(kv.Key); }
            else if (kv.Value == best) farthest.Add(kv.Key);
        }
        return farthest.Count > 0 ? farthest[Random.Range(0, farthest.Count)] : (Vector2Int?)null;
    }

    // `protectedAnchor` (if given) is never used as the attachment point for the new room - keeps
    // the Secret room's single connection from growing a second one. `maxDistanceFromStart` rejects
    // any anchor whose new leaf would reach that far or farther, so a bonus room can never tie or
    // exceed the Boss room's distance from Start (it wouldn't visually read as "the farthest room"
    // anymore if a Shop/Treasure/etc. coincidentally matched or beat it) - but in a small/cramped
    // dungeon that constraint alone can starve every candidate (observed: Event's spawn rate
    // dropping from ~50% to ~10%). Falls back in two steps rather than dropping the constraint
    // outright: first allow tying Boss's distance (still never exceeding it - the one outcome this
    // whole scheme exists to prevent), and only as a last resort (a genuinely tiny dungeon) place it
    // anywhere valid at all, since the room existing beats it not existing.
    // Returns whether a spot was actually found - callers for whom the room is truly mandatory
    // (currently only Stairs) need to know so they can fall back to ForcePlaceStairs instead of
    // silently generating a floor without one.
    static bool PlaceSpecialRoom(Dictionary<Vector2Int, RoomType> rooms, RoomType type, Vector2Int? protectedAnchor, Vector2Int start, int maxDistanceFromStart, int minGridDistanceFromSameType = 0, RoomType[] avoidTypes = null)
        => PlaceSpecialRoom(rooms, type, protectedAnchor, start, maxDistanceFromStart, out _, minGridDistanceFromSameType, avoidTypes);

    // Out-param overload - lets a caller that places several rooms of the SAME type (the two extra
    // Boss encounters, see bossTiers below) know exactly which cell each individual call landed on,
    // instead of having to guess from a shared RoomType afterward.
    static bool PlaceSpecialRoom(Dictionary<Vector2Int, RoomType> rooms, RoomType type, Vector2Int? protectedAnchor, Vector2Int start, int maxDistanceFromStart, out Vector2Int chosenCell, int minGridDistanceFromSameType = 0, RoomType[] avoidTypes = null)
    {
        Dictionary<Vector2Int, int> dist = ComputeDistances(rooms, start);
        Vector2Int? chosen = FindPlacementCandidate(rooms, type, protectedAnchor, dist, maxDistanceFromStart, minGridDistanceFromSameType, avoidTypes)
            ?? FindPlacementCandidate(rooms, type, protectedAnchor, dist, maxDistanceFromStart + 1, minGridDistanceFromSameType, avoidTypes)
            ?? FindPlacementCandidate(rooms, type, protectedAnchor, dist, int.MaxValue, minGridDistanceFromSameType, avoidTypes);
        if (!chosen.HasValue) { chosenCell = default; return false; }
        rooms[chosen.Value] = type;
        chosenCell = chosen.Value;
        return true;
    }

    // Last-resort guarantee when even PlaceSpecialRoom's most permissive threshold found no free
    // "dead end" cell to attach a new branch to (an extremely cramped/degenerate layout) - converts
    // an existing Monster or Empty room into Stairs outright instead of leaving the floor with no
    // way down. At this point in GenerateLayout only Start/Boss/Secret exist besides Monster/Empty
    // cells, and the BFS growth loop guarantees at least a few of those, so this always finds one.
    static void ForcePlaceStairs(Dictionary<Vector2Int, RoomType> rooms, Vector2Int? protectedAnchor)
    {
        var candidates = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in rooms)
        {
            if (kv.Key == protectedAnchor) continue;
            if (kv.Value == RoomType.Monster || kv.Value == RoomType.Empty) candidates.Add(kv.Key);
        }
        if (candidates.Count == 0) return; // structurally shouldn't happen - see comment above
        rooms[candidates[Random.Range(0, candidates.Count)]] = RoomType.Stairs;
    }

    static Vector2Int? FindPlacementCandidate(Dictionary<Vector2Int, RoomType> rooms, RoomType type, Vector2Int? protectedAnchor, Dictionary<Vector2Int, int> dist, int maxDistanceFromStart, int minGridDistanceFromSameType = 0, RoomType[] avoidTypes = null)
    {
        // Defaults to "just this type" (the original behavior) - a caller placing a type that
        // shouldn't cluster with OTHER special rooms either (see Gamble below, reported 2026-09-16:
        // a Gamble room landing glued to the Safe-room trio) passes a wider set instead.
        avoidTypes ??= new[] { type };

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var candidates = new List<Vector2Int>();
        var anchors = new List<Vector2Int>(); // parallel to candidates - the existing cell each one would branch off of

        foreach (Vector2Int cell in rooms.Keys)
        {
            if (cell == protectedAnchor) continue;
            if (dist.TryGetValue(cell, out int cellDist) && cellDist + 1 >= maxDistanceFromStart) continue;
            // Never branch a new room directly off an EXISTING room of the same type (or, if
            // avoidTypes was widened, any of those types) - a valid dead end can only ever touch
            // exactly ONE existing room (see neighborCount below), so if that one room already
            // matches, this candidate would glue the two together. The "farthest" scoring further
            // down only DEPRIORITIZES that (it still wins if it's the only option left in a cramped
            // layout) - this is what actually forbids it, closing the gap that let it happen anyway
            // (reported 2026-09-16: an Arcade/Restaurant Safe-room variant placed directly against
            // another Safe room - both RoomType.Safe, so the softer distance heuristic already in
            // place, see below, wasn't enough on its own once a floor started placing 3 Safe rooms
            // instead of 2).
            if (rooms.TryGetValue(cell, out RoomType existingType) && System.Array.IndexOf(avoidTypes, existingType) >= 0) continue;
            foreach (Vector2Int d in dirs)
            {
                Vector2Int next = cell + d;
                if (rooms.ContainsKey(next)) continue;

                int neighborCount = 0;
                foreach (Vector2Int d2 in dirs) if (rooms.ContainsKey(next + d2)) neighborCount++;
                if (neighborCount == 1) { candidates.Add(next); anchors.Add(cell); }
            }
        }

        if (candidates.Count == 0) return null;

        // A type placed more than once per floor (currently Safe x3, see GenerateLayout) picks the
        // dead end FARTHEST from every already-placed room of that same type, instead of a purely
        // random one among all valid dead ends - on top of the hard same-type-anchor exclusion just
        // above, this also keeps a same-type room from landing right next to a DIFFERENT existing
        // room's dead end that just happens to be close by. A type placed only once (Treasure/Shop/
        // Event/Gamble/the farthest Boss) never has an existing same-type cell yet, so sameTypeCells
        // is empty and this is a no-op for them.
        List<Vector2Int> sameTypeCells = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in rooms) if (System.Array.IndexOf(avoidTypes, kv.Value) >= 0) sameTypeCells.Add(kv.Key);
        if (sameTypeCells.Count == 0) return candidates[Random.Range(0, candidates.Count)];

        // Spatial spacing, in GRID cells rather than corridor hops (see SafeRoomMinGridDistance) - a
        // winding tree can put a candidate many corridor-hops from every existing same-type room
        // while still landing only 1-2 cells away from one on the actual map, which is what "collée"
        // reports were actually about. Picks whichever candidate maximizes its OWN minimum grid
        // distance to every existing same-type cell - when the map has room to satisfy
        // minGridDistanceFromSameType this always finds it (that's the global max), and when it
        // doesn't (a cramped layout with no candidate far enough away) it still returns the closest
        // achievable spacing instead of falling back to the unrelated graph-hop metric below, which
        // routinely picked something corridor-far but grid-adjacent.
        if (minGridDistanceFromSameType > 0)
        {
            int bestGridDist = -1;
            var spaced = new List<Vector2Int>();
            foreach (Vector2Int c in candidates)
            {
                int minDist = int.MaxValue;
                foreach (Vector2Int s in sameTypeCells)
                {
                    int d = Mathf.Abs(c.x - s.x) + Mathf.Abs(c.y - s.y);
                    if (d < minDist) minDist = d;
                }
                if (minDist > bestGridDist) { bestGridDist = minDist; spaced.Clear(); spaced.Add(c); }
                else if (minDist == bestGridDist) spaced.Add(c);
            }
            return spaced[Random.Range(0, spaced.Count)];
        }

        Dictionary<Vector2Int, int> distToSameType = ComputeDistancesMultiSource(rooms, sameTypeCells);
        int best = -1;
        var farthest = new List<Vector2Int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            int candidateDist = (distToSameType.TryGetValue(anchors[i], out int anchorDist) ? anchorDist : 0) + 1;
            if (candidateDist > best) { best = candidateDist; farthest.Clear(); farthest.Add(candidates[i]); }
            else if (candidateDist == best) farthest.Add(candidates[i]);
        }
        return farthest[Random.Range(0, farthest.Count)];
    }

    // Multi-source BFS room-hop distance, same graph rule as ComputeDistances (two cells adjacent
    // iff both already exist in `rooms`) - used to find, for every existing room, its distance to
    // the NEAREST of several source cells at once (see FindPlacementCandidate's same-type spacing).
    static Dictionary<Vector2Int, int> ComputeDistancesMultiSource(Dictionary<Vector2Int, RoomType> rooms, List<Vector2Int> sources)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var dist = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        foreach (Vector2Int source in sources)
        {
            if (dist.ContainsKey(source)) continue;
            dist[source] = 0;
            queue.Enqueue(source);
        }

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int d in dirs)
            {
                Vector2Int next = current + d;
                if (!rooms.ContainsKey(next) || dist.ContainsKey(next)) continue;
                dist[next] = dist[current] + 1;
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    static void BuildRoomGeometry(int originX, int originY, Tilemap floorMap, Tilemap wallsMap, Tile floorTile, Tile wallTile)
    {
        for (int x = 0; x < RoomWidth; x++)
        {
            for (int y = 0; y < RoomHeight; y++)
            {
                Vector3Int pos = new Vector3Int(originX + x, originY + y, 0);
                bool isWall = x == 0 || y == 0 || x == RoomWidth - 1 || y == RoomHeight - 1;
                wallsMap.SetTile(pos, isWall ? wallTile : null);
                floorMap.SetTile(pos, isWall ? null : floorTile);
            }
        }
    }

    // Local (0-based) offset for a door along a wall of the given length, kept away from corners.
    static int RandomDoorOffset(int wallLength)
    {
        int maxOffset = wallLength - DoorMargin - DoorWidth;
        return Random.Range(DoorMargin, maxOffset + 1);
    }

    // Dead center of the wall, used for a secret room's wall (so there's exactly one spot to bomb
    // along its length) and for Boss/Safe/Shop/Stairs (so their entrance reads as a deliberate
    // gate rather than a random doorway - see IsGatedRoomType).
    static int CenteredDoorOffset(int wallLength)
    {
        return (wallLength - DoorWidth) / 2;
    }

    static bool IsGatedRoomType(RoomType type) =>
        type == RoomType.Boss || type == RoomType.Safe || type == RoomType.Shop || type == RoomType.Stairs;

    // Null for a type that shouldn't announce itself (Start/Monster/Empty - the common case, no
    // need to call out every ordinary room the player walks into).
    static string RoomAnnouncementText(RoomType type) => type switch
    {
        RoomType.Boss => "Vous entrez dans une salle de boss",
        RoomType.Safe => "Vous entrez dans une salle securisee",
        RoomType.Shop => "Vous entrez dans une boutique",
        RoomType.Treasure => "Vous entrez dans une salle au tresor",
        RoomType.Event => "Vous entrez dans une salle d'evenement",
        RoomType.Gamble => "Vous entrez dans une salle de pari",
        RoomType.Secret => "Vous avez decouvert une salle secrete !",
        RoomType.Stairs => "Vous entrez dans la salle de l'escalier",
        _ => null,
    };

    // Opens each room's own threshold on a shared vertical boundary. No corridor connects them -
    // DoorTrigger teleports the player straight across instead.
    static void CarveHorizontalDoor(int leftOriginX, int rightOriginX, int leftDoorY, int rightDoorY, Tilemap floorMap, Tilemap wallsMap, Tile floorTile)
    {
        OpenBorder(leftOriginX + RoomWidth - 1, leftDoorY, true, floorMap, wallsMap, floorTile);
        OpenBorder(rightOriginX, rightDoorY, true, floorMap, wallsMap, floorTile);
    }

    // Opens each room's own threshold on a shared horizontal boundary (bottom room's top edge,
    // top room's bottom edge) - same teleport-only connection as the horizontal case.
    static void CarveVerticalDoor(int bottomOriginY, int topOriginY, int bottomDoorX, int topDoorX, Tilemap floorMap, Tilemap wallsMap, Tile floorTile)
    {
        OpenBorder(bottomOriginY + RoomHeight - 1, bottomDoorX, false, floorMap, wallsMap, floorTile);
        OpenBorder(topOriginY, topDoorX, false, floorMap, wallsMap, floorTile);
    }

    // Opens a DoorWidth-wide gap in a room's own border wall at the given fixed coordinate.
    static void OpenBorder(int fixedCoord, int doorStart, bool fixedIsX, Tilemap floorMap, Tilemap wallsMap, Tile floorTile)
    {
        for (int d = 0; d < DoorWidth; d++)
        {
            Vector3Int pos = fixedIsX ? new Vector3Int(fixedCoord, doorStart + d, 0) : new Vector3Int(doorStart + d, fixedCoord, 0);
            wallsMap.SetTile(pos, null);
            floorMap.SetTile(pos, floorTile);
        }
    }

    // Wires both directions of a door-to-door teleport: fully crossing either threshold lands
    // the player just inside the other room, past its own threshold. roomA/roomB (nullable) are
    // the RoomControllers of the rooms on each side - each trigger checks its OWN room's lock
    // state (the room it physically sits in), since entering a locked room is always allowed;
    // only leaving one before it's cleared is blocked.
    // Looks up which room controller (Monster or Boss - either can lock its doors) owns a grid
    // cell, if any, and returns its exitTriggers list for SpawnDoorTrigger to register into.
    static List<DoorTrigger> GetExitTriggerList(Vector2Int cell, Dictionary<Vector2Int, RoomController> monsterControllers, Dictionary<Vector2Int, BossRoomController> bossControllers)
    {
        if (monsterControllers.TryGetValue(cell, out RoomController rc)) return rc.exitTriggers;
        if (bossControllers.TryGetValue(cell, out BossRoomController bc)) return bc.exitTriggers;
        return null;
    }

    // Same lookup as GetExitTriggerList, for the room's doorBlockers instead - lets CreateDoorLink
    // pair each freshly-created DoorTrigger with the DoorBlocker sitting at the same doorway (see
    // DoorBlocker's own comment for why that pairing matters).
    static List<GameObject> GetDoorBlockerList(Vector2Int cell, Dictionary<Vector2Int, RoomController> monsterControllers, Dictionary<Vector2Int, BossRoomController> bossControllers)
    {
        if (monsterControllers.TryGetValue(cell, out RoomController rc)) return rc.doorBlockers;
        if (bossControllers.TryGetValue(cell, out BossRoomController bc)) return bc.doorBlockers;
        return null;
    }

    // Matches by position (both a room's DoorBlocker and its own-side DoorTrigger are spawned at
    // the exact same door.pos, just at different points in Build()) rather than by list index,
    // since the two lists are populated by separate passes with no guaranteed matching order.
    static void LinkBlockerToTrigger(List<GameObject> blockers, Vector2 pos, DoorTrigger trigger)
    {
        if (blockers == null) return;
        foreach (GameObject blocker in blockers)
        {
            if (blocker == null) continue;
            if (Vector2.Distance(blocker.transform.position, pos) < 0.05f)
            {
                blocker.GetComponent<DoorBlocker>().linkedTriggers.Add(trigger);
                return;
            }
        }
    }

    static void CreateDoorLink(Vector2 posA, Vector2 inwardA, List<DoorTrigger> exitTriggersA, List<GameObject> blockersA,
        Vector2 posB, Vector2 inwardB, List<DoorTrigger> exitTriggersB, List<GameObject> blockersB, Transform parent)
    {
        const float landingDepth = 1.5f;
        DoorTrigger triggerA = SpawnDoorTrigger(posA, inwardA, posB + inwardB * landingDepth, exitTriggersA, parent);
        DoorTrigger triggerB = SpawnDoorTrigger(posB, inwardB, posA + inwardA * landingDepth, exitTriggersB, parent);
        LinkBlockerToTrigger(blockersA, posA, triggerA);
        LinkBlockerToTrigger(blockersB, posB, triggerB);
    }

    // A secret room's single connection: same cross-to-teleport behavior as any door, but both
    // triggers start solid (locked) and a wall-colored SecretWallBlocker fills the gap on the outer
    // (non-secret) side, indistinguishable from a normal wall. Bombing it (see Bomb.cs) unlocks
    // both triggers for good, revealing the room.
    static void CreateSecretDoorLink(Vector2 secretPos, Vector2 secretInward, Vector2 outerPos, Vector2 outerInward, bool onVerticalWall, Sprite wallLikeSprite, Transform parent)
    {
        const float landingDepth = 1.5f;
        DoorTrigger secretTrigger = SpawnDoorTrigger(secretPos, secretInward, outerPos + outerInward * landingDepth, null, parent, locked: true);
        DoorTrigger outerTrigger = SpawnDoorTrigger(outerPos, outerInward, secretPos + secretInward * landingDepth, null, parent, locked: true);

        SecretWallBlocker blocker = SpawnSecretWallBlocker(outerPos, onVerticalWall, wallLikeSprite, parent);
        blocker.triggerA = secretTrigger;
        blocker.triggerB = outerTrigger;
    }

    static SecretWallBlocker SpawnSecretWallBlocker(Vector2 center, bool onVerticalWall, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("SecretWallBlocker", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(SecretWallBlocker));
        go.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        go.transform.SetParent(parent);
        go.transform.position = center;
        go.transform.localScale = onVerticalWall ? new Vector3(1f, DoorWidth, 1f) : new Vector3(DoorWidth, 1f, 1f);

        go.GetComponent<SpriteRenderer>().sprite = sprite;
        go.GetComponent<BoxCollider2D>().size = Vector2.one;

        return go.GetComponent<SecretWallBlocker>();
    }

    static DoorTrigger SpawnDoorTrigger(Vector2 pos, Vector2 inward, Vector2 destination, List<DoorTrigger> exitTriggers, Transform parent, bool locked = false)
    {
        GameObject go = new GameObject("DoorTrigger", typeof(BoxCollider2D), typeof(DoorTrigger));
        go.transform.SetParent(parent);
        // pos is the center of the room's own 1-unit-thick border wall row/column (half-width
        // 0.5), so the offset must clear at least that much to sit fully in the void beyond the
        // wall - otherwise the trigger still overlaps the wall's own tile and fires while the
        // player visually hasn't cleared it yet (worse the taller a neighboring wall tile renders,
        // e.g. a room's own top/side walls, whose sprite pokes further up than its collision
        // cell). Capped just under 1 unit so the two paired triggers on either side of the Gap
        // (width 3, doorWidth-sized triggers) never overlap each other.
        go.transform.position = pos - inward * 0.95f;

        BoxCollider2D collider = go.GetComponent<BoxCollider2D>();
        collider.isTrigger = !locked;
        collider.size = new Vector2(DoorWidth, DoorWidth);

        DoorTrigger trigger = go.GetComponent<DoorTrigger>();
        trigger.destination = destination;

        if (exitTriggers != null) exitTriggers.Add(trigger);
        return trigger;
    }

    static void PopulateRoom(RoomType type, int originX, int originY, Transform parent, Transform player,
        Sprite shopMarker, Sprite treasureMarker, Sprite secretMarker, Sprite gambleMarker, Sprite bossMarker, Sprite eventMarker, Sprite safeMarker,
        Sprite chestSprite)
    {
        Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);

        switch (type)
        {
            case RoomType.Shop:
                SpawnMarker("ShopMarker", center, shopMarker, parent);
                break;
            case RoomType.Treasure:
                SpawnMarker("TreasureMarker", center, treasureMarker, parent);
                // The treasure room guarantees both weapons are reachable on every floor - real
                // ground pickups now (see ItemIds.Sword/Staff), inspectable and equipped by hand
                // from the inventory panel instead of the old touch-to-equip WeaponPickup.
                SpawnItemPickup("SwordPickup", center + new Vector2(-1.5f, 0f), ItemIds.Sword, 1, parent);
                SpawnItemPickup("StaffPickup", center + new Vector2(1.5f, 0f), ItemIds.Staff, 1, parent);
                // 2 chests, each an independent rarity-weighted roll from a broad pool (see
                // ChestLootPool) - explicit request for chests as a new, inspect-before-take way to
                // find loot, distinct from a plain ground pickup.
                PlayerInventory chestInventory = player != null ? player.GetComponent<PlayerInventory>() : null;
                SpawnChest(center + new Vector2(-2.5f, -3f), chestSprite, chestInventory, parent);
                SpawnChest(center + new Vector2(2.5f, -3f), chestSprite, chestInventory, parent);
                break;
            case RoomType.Secret:
                SpawnMarker("SecretMarker", center, secretMarker, parent);
                break;
            case RoomType.Gamble:
                SpawnMarker("GambleMarker", center, gambleMarker, parent);
                break;
            case RoomType.Boss:
                SpawnMarker("BossMarker", center, bossMarker, parent);
                break;
            case RoomType.Event:
                SpawnMarker("EventMarker", center, eventMarker, parent);
                break;
            case RoomType.Safe:
                SpawnMarker("SafeMarker", center, safeMarker, parent);
                break;
            case RoomType.Start:
                SpawnItemPickup("GoldPickup", center + new Vector2(-2f, 1.5f), ItemIds.Gold, 5, parent);
                SpawnItemPickup("ShurikenPickup", center + new Vector2(-0.7f, 1.5f), ItemIds.Shuriken, 3, parent);
                SpawnItemPickup("CaillouPickup", center + new Vector2(0.7f, 1.5f), ItemIds.Caillou, 3, parent);
                SpawnItemPickup("BatonPickup", center + new Vector2(2f, 1.5f), ItemIds.Baton, 3, parent);
                SpawnItemPickup("BombPickup", center + new Vector2(0f, 2.7f), ItemIds.Bomb, 3, parent);
                break;
        }
    }

    static void AddDoorInfo(Dictionary<Vector2Int, List<(Vector2 pos, bool onVerticalWall)>> doorsByRoom, Vector2Int room, Vector2 pos, bool onVerticalWall)
    {
        if (!doorsByRoom.TryGetValue(room, out List<(Vector2 pos, bool onVerticalWall)> list))
        {
            list = new List<(Vector2, bool)>();
            doorsByRoom[room] = list;
        }
        list.Add((pos, onVerticalWall));
    }

    // rarity has no default - every item registration must name one explicitly (see ItemRarity),
    // it's just declared last/optional for C#'s "named args after positional" rule rather than
    // forcing every call site to also spell out every OTHER trailing param up to it.
    static void RegisterItem(List<ItemCatalog.Entry> entries, string id, string displayName, ItemCategory category, int maxStack, Sprite icon,
        string description = "", int weight = 0, bool isCursed = false, bool hasCursedWeapon = false,
        PlayerController.WeaponType cursedWeaponType = PlayerController.WeaponType.Fist, bool isWeapon = false,
        PlayerController.WeaponType weaponType = PlayerController.WeaponType.Fist, bool isThrowable = false, bool isTrap = false, int healAmount = 0,
        float speedBuffMultiplier = 0f, float speedBuffDuration = 0f, float staminaRegenBuffMultiplier = 0f, float staminaRegenBuffDuration = 0f,
        bool isEquipment = false, EquipmentSlotType equipmentSlot = default, StatType ringBonusStat = StatType.None, int armorValue = 0,
        int maxDurability = 0, MaterialType material = MaterialType.None, int rarity = ItemRarity.Min)
    {
        // Catches the exact bug SimpleRing shipped with (registered with no ringBonusStat, so
        // equipping it did literally nothing). Scoped to ring slots only, not every equipment
        // slot: an armor slot legitimately can have armorValue 0 when its real effect is a bespoke
        // id check elsewhere (see AntiHoleBoots/VisionGlasses below, neither uses armorValue at
        // all) - a ring has no such alternate mechanism, RingBonusStat is its only effect today.
        if (isEquipment && (equipmentSlot == EquipmentSlotType.RingLeft || equipmentSlot == EquipmentSlotType.RingRight) && ringBonusStat == StatType.None)
            Debug.LogWarning("RegisterItem: '" + id + "' (" + displayName + ") is a ring with no ringBonusStat - it will do nothing while equipped.");

        ItemDatabase.Register(new ItemDefinition
        {
            Id = id, DisplayName = displayName, Category = category, MaxStack = maxStack, Icon = icon,
            Description = description, Rarity = rarity, Weight = weight, IsCursed = isCursed, HasCursedWeapon = hasCursedWeapon,
            CursedWeaponType = cursedWeaponType, IsWeapon = isWeapon, Weapon = weaponType, IsThrowable = isThrowable, IsTrap = isTrap, HealAmount = healAmount,
            SpeedBuffMultiplier = speedBuffMultiplier, SpeedBuffDuration = speedBuffDuration,
            StaminaRegenBuffMultiplier = staminaRegenBuffMultiplier, StaminaRegenBuffDuration = staminaRegenBuffDuration,
            IsEquipment = isEquipment, EquipmentSlot = equipmentSlot, RingBonusStat = ringBonusStat, ArmorValue = armorValue,
            MaxDurability = maxDurability, Material = material
        });
        entries.Add(new ItemCatalog.Entry
        {
            id = id, displayName = displayName, category = category, maxStack = maxStack, icon = icon,
            description = description, rarity = rarity, weight = weight, isCursed = isCursed, hasCursedWeapon = hasCursedWeapon,
            cursedWeaponType = cursedWeaponType, isWeapon = isWeapon, weaponType = weaponType, isThrowable = isThrowable, isTrap = isTrap, healAmount = healAmount,
            speedBuffMultiplier = speedBuffMultiplier, speedBuffDuration = speedBuffDuration,
            staminaRegenBuffMultiplier = staminaRegenBuffMultiplier, staminaRegenBuffDuration = staminaRegenBuffDuration,
            isEquipment = isEquipment, equipmentSlot = equipmentSlot, ringBonusStat = ringBonusStat, armorValue = armorValue,
            maxDurability = maxDurability, material = material
        });
    }

    static void SpawnItemPickup(string name, Vector2 position, string itemId, int amount, Transform parent)
    {
        GameObject pickup = ItemPickup.SpawnAt(position, itemId, amount);
        pickup.name = name;
        pickup.transform.SetParent(parent);
    }

    // A broad cross-section of the game's real loot (weapons, armor, rings, materials, throwables,
    // a potion, gold) - rarity-weighted (see LootTable.PickWeighted), so a Chest can hand out
    // anything from a common Caillou to (rarely) a stat ring. Boss trophies/CursedSword deliberately
    // excluded - those stay earned from a boss kill or found loose on the floor, not handed out by
    // a plain Treasure-room container.
    static readonly string[] ChestLootPool =
    {
        ItemIds.Sword, ItemIds.Staff, ItemIds.HealthPotion, ItemIds.Gold,
        ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton, ItemIds.Bomb,
        ItemIds.Wood, ItemIds.Metal, ItemIds.Stone,
        ItemIds.IronHelmet, ItemIds.LeatherPauldrons, ItemIds.CombatGloves, ItemIds.WalkingBoots,
        ItemIds.SimpleNecklace, ItemIds.LeatherBelt, ItemIds.LeatherKneepads, ItemIds.SimpleRing,
        ItemIds.AntiHoleBoots, ItemIds.VisionGlasses,
        ItemIds.RingForce, ItemIds.RingDexterite, ItemIds.RingIntelligence, ItemIds.RingVitesse,
        ItemIds.RingConstitution, ItemIds.RingPortee, ItemIds.RingCharisme, ItemIds.RingEndurance,
    };

    static void SpawnChest(Vector2 position, Sprite sprite, PlayerInventory playerInventory, Transform parent)
    {
        string itemId = LootTable.PickWeighted(ChestLootPool);
        int amount = itemId == ItemIds.Gold ? Random.Range(3, 8)
            : (itemId == ItemIds.Wood || itemId == ItemIds.Metal || itemId == ItemIds.Stone) ? Random.Range(2, 5)
            : 1;
        GameObject chest = Chest.SpawnAt(position, itemId, amount, sprite, playerInventory);
        chest.transform.SetParent(parent);
    }

    static void SpawnMarker(string name, Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject marker = new GameObject(name, typeof(SpriteRenderer));
        marker.transform.SetParent(parent);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * 0.6f;
        marker.GetComponent<SpriteRenderer>().sprite = sprite;
    }

    // Template NPC for the dialogue+dice-roll system - reproduces the 3-choice example exactly
    // (threaten/ask/browse wares), DCs recalibrated from the original D10 pitch (15/7) onto the
    // official D20 bands (15 = Hard, 10 = Easy). Future NPCs can be built the same way.
    // Floating marker above an NPC's/interactable's head, same convention as EnemyController's
    // elite badges - the only thing that visually tells it apart from a flat-colored ground item.
    const float NpcBadgeHeight = 1.9f;

    static void AddNpcBadge(GameObject npcGO, Sprite badgeSprite)
    {
        if (badgeSprite == null) return;
        GameObject badgeGO = new GameObject("Badge", typeof(SpriteRenderer));
        badgeGO.transform.SetParent(npcGO.transform, false);
        badgeGO.transform.localPosition = new Vector3(0f, NpcBadgeHeight, 0f);
        badgeGO.transform.localScale = Vector3.one * 0.6f;
        SpriteRenderer badgeRenderer = badgeGO.GetComponent<SpriteRenderer>();
        badgeRenderer.sprite = badgeSprite;
        badgeRenderer.sortingOrder = 1;
    }

    static void SpawnExampleNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0.55f, 0.5f, 0.68f); // dim, faintly purple - a shady wanderer
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Etranger encapuchonne";
        npc.greeting = "Un etranger vous regarde avec mefiance.";
        npc.options = new List<DialogueOption>
        {
            new DialogueOption
            {
                text = "Le menacer pour qu'il vous cede un objet",
                checkStat = StatType.Charisme,
                dc = 15,
                risk = RiskTier.Risky,
                onSuccess = new DialogueOutcome
                {
                    message = "Il tremble et vous tend un objet.",
                    itemRewardPool = new[] { ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton, ItemIds.Bomb, ItemIds.Gold },
                },
                onFailure = new DialogueOutcome
                {
                    message = "Il vous frappe, s'enfuit et crache une malediction sur vous.",
                    statPenaltyTypes = new[] { StatType.Vitesse, StatType.Charisme },
                    statPenaltyAmounts = new[] { 2, 2 },
                    curse = true,
                    npcDisappearsForever = true,
                },
            },
            new DialogueOption
            {
                text = "Lui demander ce qu'il fait ici",
                checkStat = StatType.Intelligence,
                dc = 10,
                risk = RiskTier.Important,
                onSuccess = new DialogueOutcome
                {
                    messagePool = new[]
                    {
                        "Je fuis un contrat que je ne pouvais pas honorer.",
                        "Je cherche un tresor perdu par mon grand-pere dans ces murs.",
                        "Je me cache d'une guilde qui me veut du mal.",
                        "J'explore ce donjon depuis plus longtemps que vous ne l'imaginez.",
                    },
                },
                onFailure = new DialogueOutcome
                {
                    message = "Il n'a pas de temps a perdre avec des illettres pour l'instant.",
                },
            },
            new DialogueOption
            {
                text = "Voir sa marchandise",
                checkStat = StatType.None,
                onSuccess = new DialogueOutcome { message = "Il vous montre ses articles." },
            },
            new DialogueOption
            {
                text = "Lui demander de lever une malediction",
                checkStat = StatType.Intelligence,
                dc = 12,
                risk = RiskTier.Safe,
                onSuccess = new DialogueOutcome
                {
                    message = "Il murmure quelques mots et vous sentez un poids disparaitre.",
                    removesCursedItem = true,
                },
                onFailure = new DialogueOutcome
                {
                    message = "Il n'y connait rien a la magie et hausse les epaules.",
                },
            },
        };
    }

    const int HealthPotionPrice = 5;

    // The Safe room's NPC - advice and attribute allocation only (2026-09-14: no longer sells
    // anything, and no longer the way to rest - see RestBed/SpawnTavernFurniture, a physical bed
    // in the room now does both the heal+save and the "Se reposer" flavor text).
    static void SpawnTavernNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(1f, 0.9f, 0.7f); // warm, welcoming
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Le Tavernier";
        npc.greeting = "Bienvenue, voyageur. Ici, vous ne craignez rien.";
        npc.options = new List<DialogueOption>
        {
            new DialogueOption
            {
                text = "Demander des conseils",
                checkStat = StatType.None,
                onSuccess = new DialogueOutcome
                {
                    messagePool = new[]
                    {
                        "Les tonneaux explosifs ne reagissent qu'au feu ou a une explosion voisine - inutile de les frapper.",
                        "Un objet maudit se colle a votre inventaire. Certains PNJ savent lever une malediction.",
                        "Examinez un objet au sol avant de le ramasser si quelque chose vous semble louche.",
                        "La Force determine quels blocs vous pouvez briser a mains nues - une bombe passe outre.",
                        "Les salles securisees comme celle-ci sont les seules ou vous pouvez sauvegarder.",
                    },
                },
            },
        };
    }

    // An inanimate crafting station, built on the exact same NpcInteractable/DialogueManager
    // machinery as a talkable NPC (proximity prompt, E to open, numbered options) - a craft is
    // just a "purchase" (see DialogueOption.isPurchase) paid in a material instead of gold.
    static void SpawnCraftingTable(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("CraftingTable", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.2f;

        NpcInteractable table = go.GetComponent<NpcInteractable>();
        table.npcName = "Table de Craft";
        table.greeting = "Des materiaux et des outils pour l'equipement sont poses ici.";
        table.options = new List<DialogueOption>
        {
            new DialogueOption
            {
                text = "Fabriquer une Corde (2 Tissu)",
                isPurchase = true,
                purchaseItemId = ItemIds.Corde,
                costItemId = ItemIds.Cloth,
                costAmount = 2,
            },
            // Boss equipment crafts (2026-09-21 request: "la patoune de cerbere, une epee, une
            // griffe de cerbere et de la corde") - one per family, each a base equipment item plus
            // that family's own resourceDropIds (see BossFamilyFor) plus a generic ingredient.
            new DialogueOption
            {
                text = "Fabriquer une Patoune de Cerbere (1 Epee, 1 Griffe de Cerbere, 1 Corde)",
                isPurchase = true,
                purchaseItemId = ItemIds.CerberePaw,
                costItemIds = new[] { ItemIds.Sword, ItemIds.CerbereClaw, ItemIds.Corde },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer une Dague Crochet d'Anaconda (1 Epee, 1 Croc d'Anaconda, 1 Peau d'Anaconda)",
                isPurchase = true,
                purchaseItemId = ItemIds.AnacondaFangDagger,
                costItemIds = new[] { ItemIds.Sword, ItemIds.AnacondaFang, ItemIds.AnacondaSkin },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer des Epaulieres d'Ecorce d'Ent (1 Epaulieres de Cuir, 1 Ecorce d'Ent, 1 Seve d'Ent)",
                isPurchase = true,
                purchaseItemId = ItemIds.EntBarkPauldrons,
                costItemIds = new[] { ItemIds.LeatherPauldrons, ItemIds.EntBark, ItemIds.EntSap },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer des Gantelets de Plaque du Golem (1 Gants de Combat, 1 Plaque du Golem, 1 Rivet du Golem)",
                isPurchase = true,
                purchaseItemId = ItemIds.GolemPlateGauntlets,
                costItemIds = new[] { ItemIds.CombatGloves, ItemIds.GolemPlate, ItemIds.GolemRivet },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer un Collier d'Encre du Kraken (1 Collier Simple, 1 Encre de Kraken, 1 Oeil de Kraken)",
                isPurchase = true,
                purchaseItemId = ItemIds.KrakenInkNecklace,
                costItemIds = new[] { ItemIds.SimpleNecklace, ItemIds.KrakenInk, ItemIds.KrakenEye },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer des Bottes de Plume d'Acier (1 Bottes de Marche, 1 Plume de l'Aigle, 1 Serre de l'Aigle)",
                isPurchase = true,
                purchaseItemId = ItemIds.AigleFeatherBoots,
                costItemIds = new[] { ItemIds.WalkingBoots, ItemIds.AigleFeather, ItemIds.AigleTalon },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Fabriquer un Anneau d'Ombre de l'Arpenteur (1 Anneau Simple, 1 Ombre de l'Arpenteur, 1 Poussiere de l'Arpenteur)",
                isPurchase = true,
                purchaseItemId = ItemIds.ArpenteurShadowRing,
                costItemIds = new[] { ItemIds.SimpleRing, ItemIds.ArpenteurShadow, ItemIds.ArpenteurDust },
                costAmounts = new[] { 1, 1, 1 },
            },
            new DialogueOption
            {
                text = "Reparer / demonter mon equipement",
                checkStat = StatType.None,
                onSuccess = new DialogueOutcome
                {
                    message = "Vous etalez votre equipement sur la table.",
                    opensRepairPanel = true,
                },
            },
        };
    }

    // Consumables-only counterpart to SpawnCraftingTable above (2026-09-21 request: "une table
    // d'alchimie (pour les consommables) et une table de craft (pour les equippements)") - holds
    // every throwable/potion recipe that used to live on the crafting table, plus the 3 new
    // potions. Capped at 8 options (see HandleOptionInput's 9-key limit).
    static void SpawnAlchemyTable(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("AlchemyTable", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.2f;

        NpcInteractable table = go.GetComponent<NpcInteractable>();
        table.npcName = "Table d'Alchimie";
        table.greeting = "Fioles, herbes et ingredients sont poses ici.";
        table.options = new List<DialogueOption>
        {
            new DialogueOption
            {
                text = "Fabriquer un Baton (2 Bois)",
                isPurchase = true,
                purchaseItemId = ItemIds.Baton,
                costItemId = ItemIds.Wood,
                costAmount = 2,
            },
            new DialogueOption
            {
                text = "Fabriquer un Caillou (1 Pierre)",
                isPurchase = true,
                purchaseItemId = ItemIds.Caillou,
                costItemId = ItemIds.Stone,
                costAmount = 1,
            },
            new DialogueOption
            {
                text = "Fabriquer une Bombe (2 Metal)",
                isPurchase = true,
                purchaseItemId = ItemIds.Bomb,
                costItemId = ItemIds.Metal,
                costAmount = 2,
            },
            // 2 flower variants for the same Petite Potion de Vie (see DecorType.Flower) - trimmed
            // from the old 4-variant list to make room for the 3 new potions below within the
            // 9-option cap.
            new DialogueOption
            {
                text = "Fabriquer une Petite Potion de Vie (2 Fleur Ecarlate)",
                isPurchase = true,
                purchaseItemId = ItemIds.HealthPotion,
                costItemId = ItemIds.FlowerRed,
                costAmount = 2,
            },
            new DialogueOption
            {
                text = "Fabriquer une Petite Potion de Vie (2 Herbe Argentee)",
                isPurchase = true,
                purchaseItemId = ItemIds.HealthPotion,
                costItemId = ItemIds.Herb,
                costAmount = 2,
            },
            new DialogueOption
            {
                text = "Fabriquer une Grande Potion de Vie (2 Petite Potion de Vie, 1 Griffe de Cerbere)",
                isPurchase = true,
                purchaseItemId = ItemIds.GreaterHealthPotion,
                costItemIds = new[] { ItemIds.HealthPotion, ItemIds.CerbereClaw },
                costAmounts = new[] { 2, 1 },
            },
            // Explicit request: "la potion de vitesse necessite un champignon dore et d'autres compos".
            new DialogueOption
            {
                text = "Fabriquer une Potion de Vitesse (1 Champignon Dore, 2 Fleur Azur)",
                isPurchase = true,
                purchaseItemId = ItemIds.SpeedPotion,
                costItemIds = new[] { ItemIds.Mushroom, ItemIds.FlowerBlue },
                costAmounts = new[] { 1, 2 },
            },
            new DialogueOption
            {
                text = "Fabriquer une Potion d'Adrenaline (2 Herbe Argentee, 1 Fleur Azur)",
                isPurchase = true,
                purchaseItemId = ItemIds.AdrenalinePotion,
                costItemIds = new[] { ItemIds.Herb, ItemIds.FlowerBlue },
                costAmounts = new[] { 2, 1 },
            },
        };
    }

    // Furniture for the Safe room ("la taverne") - a bar counter behind the Tavernier, two
    // table+chairs clusters with a rug each, wall paintings, and a RestBed (see RestBed.cs) all in
    // shuffled room corners (GenerateCornerPositions keeps them off walls/doors and, since this
    // room never calls SpawnRoomDecor, away from the NPC/craft table too - both sit near center,
    // corners sit ~8 units out). Explicit request to make special rooms feel lived-in rather than
    // an empty box with a marker and an NPC (2026-09-14).
    static void SpawnTavernFurniture(Vector2 roomOrigin, Vector2 roomSize, Vector2 npcPos, Sprite woodSprite, Sprite rugSprite, Sprite wallDecorSprite, Transform parent,
        Health playerHealth, PlayerLimbs playerLimbs, PlayerInventory playerInventory, PlayerStats playerStats, Stamina playerStamina, PlayerController playerController, PlayerEquipment playerEquipment)
    {
        SpawnProp("BarCounter", npcPos + new Vector2(0f, 1.3f), woodSprite, parent, new Vector2(3.2f, 0.7f), 0f, 1, true);

        List<Vector2> corners = GenerateCornerPositions(4, roomOrigin, roomSize);
        SpawnTableCluster(corners[0], woodSprite, rugSprite, parent);
        SpawnTableCluster(corners[1], woodSprite, rugSprite, parent);
        SpawnRestBed(corners[2], woodSprite, parent, playerHealth, playerLimbs, playerInventory, playerStats, playerStamina, playerController, playerEquipment);

        // Paintings on the top wall, well clear (5 units) of a centered door's 2-unit gap - Safe is
        // a gated room type, its doors are always centered (see IsGatedRoomType).
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f - 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f + 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnAttributeBoard(roomOrigin, roomSize, wallDecorSprite, parent);
    }

    static void SpawnTableCluster(Vector2 pos, Sprite woodSprite, Sprite rugSprite, Transform parent)
    {
        SpawnProp("Rug", pos, rugSprite, parent, new Vector2(2.6f, 2.2f), 0f, -1, false);
        SpawnProp("Table", pos, woodSprite, parent, new Vector2(1f, 1f), 0f, 0, true);
        Color chairTint = new Color(0.5f, 0.34f, 0.2f);
        SpawnProp("Chair", pos + new Vector2(0.9f, 0f), woodSprite, parent, new Vector2(0.5f, 0.5f), 0f, 0, false, chairTint);
        SpawnProp("Chair", pos + new Vector2(-0.9f, 0f), woodSprite, parent, new Vector2(0.5f, 0.5f), 180f, 0, false, chairTint);
    }

    // The physical stand-in for the Tavernier's old "Se reposer" dialogue option (see RestBed.cs) -
    // wired with the same player component references DialogueManager itself uses for the outcome.
    static void SpawnRestBed(Vector2 pos, Sprite woodSprite, Transform parent,
        Health playerHealth, PlayerLimbs playerLimbs, PlayerInventory playerInventory, PlayerStats playerStats, Stamina playerStamina, PlayerController playerController, PlayerEquipment playerEquipment)
    {
        GameObject go = new GameObject("RestBed", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(RestBed));
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(1f, 1.8f, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = woodSprite;
        renderer.color = new Color(0.65f, 0.55f, 0.75f); // a blanket tone, distinct from the bare wood furniture
        renderer.sortingOrder = 0;

        go.GetComponent<CircleCollider2D>().radius = 1.3f;

        RestBed bed = go.GetComponent<RestBed>();
        bed.playerHealth = playerHealth;
        bed.playerLimbs = playerLimbs;
        bed.playerInventory = playerInventory;
        bed.playerStats = playerStats;
        bed.playerStamina = playerStamina;
        bed.playerController = playerController;
        bed.playerEquipment = playerEquipment;
    }

    // A wall-mounted attribute-allocation board (see AttributeBoard.cs) - present in every Safe
    // room variant (Tavern/Restaurant/Arcade), not just the Tavernier's, so points can be spent
    // regardless of which Safe room the player finds first (2026-09-21). Placed on the bottom
    // wall, mirroring the top wall's paintings (same 5-unit clearance from a centered door's
    // 2-unit gap, see SpawnTavernFurniture's note) so the two never collide.
    static void SpawnAttributeBoard(Vector2 roomOrigin, Vector2 roomSize, Sprite wallDecorSprite, Transform parent)
    {
        Vector2 pos = roomOrigin + new Vector2(roomSize.x / 2f - 5f, 1.3f);
        GameObject go = new GameObject("AttributeBoard", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(AttributeBoard));
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = wallDecorSprite;
        renderer.color = new Color(0.85f, 0.75f, 0.5f); // parchment/board tone, distinct from the plain WallDecor paintings
        renderer.sortingOrder = 1;

        go.GetComponent<CircleCollider2D>().radius = 1.2f;
    }

    // The second Safe room's NPC (see restaurantCell in GenerateLayout) - serves food eaten on the
    // spot (see EatOption): pay gold, get healed immediately, nothing ever enters the inventory.
    // Distinct from the Shop's Potion de Soin (a real carryable item) and from the Table de Craft's
    // potion recipes (see SpawnCraftingTable) - the Restaurant's dishes exist only as this dialogue.
    static void SpawnCookNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0.9f, 0.6f, 0.3f); // warm, kitchen-like - distinct from the Tavernier's tint
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Le Cuisinier";
        npc.greeting = "Un bon repas chaud avant de repartir ?";
        npc.options = new List<DialogueOption>
        {
            EatOption("Manger une Soupe du Jour (3 or)", 3, 4, "Une soupe chaude et simple. Ca fait du bien."),
            EatOption("Manger un Ragout Costaud (8 or)", 8, 12, "Un plat roboratif qui vous requinque bien."),
            EatOption("Manger un Festin du Chef (15 or)", 15, 25, "La meilleure table du donjon."),
        };
    }

    // Furniture for the Restaurant room - a kitchen counter (stove) behind the Cuisinier instead of
    // the Tavern's bar, two dining table+chairs clusters. No RestBed here (2026-09-16, explicit
    // request) - the Tavern (first Safe room, always guaranteed) is now the only save/rest point;
    // Restaurant/Arcade/the other Safe variants are pure content rooms.
    static void SpawnRestaurantFurniture(Vector2 roomOrigin, Vector2 roomSize, Vector2 npcPos, Sprite woodSprite, Sprite rugSprite, Sprite wallDecorSprite, Transform parent)
    {
        SpawnProp("Stove", npcPos + new Vector2(0f, 1.3f), woodSprite, parent, new Vector2(3.2f, 0.7f), 0f, 1, true, new Color(0.55f, 0.25f, 0.15f));

        List<Vector2> corners = GenerateCornerPositions(4, roomOrigin, roomSize);
        SpawnTableCluster(corners[0], woodSprite, rugSprite, parent);
        SpawnTableCluster(corners[1], woodSprite, rugSprite, parent);
        SpawnTableCluster(corners[2], woodSprite, rugSprite, parent);

        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f - 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f + 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnAttributeBoard(roomOrigin, roomSize, wallDecorSprite, parent);
    }

    // The third Safe room's NPC (see arcadeCell in GenerateLayout) - 3 coin-operated "machines",
    // each a stat check paid for in gold (see ArcadeOption): pay first (gold is gone either way,
    // win or lose), then roll exactly like any free dialogue check. risk = Safe on all three - a
    // loss never costs more than the entry fee, only a win ever changes anything.
    static void SpawnArcadeNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0.6f, 0.35f, 0.75f); // neon purple - distinct from the Tavernier/Cuisinier tints
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Bornes d'Arcade";
        npc.greeting = "Trois machines usees, encore allumees. Tentez votre chance.";
        npc.options = new List<DialogueOption>
        {
            ArcadeOption("Marteau de Force (2 or)", 2, StatType.Force, 12,
                "Le marteau frappe fort - la machine recrache une recompense !",
                new[] { ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton, ItemIds.Gold }),
            ArcadeOption("Jeu d'Adresse (2 or)", 2, StatType.Dexterite, 12,
                "Un geste precis - vous decrochez le lot !",
                new[] { ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Gold }),
            ArcadeOption("Machine a Sous (3 or)", 3, StatType.Charisme, 14,
                "Trois symboles s'alignent - jackpot !",
                new[] { ItemIds.Gold, ItemIds.HealthPotion }),
        };
    }

    // Furniture for the Arcade room - cabinets around the machines instead of the Tavern's bar or
    // the Restaurant's stove. No RestBed here (2026-09-16, explicit request) - see
    // SpawnRestaurantFurniture's note, the Tavern is now the only Safe-room save/rest point.
    static void SpawnArcadeFurniture(Vector2 roomOrigin, Vector2 roomSize, Vector2 npcPos, Sprite woodSprite, Sprite rugSprite, Sprite wallDecorSprite, Transform parent)
    {
        Color cabinetTint = new Color(0.3f, 0.2f, 0.4f);
        SpawnProp("Cabinet", npcPos + new Vector2(-1.6f, 0.6f), woodSprite, parent, new Vector2(0.9f, 1.6f), 0f, 1, true, cabinetTint);
        SpawnProp("Cabinet", npcPos + new Vector2(0f, 0.8f), woodSprite, parent, new Vector2(0.9f, 1.6f), 0f, 1, true, cabinetTint);
        SpawnProp("Cabinet", npcPos + new Vector2(1.6f, 0.6f), woodSprite, parent, new Vector2(0.9f, 1.6f), 0f, 1, true, cabinetTint);

        List<Vector2> corners = GenerateCornerPositions(4, roomOrigin, roomSize);
        SpawnTableCluster(corners[0], woodSprite, rugSprite, parent);
        SpawnTableCluster(corners[1], woodSprite, rugSprite, parent);
        SpawnTableCluster(corners[2], woodSprite, rugSprite, parent);

        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f - 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f + 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnAttributeBoard(roomOrigin, roomSize, wallDecorSprite, parent);
    }

    // Ingredients found ONLY in the Flower Garden variant (see SafeRoomVariant/ItemIds.Mushroom) -
    // includes Mushroom, unlike the world's FlowerIds pool (DecorType.Flower). No NPC/furniture
    // here, unlike the other Safe-room variants - just a resource-heavy room to walk through and
    // pick clean, with purely cosmetic jungle/ivy wall sprites (see SpawnIvyWall) setting the mood.
    static readonly string[] GardenFlowerIds = { ItemIds.FlowerRed, ItemIds.FlowerBlue, ItemIds.Herb, ItemIds.Mushroom };

    static void SpawnFlowerGardenContent(Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, Vector2 center, Sprite ivySprite, Transform parent)
    {
        List<Vector2> positions = GenerateDecorPositions(12, roomOrigin, roomSize, doors, new List<Vector2> { center });
        foreach (Vector2 pos in positions)
        {
            string flowerId = GardenFlowerIds[Random.Range(0, GardenFlowerIds.Length)];
            SpawnItemPickup(flowerId + "GardenPickup", pos, flowerId, 1, parent);
        }

        SpawnIvyWall(roomOrigin, roomSize, ivySprite, parent, new Color(0.2f, 0.5f, 0.25f));
    }

    // Dense ring of purely cosmetic wall sprites (no collider, no script - can never be picked up
    // or interacted with, see the explicit "uniquement sprite, pas ramassable" request) covering
    // all 4 walls, unlike the usual 2-on-the-top-wall WallDecor pattern every other Safe/Shop room
    // uses - this variant is meant to read as an overgrown room, not a room with a painting or two.
    static void SpawnIvyWall(Vector2 roomOrigin, Vector2 roomSize, Sprite ivySprite, Transform parent, Color tint)
    {
        float[] topBottomOffsets = { -8f, -3f, 3f, 8f };
        foreach (float off in topBottomOffsets)
        {
            SpawnProp("Ivy", roomOrigin + new Vector2(roomSize.x / 2f + off, roomSize.y - 1.1f), ivySprite, parent, new Vector2(1f, 1.3f), 0f, 1, false, tint);
            SpawnProp("Ivy", roomOrigin + new Vector2(roomSize.x / 2f + off, 1.1f), ivySprite, parent, new Vector2(1f, 1.3f), 0f, 1, false, tint);
        }
        float[] sideOffsets = { -3f, 3f };
        foreach (float off in sideOffsets)
        {
            SpawnProp("Ivy", roomOrigin + new Vector2(1.1f, roomSize.y / 2f + off), ivySprite, parent, new Vector2(1.3f, 1f), 0f, 1, false, tint);
            SpawnProp("Ivy", roomOrigin + new Vector2(roomSize.x - 1.1f, roomSize.y / 2f + off), ivySprite, parent, new Vector2(1.3f, 1f), 0f, 1, false, tint);
        }
    }

    // Abundant crafting materials, the Material Storage variant's counterpart to the Flower
    // Garden - no exclusive material here (Wood/Metal/Stone are all already found elsewhere via
    // destructible decor), just a lot of them in one guaranteed place.
    static readonly string[] StorageMaterialIds = { ItemIds.Wood, ItemIds.Metal, ItemIds.Stone };

    static void SpawnMaterialStorageContent(Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, Vector2 center, Sprite crateSprite, Sprite wallDecorSprite, Transform parent)
    {
        Color crateTint = new Color(0.45f, 0.4f, 0.32f);
        Vector2[] cratePositions =
        {
            center + new Vector2(-3f, 3f), center + new Vector2(3f, 3f),
            center + new Vector2(-3f, -3f), center + new Vector2(3f, -3f),
        };
        float[] crateRotations = { 10f, -10f, 5f, -5f };
        for (int i = 0; i < cratePositions.Length; i++)
            SpawnProp("Crate", cratePositions[i], crateSprite, parent, new Vector2(1f, 1f), crateRotations[i], 1, true, crateTint);

        List<Vector2> avoid = new List<Vector2> { center };
        avoid.AddRange(cratePositions);
        List<Vector2> positions = GenerateDecorPositions(12, roomOrigin, roomSize, doors, avoid);
        foreach (Vector2 pos in positions)
        {
            string materialId = StorageMaterialIds[Random.Range(0, StorageMaterialIds.Length)];
            SpawnItemPickup(materialId + "StoragePickup", pos, materialId, Random.Range(1, 4), parent);
        }

        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f - 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f + 5f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
    }

    // Furniture for the Shop room - crates and a shelf around the Marchand, a rug underfoot, and
    // the same wall paintings as the tavern (2026-09-14, same "make it feel lived-in" request).
    static void SpawnShopFurniture(Vector2 roomOrigin, Vector2 roomSize, Vector2 npcPos, Sprite crateSprite, Sprite rugSprite, Sprite wallDecorSprite, Transform parent)
    {
        SpawnProp("Rug", npcPos, rugSprite, parent, new Vector2(3f, 2.4f), 0f, -1, false, new Color(0.3f, 0.32f, 0.4f));
        SpawnProp("Crate", npcPos + new Vector2(-2.2f, 1.2f), crateSprite, parent, new Vector2(0.9f, 0.9f), 0f, 0, true);
        SpawnProp("Crate", npcPos + new Vector2(2.2f, 1.2f), crateSprite, parent, new Vector2(0.9f, 0.9f), 15f, 0, true);
        SpawnProp("Shelf", npcPos + new Vector2(0f, 2.4f), crateSprite, parent, new Vector2(3.4f, 0.6f), 0f, 1, true);

        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f - 6f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
        SpawnProp("WallDecor", roomOrigin + new Vector2(roomSize.x / 2f + 6f, roomSize.y - 1.3f), wallDecorSprite, parent, new Vector2(0.8f, 0.8f), 0f, 1, false);
    }

    // Shop rooms had a marker only until now (no purchase flow existed) - reuses the exact same
    // isPurchase mechanism as the Tavernier/Table de Craft (feature 9/10), just priced in gold.
    static void SpawnMerchantNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(1f, 0.88f, 0.5f); // gold, mercantile
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Marchand";
        npc.greeting = "Jetez un oeil, tout est a vendre.";
        npc.useShopUI = true; // visual clickable grid instead of a numbered list - see DialogueManager.BuildShopGrid
        npc.options = new List<DialogueOption>
        {
            BuyOption("Acheter un Shuriken (3 or)", ItemIds.Shuriken, 3),
            BuyOption("Acheter un Caillou (2 or)", ItemIds.Caillou, 2),
            BuyOption("Acheter un Baton (4 or)", ItemIds.Baton, 4),
            BuyOption("Acheter une Bombe (8 or)", ItemIds.Bomb, 8),
            BuyOption("Acheter une Potion de Soin (" + HealthPotionPrice + " or)", ItemIds.HealthPotion, HealthPotionPrice),
            BuyOption("Acheter un Casque de Fer (12 or)", ItemIds.IronHelmet, 12),
            BuyOption("Acheter des Epaulieres de Cuir (10 or)", ItemIds.LeatherPauldrons, 10),
            BuyOption("Acheter des Gants de Combat (8 or)", ItemIds.CombatGloves, 8),
            BuyOption("Acheter des Bottes de Marche (8 or)", ItemIds.WalkingBoots, 8),
            BuyOption("Acheter un Collier Simple (10 or)", ItemIds.SimpleNecklace, 10),
            BuyOption("Acheter une Ceinture de Cuir (6 or)", ItemIds.LeatherBelt, 6),
            BuyOption("Acheter des Genouilleres de Cuir (6 or)", ItemIds.LeatherKneepads, 6),
            RollRandomRingOption(),
            BuyOption("Acheter des Bottes Anti-Trous (20 or)", ItemIds.AntiHoleBoots, 20),
            BuyOption("Acheter des Lunettes de Vision (20 or)", ItemIds.VisionGlasses, 20),
        };
    }

    // Quest reward pools by difficulty (2026-09-21 request: "donnera un item en consequence" of
    // the quest's difficulty) - subsets of ChestLootPool's own items, kept rarity-appropriate:
    // difficulty 1 stays throwables/consumables (rarity 1), 2 moves up to basic gear (rarity 2), 3
    // reaches the stat rings/utility gear (rarity 3). No ordinary item sits above rarity 3 today
    // (ChestLootPool's own comment - boss trophies/CursedSword are earned, not handed out by a
    // container) so difficulty 4 reuses tier 3's pool and leans on QuestXpReward's steeper curve
    // instead to still feel like the biggest prize.
    static readonly string[] QuestRewardPoolTier1 = { ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton, ItemIds.Bomb, ItemIds.HealthPotion };
    static readonly string[] QuestRewardPoolTier2 = { ItemIds.IronHelmet, ItemIds.LeatherPauldrons, ItemIds.CombatGloves, ItemIds.WalkingBoots, ItemIds.SimpleNecklace, ItemIds.LeatherBelt, ItemIds.LeatherKneepads, ItemIds.SimpleRing };
    static readonly string[] QuestRewardPoolTier3 = { ItemIds.RingForce, ItemIds.RingDexterite, ItemIds.RingIntelligence, ItemIds.RingVitesse, ItemIds.RingConstitution, ItemIds.RingPortee, ItemIds.RingCharisme, ItemIds.RingEndurance, ItemIds.AntiHoleBoots, ItemIds.VisionGlasses };
    // "echange de ressources (rapporte moi 2 bombes)" - a small, always-affordable-to-gather pool.
    static readonly string[] QuestTurnInPool = { ItemIds.Bomb, ItemIds.Wood, ItemIds.Metal, ItemIds.Stone, ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton };

    static string[] QuestRewardPoolFor(int difficulty) => difficulty switch
    {
        1 => QuestRewardPoolTier1,
        2 => QuestRewardPoolTier2,
        _ => QuestRewardPoolTier3, // 3 and 4
    };

    // Floor-scaled like every other XP reward in this file (see RoomController.SpawnEnemies'
    // xpReward * floor) - this session's own numeric choice: difficulty 1 sits a bit above a
    // regular kill's own reward, 4 (a Region boss contract) pays close to that boss's own kill XP
    // again on top, since it demanded a full boss fight to even unlock.
    static int QuestXpReward(int difficulty, int floor) => difficulty switch
    {
        1 => 8 * floor,
        2 => 15 * floor,
        3 => 25 * floor,
        _ => 40 * floor,
    };

    static string QuestObjectiveText(QuestType type) => type switch
    {
        QuestType.KillMonsters => "J'ai besoin qu'on debarrasse ces couloirs de quelques monstres.",
        QuestType.KillZoneBoss => "Le boss de quartier terrorise cette zone. Reglez-lui son compte.",
        QuestType.KillVilleBoss => "Le boss de ville est une menace serieuse. Vous en sentez-vous capable ?",
        _ => "Le boss de region... peu en reviennent. Relevez le defi et vous serez largement recompense.",
    };

    static string QuestCompletionMessage(QuestType type) => type switch
    {
        QuestType.KillMonsters => "Bon debarras. Voici votre du.",
        QuestType.KillZoneBoss => "Le quartier vous doit une fiere chandelle.",
        QuestType.KillVilleBoss => "Toute la ville va en parler.",
        _ => "Un exploit dont peu peuvent se vanter. Bien joue.",
    };

    // The optional quest-giver (2026-09-21 request: "1 maximum par etage") - always placed in the
    // Shop room alongside the Marchand rather than a whole new special-room type, to avoid eating
    // further into the floor's Monster-room budget (see the comment on PlaceSpecialRoom's cell
    // count). One of the 5 QuestType values is rolled uniformly every floor - a KillMonsters or
    // TurnInItem quest is always completable that floor (regular monsters always spawn), and all 3
    // boss tiers exist on every floor too (see BuildFloorAssetsPart2/GenerateLayout's bossTiers).
    static void SpawnQuestNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent, int floor)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable), typeof(QuestNpc));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0.8f, 0.65f, 0.35f); // brass/parchment - a contracts board keeper
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Chasseur de primes";

        QuestType type = (QuestType)Random.Range(0, 5);
        QuestNpc quest = go.GetComponent<QuestNpc>();
        quest.type = type;

        if (type == QuestType.TurnInItem)
        {
            string itemId = QuestTurnInPool[Random.Range(0, QuestTurnInPool.Length)];
            int amount = Random.Range(2, 5);
            ItemDefinition definition = ItemDatabase.Get(itemId);
            string itemName = definition != null ? definition.DisplayName : itemId;

            npc.greeting = "J'ai besoin de fournitures. Rapportez-moi " + amount + "x " + itemName + " et vous serez recompense.";
            npc.options = new List<DialogueOption>
            {
                new DialogueOption
                {
                    text = "Rapporter " + amount + "x " + itemName,
                    isPurchase = true,
                    costItemId = itemId,
                    costAmount = amount,
                    checkStat = StatType.None,
                    onSuccess = new DialogueOutcome
                    {
                        message = "Merci, exactement ce qu'il me fallait.",
                        xpReward = QuestXpReward(1, floor),
                        itemRewardPool = QuestRewardPoolTier1,
                        npcDisappearsForever = true,
                    },
                },
            };
            return;
        }

        int difficulty = type switch
        {
            QuestType.KillMonsters => 1,
            QuestType.KillZoneBoss => 2,
            QuestType.KillVilleBoss => 3,
            _ => 4, // KillRegionBoss
        };
        quest.difficulty = difficulty;
        quest.targetCount = type == QuestType.KillMonsters ? Random.Range(3, 6) : 1;
        quest.xpReward = QuestXpReward(difficulty, floor);
        quest.rewardPool = QuestRewardPoolFor(difficulty);
        quest.objectiveText = QuestObjectiveText(type);
        quest.completionMessage = QuestCompletionMessage(type);
        quest.RefreshText();
    }

    static readonly string[] ShopRingIds =
    {
        ItemIds.RingForce, ItemIds.RingDexterite, ItemIds.RingIntelligence, ItemIds.RingVitesse,
        ItemIds.RingConstitution, ItemIds.RingPortee, ItemIds.RingCharisme, ItemIds.RingEndurance,
    };

    // One of the 8 stat rings, re-rolled every time a Shop room is populated (i.e. every floor) -
    // "anneau +1 dans une stat random" reads as "which ring you can buy is random", not that a
    // single shared item id rolls a different stat per copy (which InventorySlot's simple
    // itemId+count stacking has no room to represent per-instance anyway).
    static DialogueOption RollRandomRingOption()
    {
        string ringId = ShopRingIds[Random.Range(0, ShopRingIds.Length)];
        ItemDefinition definition = ItemDatabase.Get(ringId);
        return BuyOption("Acheter un " + definition.DisplayName + " (15 or)", ringId, 15);
    }

    static DialogueOption BuyOption(string text, string itemId, int goldPrice)
    {
        return new DialogueOption
        {
            text = text,
            isPurchase = true,
            purchaseItemId = itemId,
            costItemId = ItemIds.Gold,
            costAmount = goldPrice,
        };
    }

    // Same isPurchase flow as BuyOption, minus a purchaseItemId - DialogueManager.ChooseOption
    // treats a null/empty purchaseItemId as "consumed on the spot", applying onSuccess (here just a
    // heal) directly instead of adding anything to the inventory. Used by the Restaurant's Cuisinier.
    static DialogueOption EatOption(string text, int goldPrice, int healAmount, string message)
    {
        return new DialogueOption
        {
            text = text,
            isPurchase = true,
            costItemId = ItemIds.Gold,
            costAmount = goldPrice,
            onSuccess = new DialogueOutcome { message = message, healAmount = healAmount },
        };
    }

    // Same isPurchase-with-no-purchaseItemId flow as EatOption, but with a real checkStat/dc set -
    // DialogueManager.ChooseOption pays the entry fee first, then rolls exactly like a free dialogue
    // check (see RollAndResolve). risk stays Safe (see SpawnArcadeNpc) so onFailure's content is
    // never actually shown (apply is always false at 0% malus chance - Resolve prints its own
    // generic "Rien ne se passe." instead) - it only needs to be non-null so Resolve doesn't
    // short-circuit to an instant, message-less Close().
    static DialogueOption ArcadeOption(string text, int goldPrice, StatType stat, int dc, string winMessage, string[] rewardPool)
    {
        return new DialogueOption
        {
            text = text,
            isPurchase = true,
            costItemId = ItemIds.Gold,
            costAmount = goldPrice,
            checkStat = stat,
            dc = dc,
            risk = RiskTier.Safe,
            onSuccess = new DialogueOutcome { message = winMessage, itemRewardPool = rewardPool },
            onFailure = new DialogueOutcome(),
        };
    }

    // Builds a fixed enemy "recipe" for the room (positions + elite flag) and hands it to a
    // RoomController, which does the actual spawning (and re-spawning on reset) at play time.
    // Picks `count` world-space positions inside the room's walkable interior, away from its
    // walls, its own doors (so a spawn never blocks a threshold), each other, and optionally an
    // arbitrary set of points to avoid (e.g. already-placed enemies, when placing decor next).
    static List<Vector2> GenerateEnemySpawnPositions(int count, Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors)
        => GeneratePlacementPositions(count, roomOrigin, roomSize, doors, null, EnemySpawnWallMargin, EnemySpawnMinSpacing, EnemySpawnMinDoorDistance, 0f);

    static List<Vector2> GeneratePlacementPositions(int count, Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors,
        List<Vector2> avoid, float wallMargin, float minSpacing, float minDoorDistance, float minAvoidDistance)
    {
        Rect interior = new Rect(
            roomOrigin.x + wallMargin, roomOrigin.y + wallMargin,
            roomSize.x - wallMargin * 2f, roomSize.y - wallMargin * 2f);

        List<Vector2> accepted = new List<Vector2>();
        for (int i = 0; i < count; i++)
        {
            Vector2 best = interior.center;
            float bestNearestDistance = -1f;

            for (int attempt = 0; attempt < EnemySpawnMaxAttempts; attempt++)
            {
                Vector2 candidate = new Vector2(Random.Range(interior.xMin, interior.xMax), Random.Range(interior.yMin, interior.yMax));

                bool rejected = false;
                foreach ((Vector2 pos, bool onVerticalWall) door in doors)
                {
                    if (Vector2.Distance(candidate, door.pos) < minDoorDistance) { rejected = true; break; }
                }
                if (!rejected && avoid != null)
                {
                    foreach (Vector2 a in avoid)
                    {
                        if (Vector2.Distance(candidate, a) < minAvoidDistance) { rejected = true; break; }
                    }
                }
                if (rejected) continue;

                float nearestNeighbor = float.MaxValue;
                foreach (Vector2 other in accepted) nearestNeighbor = Mathf.Min(nearestNeighbor, Vector2.Distance(candidate, other));

                if (accepted.Count == 0 || nearestNeighbor >= minSpacing)
                {
                    best = candidate;
                    break;
                }
                // Every attempt got rejected for spacing - keep the least-crowded candidate seen
                // so a spawn position is always produced instead of leaving a gap in the recipe.
                if (nearestNeighbor > bestNearestDistance)
                {
                    bestNearestDistance = nearestNeighbor;
                    best = candidate;
                }
            }

            accepted.Add(best);
        }
        return accepted;
    }

    // The room's 4 interior corners (inset by the same wall margin as a scattered spawn), shuffled
    // so which enemy gets which corner varies. Cycles back through the list (with a small jitter
    // so repeats don't stack exactly) if count > 4, though no current composition needs that.
    static List<Vector2> GenerateCornerPositions(int count, Vector2 roomOrigin, Vector2 roomSize)
    {
        float mx = EnemySpawnWallMargin;
        List<Vector2> corners = new List<Vector2>
        {
            roomOrigin + new Vector2(mx, mx),
            roomOrigin + new Vector2(roomSize.x - mx, mx),
            roomOrigin + new Vector2(mx, roomSize.y - mx),
            roomOrigin + new Vector2(roomSize.x - mx, roomSize.y - mx),
        };
        Shuffle(corners);

        List<Vector2> positions = new List<Vector2>();
        for (int i = 0; i < count; i++)
        {
            Vector2 jitter = i >= corners.Count ? Random.insideUnitCircle * 1.5f : Vector2.zero;
            positions.Add(corners[i % corners.Count] + jitter);
        }
        return positions;
    }

    // 4 positions forming a small square around the room's center, with any enemy beyond the 4th
    // (the 5-Larve pack) landing dead center, inside the square - the "4 in a square + 1 in the
    // middle" formation requested over the previous tight single-point cluster.
    static List<Vector2> GenerateCenterSquarePositions(int count, Vector2 roomOrigin, Vector2 roomSize)
    {
        const float Radius = 2f;
        Vector2 center = roomOrigin + roomSize / 2f;
        Vector2[] square =
        {
            center + new Vector2(-Radius, -Radius),
            center + new Vector2(Radius, -Radius),
            center + new Vector2(-Radius, Radius),
            center + new Vector2(Radius, Radius),
        };

        List<Vector2> positions = new List<Vector2>();
        for (int i = 0; i < count; i++) positions.Add(i < square.Length ? square[i] : center);
        return positions;
    }

    static List<Vector2> GenerateDecorPositions(int count, Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, List<Vector2> avoid)
        => GeneratePlacementPositions(count, roomOrigin, roomSize, doors, avoid, DecorWallMargin, DecorMinSpacing, DecorMinDoorDistance, DecorMinAvoidDistance);

    enum DecorType { StoneBlock, WoodDebris, MetalDebris, ExplosiveBarrel, FuelPuddle, LootPickup, FloorTrap, Hole, Flower }

    // Crafting-only ingredients (see DecorType.Flower/SpawnCraftingTable) - one picked at random
    // per Flower decor slot, same ground-pickup pattern as LootPickup. Mushroom is deliberately left
    // out (2026-09-16, explicit request) - it only grows in the Flower Garden Safe-room variant
    // (see GardenFlowerIds/SpawnFlowerGardenContent), everywhere else it's simply never found.
    static readonly string[] FlowerIds = { ItemIds.FlowerRed, ItemIds.FlowerBlue, ItemIds.Herb };

    // Chance a LootPickup slot spawns one of the special weight/curse/trap items instead of the
    // common LootTable pool - rare environmental finds, never a kill/break reward.
    const float SpecialLootChance = 0.15f;
    static readonly string[] SpecialLootIds = { ItemIds.Anvil, ItemIds.CursedSword, ItemIds.TrapSack };

    // Everything BuildFloorAssetsPart1/Part2 create that's still referenced LATER in Build() (past
    // the asset/item-registration block itself) - a sprite/tile only ever used to register an item
    // (e.g. every crafting material, every piece of starter armor) stays a purely local variable
    // inside those methods instead of living here, see their own comments for how that set was
    // derived. 2026-09-16 cleanup (full-codebase review, finding #9): Build() used to inline sprite
    // generation and item registration directly, ~290 lines mixed into the ~1700-line method body.
    class FloorAssets
    {
        public Sprite floorSprite;
        public Sprite wallSprite;
        public Sprite playerSprite;
        public Sprite bossProjectileSprite;
        public Sprite bossSlobberPuddleSprite;
        public DecorSprites decorSprites;
        public Sprite furnitureWoodSprite;
        public Sprite rugSprite;
        public Sprite wallDecorSprite;
        public Sprite shopCrateSprite;
        public Sprite enemyGlowSprite;
        public Sprite speedUpBadge;
        public Sprite hpUpBadge;
        public RoomController.EnemyPresetEntry[] enemyPresets;
        public Sprite shopMarker;
        public Sprite treasureMarker;
        public Sprite chestSprite;
        public Sprite secretMarker;
        public Sprite gambleMarker;
        public Sprite bossMarker;
        public Sprite eventMarker;
        public Sprite safeMarker;
        public Sprite stairsMarker;
        public Sprite stairsCageSprite;
        public Sprite leverSprite;
        public Sprite craftingTableSprite;
        public Sprite alchemyTableSprite;
        public Sprite projectileSprite;
        public Sprite fistVisualSprite;
        public Sprite swordVisualSprite;
        public Sprite explosionSprite;
        public Sprite lightningOrbSprite;
        public Sprite lightningBoltSprite;
        public Sprite fireballSprite;
        public Sprite fireLineSprite;
        public Sprite burnIconSprite;
        public Sprite doorBarrierSprite;
        public Sprite secretWallSprite;
        public Sprite outlineRingSprite;
        public Sprite npcStrangerSprite;
        public Sprite npcElderSprite;
        public Sprite npcMerchantSprite;
        public Sprite npcBadgeSprite;
        public Tile floorTile;
        public Tile wallTile;
    }

    // Bundles every sprite a decor piece might need, so SpawnRoomDecor's signature doesn't grow
    // with each new decor type.
    struct DecorSprites
    {
        public Sprite stoneBlock;
        public Sprite woodDebris;
        public Sprite metalDebris;
        public Sprite barrel;
        public Sprite fuelPuddle;
        public Sprite floorTrap;
        public Sprite explosion;
        public Sprite hole;
        public Sprite backroomsChair;
        public Sprite backroomsPillar;
        public Sprite backroomsDoor;
        public Sprite backroomsStain;
    }

    // 0-2 decor pieces per cell (a multi-cell room scales up via cellCount), kept away from walls/
    // doors and from `avoid` (already-placed enemies, or a special room's marker/NPC at its
    // center). Each position gets a random decor type. On a Backrooms-biome floor, every room that
    // calls this also gets a denser, purely-cosmetic layer of "loufoque" props on top (see
    // SpawnBackroomsDecor) - every room already routes through here, so no extra call sites needed.
    static void SpawnRoomDecor(Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, List<Vector2> avoid, DecorSprites sprites, Transform parent, int cellCount = 1)
    {
        if (CurrentBiome == Biome.Backrooms) SpawnBackroomsDecor(roomOrigin, roomSize, doors, avoid, sprites, parent, cellCount);

        int count = Random.Range(0, 3) * cellCount;
        if (count == 0) return;

        foreach (Vector2 pos in GenerateDecorPositions(count, roomOrigin, roomSize, doors, avoid))
        {
            switch ((DecorType)Random.Range(0, 9))
            {
                case DecorType.StoneBlock:
                    SpawnDestructible("StoneBlock", pos, sprites.stoneBlock, StoneBlockHealth, StoneBlockRequiredForce, parent, ItemIds.Stone);
                    break;
                case DecorType.WoodDebris:
                    SpawnDestructible("WoodDebris", pos, sprites.woodDebris, WoodDebrisHealth, WoodDebrisRequiredForce, parent, ItemIds.Wood);
                    break;
                case DecorType.MetalDebris:
                    SpawnDestructible("MetalDebris", pos, sprites.metalDebris, MetalDebrisHealth, MetalDebrisRequiredForce, parent, ItemIds.Metal);
                    break;
                case DecorType.ExplosiveBarrel:
                    SpawnExplosiveBarrel(pos, sprites.barrel, sprites.explosion, parent);
                    break;
                case DecorType.FuelPuddle:
                    SpawnFuelPuddle(pos, sprites.fuelPuddle, parent);
                    break;
                case DecorType.LootPickup:
                    if (Random.value < SpecialLootChance)
                    {
                        string specialId = SpecialLootIds[Random.Range(0, SpecialLootIds.Length)];
                        SpawnItemPickup(specialId + "Pickup", pos, specialId, 1, parent);
                    }
                    else
                    {
                        LootTable.PickRandomItem(out string lootId, out int lootAmount);
                        SpawnItemPickup(lootId + "Pickup", pos, lootId, lootAmount, parent);
                    }
                    break;
                case DecorType.FloorTrap:
                    SpawnFloorTrap(pos, sprites.floorTrap, parent);
                    break;
                case DecorType.Hole:
                    SpawnHole(pos, sprites.hole, parent);
                    break;
                case DecorType.Flower:
                    string flowerId = FlowerIds[Random.Range(0, FlowerIds.Length)];
                    SpawnItemPickup(flowerId + "Pickup", pos, flowerId, 1, parent);
                    break;
            }
        }
    }

    enum BackroomsDecorType { Chair, Pillar, LoneDoor, DampStain }

    // Denser than normal decor (endless-identical-room monotony is the point). Pillar/LoneDoor are
    // solid - per feedback, a room full of decor you walk straight through didn't feel obstructive
    // enough - while Chair/DampStain stay purely cosmetic (no collider, no script).
    static void SpawnBackroomsDecor(Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, List<Vector2> avoid, DecorSprites sprites, Transform parent, int cellCount)
    {
        int count = Random.Range(2, 4) * cellCount;
        foreach (Vector2 pos in GenerateDecorPositions(count, roomOrigin, roomSize, doors, avoid))
        {
            switch ((BackroomsDecorType)Random.Range(0, 4))
            {
                case BackroomsDecorType.Chair:
                    // Wildly inconsistent scale on purpose - a chair the size of a doormat next to
                    // one the size of a fridge, with nothing explaining why.
                    float chairScale = Random.Range(0.6f, 2.4f);
                    SpawnProp("Chair", pos, sprites.backroomsChair, parent, new Vector2(chairScale, chairScale), 0f, 0, false);
                    break;
                case BackroomsDecorType.Pillar:
                    SpawnProp("Pillar", pos, sprites.backroomsPillar, parent, new Vector2(0.8f, 2.4f), 0f, 1, true);
                    break;
                case BackroomsDecorType.LoneDoor:
                    // Standing in open floor, never against a wall, sometimes sideways - it leads
                    // nowhere, but you still can't walk through it.
                    float doorRotation = Random.value < 0.5f ? 0f : 90f;
                    SpawnProp("LoneDoor", pos, sprites.backroomsDoor, parent, new Vector2(1f, 1.8f), doorRotation, 0, true);
                    break;
                case BackroomsDecorType.DampStain:
                    float stainScale = Random.Range(1f, 2.8f);
                    SpawnProp("DampStain", pos, sprites.backroomsStain, parent, new Vector2(stainScale, stainScale), 0f, -1, false);
                    break;
            }
        }
    }

    // Generic "flat sprite stretched/rotated into a prop" spawner - started out Backrooms-only,
    // now reused for tavern/shop furniture too (see SpawnTavernFurniture/SpawnShopFurniture) since
    // the same trick (one plain colored square, non-uniform scale + rotation) reads fine as a bar
    // counter, a table, a crate, etc. without needing a dedicated sprite per furniture piece.
    static void SpawnProp(string name, Vector2 position, Sprite sprite, Transform parent, Vector2 scale, float rotationZ, int sortingOrder, bool solid, Color? tint = null)
    {
        GameObject go = solid
            ? new GameObject(name, typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D))
            : new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        if (tint.HasValue) renderer.color = tint.Value;

        if (solid)
        {
            // Sized in LOCAL space (0.7 of a unit before the transform's own scale stretches it),
            // so a taller/wider prop still blocks roughly its drawn footprint instead of always a
            // fixed 1x1 regardless of how comically oversized the sprite scale made it look.
            go.GetComponent<BoxCollider2D>().size = Vector2.one * 0.7f;
            go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            go.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        }
    }

    static void SpawnDestructible(string name, Vector2 position, Sprite sprite, int maxHealth, int requiredForce, Transform parent, string guaranteedDropItemId = null)
    {
        GameObject go = new GameObject(name, typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(DestructibleObject));
        go.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        go.GetComponent<BoxCollider2D>().size = Vector2.one * 0.9f;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;

        DestructibleObject destructible = go.GetComponent<DestructibleObject>();
        destructible.maxHealth = maxHealth;
        destructible.requiredForce = requiredForce;
        destructible.guaranteedDropItemId = guaranteedDropItemId;
    }

    static void SpawnExplosiveBarrel(Vector2 position, Sprite sprite, Sprite explosionSprite, Transform parent)
    {
        GameObject go = new GameObject("ExplosiveBarrel", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(ExplosiveBarrel));
        go.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        go.GetComponent<BoxCollider2D>().size = Vector2.one * 0.9f;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;

        go.GetComponent<ExplosiveBarrel>().explosionSprite = explosionSprite;
    }

    static void SpawnFuelPuddle(Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("FuelPuddle", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(FuelPuddle));
        go.transform.SetParent(parent);
        go.transform.position = position;
        // Flattened into a puddle shape rather than the plain circle it's drawn as.
        go.transform.localScale = new Vector3(1.4f, 0.7f, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1;

        go.GetComponent<CircleCollider2D>().isTrigger = true;
    }

    static void SpawnFloorTrap(Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("FloorTrap", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(FloorTrap));
        go.transform.SetParent(parent);
        go.transform.position = position;
        // Same flattened-decal look as FuelPuddle - visually unremarkable, on purpose.
        go.transform.localScale = new Vector3(1.4f, 0.7f, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1;

        go.GetComponent<CircleCollider2D>().isTrigger = true;

        // Flavor only - never visible before the trap triggers (see FloorTrap's own comment) -
        // decides which BodyPart zone it targets (see FloorTrap.AttackSourceFor).
        go.GetComponent<FloorTrap>().trapType = Random.value < 0.5f ? TrapType.BearTrap : TrapType.CollapsingCeiling;
    }

    static void SpawnHole(Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("Hole", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Hole));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1;

        go.GetComponent<CircleCollider2D>().isTrigger = true;
    }

    // How a composition's positions are laid out - replaces a plain "clustered" flag, since a
    // single tight cluster (1.2-unit radius) read as "all mobs in one pile" per feedback.
    enum SpawnFormation { Scattered, Corners, CenterSquare }

    // Encounter compositions a Monster room can roll (used unless a floor-wide theme is active) -
    // paired 1:1 with EncounterPatternFormation. Expanded from 4 to 10 to 16 across two 2026-09-21
    // reports ("on a pas assez de monstres differents", then "rajoute quelques mobs" adding Momie/
    // Sanglier/Skinwalker to the roster - see EnemyType) - every new species gets woven into at
    // least 2-3 mixed patterns rather than just appended as its own solo triple, so variety keeps
    // scaling with the roster instead of just adding more ways to see the same old 3.
    static readonly EnemyType[][] EncounterPatterns =
    {
        new[] { EnemyType.Zombie, EnemyType.Zombie, EnemyType.Zombie },
        new[] { EnemyType.ChauveSouris, EnemyType.ChauveSouris, EnemyType.ChauveSouris },
        new[] { EnemyType.ChauveSouris, EnemyType.ChauveSouris, EnemyType.Zombie },
        new[] { EnemyType.Larve, EnemyType.Larve, EnemyType.Larve, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.Zombie, EnemyType.Zombie, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.ChauveSouris, EnemyType.ChauveSouris, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.Zombie, EnemyType.ChauveSouris, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.Zombie, EnemyType.Larve, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.ChauveSouris, EnemyType.Zombie, EnemyType.ChauveSouris, EnemyType.Larve },
        new[] { EnemyType.Larve, EnemyType.Larve, EnemyType.Larve, EnemyType.ChauveSouris },
        new[] { EnemyType.Momie, EnemyType.Momie, EnemyType.Zombie },
        new[] { EnemyType.Sanglier, EnemyType.Sanglier, EnemyType.Sanglier, EnemyType.Larve },
        new[] { EnemyType.Skinwalker, EnemyType.Skinwalker, EnemyType.ChauveSouris },
        new[] { EnemyType.Momie, EnemyType.Skinwalker, EnemyType.Larve, EnemyType.Larve },
        new[] { EnemyType.Sanglier, EnemyType.Sanglier, EnemyType.Zombie },
        new[] { EnemyType.Skinwalker, EnemyType.Sanglier, EnemyType.ChauveSouris, EnemyType.Larve },
        // Sorcier (2026-09-21) - always mixed with melee escorts rather than alone, so its flee AI
        // actually has something to kite away from while the escort closes in.
        new[] { EnemyType.Sorcier, EnemyType.Zombie, EnemyType.Zombie },
        new[] { EnemyType.Sorcier, EnemyType.Sorcier, EnemyType.Larve },
        new[] { EnemyType.Sorcier, EnemyType.Skinwalker, EnemyType.Larve },
    };
    static readonly SpawnFormation[] EncounterPatternFormation =
    {
        SpawnFormation.Corners, SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.CenterSquare,
        SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Scattered,
        SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Corners,
        SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Corners, SpawnFormation.Scattered,
        SpawnFormation.Scattered, SpawnFormation.Scattered, SpawnFormation.Scattered,
    };

    static bool SetupMonsterRoom(List<Vector2Int> memberCells, int originX, int originY, Vector2 roomSize, Transform parent, Transform player,
        RoomController.EnemyPresetEntry[] presets, Sprite speedUpBadge, Sprite hpUpBadge, Sprite glowSprite, Sprite doorBarrierSprite,
        DecorSprites decorSprites, EnemyType? floorTheme, List<(Vector2 pos, bool onVerticalWall)> doors, List<RoomController> controllers, bool startCleared)
    {
        Vector2Int gridPos = memberCells[0];
        int cellCount = memberCells.Count;
        // A multi-cell room repeats its base composition instead of a single bigger pattern - 2
        // cells keeps the original size (a modest bump isn't worth dedicated content), 4/6/8 scale
        // up proportionally for the bigger, more spectacular formations this feature exists for.
        int scaleFactor = Mathf.Max(1, cellCount / 2);

        EnemyType[] baseComposition;
        SpawnFormation formation;
        if (floorTheme.HasValue)
        {
            baseComposition = new EnemyType[Random.Range(2, 4)];
            for (int i = 0; i < baseComposition.Length; i++) baseComposition[i] = floorTheme.Value;
            formation = SpawnFormation.Scattered;
        }
        else
        {
            int patternIndex = Random.Range(0, EncounterPatterns.Length);
            baseComposition = EncounterPatterns[patternIndex];
            formation = EncounterPatternFormation[patternIndex];
        }
        // A fixed 4-corner/center-square shape only reads right at the original single-cell scale
        // - a merged room's whole point is using the extra space, so it always spreads out instead.
        if (cellCount > 1) formation = SpawnFormation.Scattered;

        EnemyType[] composition = new EnemyType[baseComposition.Length * scaleFactor];
        for (int i = 0; i < composition.Length; i++) composition[i] = baseComposition[i % baseComposition.Length];

        int count = composition.Length;
        bool hasElite = Random.value < EliteChance;
        int eliteIndex = hasElite ? Random.Range(0, count) : -1;
        EliteModifier eliteModifier = hasElite ? (Random.value < 0.5f ? EliteModifier.SpeedUp : EliteModifier.HpUp) : EliteModifier.None;

        Vector2 roomOrigin = new Vector2(originX, originY);
        List<Vector2> positions = formation switch
        {
            SpawnFormation.Corners => GenerateCornerPositions(count, roomOrigin, roomSize),
            SpawnFormation.CenterSquare => GenerateCenterSquarePositions(count, roomOrigin, roomSize),
            _ => GenerateEnemySpawnPositions(count, roomOrigin, roomSize, doors),
        };

        RoomController.EnemySpawn[] recipe = new RoomController.EnemySpawn[count];
        for (int i = 0; i < count; i++)
        {
            recipe[i] = new RoomController.EnemySpawn
            {
                localOffset = positions[i] - roomOrigin,
                type = composition[i],
                modifier = i == eliteIndex ? eliteModifier : EliteModifier.None,
            };
        }

        GameObject roomGO = new GameObject("MonsterRoom_" + gridPos, typeof(RoomController));
        roomGO.transform.SetParent(parent);

        RoomController controller = roomGO.GetComponent<RoomController>();
        controller.gridPos = gridPos;
        controller.memberCells = memberCells.ToArray();
        controller.roomOrigin = new Vector2(originX, originY);
        controller.roomSize = roomSize;
        controller.player = player;
        controller.presets = presets;
        controller.speedUpBadge = speedUpBadge;
        controller.hpUpBadge = hpUpBadge;
        controller.glowSprite = glowSprite;
        controller.recipe = recipe;
        controller.startCleared = startCleared;
        // Feeds a resumed save's progress back into DungeonGenerator's live tracking (see
        // clearedRoomsThisFloor) so re-saving later still reflects everything cleared so far,
        // restored rooms included - not just whatever gets freshly cleared after resuming.
        controller.OnRoomCleared += cells => { foreach (Vector2Int c in cells) clearedRoomsThisFloor.Add(c); };

        foreach ((Vector2 pos, bool onVerticalWall) door in doors)
        {
            GameObject blocker = SpawnDoorBlocker(door.pos, door.onVerticalWall, doorBarrierSprite, roomGO.transform);
            controller.doorBlockers.Add(blocker);
        }

        SpawnRoomDecor(roomOrigin, roomSize, doors, positions, decorSprites, roomGO.transform, cellCount);

        controllers.Add(controller);
        return hasElite;
    }

    // A floor has 3 bosses now (explicit request) - one from each power tier, all the same
    // biome-themed family (see BossFamilyFor). Zone is an early, easy encounter; Region is the
    // single farthest room on the floor and the real final fight. Public (2026-09-21) so
    // BossRoomController/QuestNpc can tell which tier a boss-kill quest needs.
    public enum BossTier { Zone, Ville, Region }

    struct BossFamily
    {
        public string zoneName, villeName, regionName;
        public Color color;
        public string dropItemId;
        // 3 crafting-material drops themed to this family (2026-09-21 request) - separate from
        // dropItemId above, which stays the single unique "trophy" collectible.
        public string[] resourceDropIds;
        public string[] mask;
        // Boss-intro cutscene content (2026-09-21 request) - introKey names the voice clip folder
        // entry (see BossRoomController.introVoiceKey/Tools/generate_boss_intro_voices.ps1),
        // introDescription is the lore line read aloud alongside the name/tier.
        public string introKey;
        public string introDescription;
    }

    struct BossTierStats
    {
        public int health;
        public int contactDamage;
        public float chargeSpeed;
        public int volleyDamage;
        public int volleyCount;
        public float moveSpeed;
        public float dropChance;
        public int xpReward;
    }

    // One family per biome, each with a name for all 3 tiers - stats/drop chance come from
    // BossTierStatsFor instead, so every family scales identically regardless of theme. Cave
    // reuses the existing Cerberus (a hellhound already fits "guards the depths"); the other 6 are
    // new, including both of the user's own examples (SkyCastle eagle, Cave... reassigned to a
    // steel golem in City instead so Cerberus's existing lore/drop item isn't wasted - still a
    // literal golem, just a different zone).
    static BossFamily BossFamilyFor(Biome biome) => biome switch
    {
        Biome.Jungle => new BossFamily { zoneName = "Jeune Anaconda", villeName = "Anaconda Royale", regionName = "Anaconda Primordiale", color = new Color(0.2f, 0.55f, 0.15f), dropItemId = ItemIds.AnacondaScale, mask = AnacondaMask,
            resourceDropIds = new[] { ItemIds.AnacondaFang, ItemIds.AnacondaEye, ItemIds.AnacondaSkin },
            introKey = "jungle", introDescription = "Ce serpent gigantesque rode dans les frondaisons depuis des siecles, digerant lentement tout ce qui a eu le malheur de croiser sa route. Il vient de sentir une nouvelle proie approcher, et il en a apres VOUS." },
        Biome.Forest => new BossFamily { zoneName = "Sapling Enrage", villeName = "Ent Corrompu", regionName = "Ent Ancien, Coeur de la Foret", color = new Color(0.35f, 0.28f, 0.12f), dropItemId = ItemIds.EntHeartshard, mask = EntMask,
            resourceDropIds = new[] { ItemIds.EntBark, ItemIds.EntRoot, ItemIds.EntSap },
            introKey = "forest", introDescription = "Autrefois gardien paisible de cette foret, cet arbre anime a vu trop d'aventuriers pietiner ses racines sans jamais s'excuser. Sa patience est epuisee, et il en a apres VOUS." },
        Biome.City => new BossFamily { zoneName = "Automate Rouille", villeName = "Golem d'Acier", regionName = "Golem d'Acier, Gardien de la Cite", color = new Color(0.55f, 0.56f, 0.6f), dropItemId = ItemIds.GolemCore, mask = GolemMask,
            resourceDropIds = new[] { ItemIds.GolemPlate, ItemIds.GolemRivet, ItemIds.GolemCable },
            introKey = "city", introDescription = "Assemble a partir des ruines d'une cite oubliee, ce golem de fer et de rouille ne connait qu'un seul ordre : proteger ce territoire de tout intrus. Ses capteurs viennent de vous reperer, et il en a apres VOUS." },
        Biome.Beach => new BossFamily { zoneName = "Calmar Geant", villeName = "Kraken Echoue", regionName = "Kraken des Abysses", color = new Color(0.1f, 0.25f, 0.45f), dropItemId = ItemIds.KrakenTentacle, mask = KrakenMask,
            resourceDropIds = new[] { ItemIds.KrakenEye, ItemIds.KrakenSucker, ItemIds.KrakenInk },
            introKey = "beach", introDescription = "Echoue sur ce rivage il y a bien longtemps, ce monstre des profondeurs n'a jamais cesse de chercher un chemin vers l'ocean, brisant tout ce qui se trouve sur son passage. Il vient de decider que VOUS feriez un bon obstacle a eliminer." },
        Biome.Cave => new BossFamily { zoneName = "Chiot du Cerbere", villeName = "Cerbere", regionName = "Cerbere, Gardien des Enfers", color = new Color(0.15f, 0.1f, 0.1f), dropItemId = ItemIds.CerberusCollar, mask = CerbereMask,
            resourceDropIds = new[] { ItemIds.CerbereClaw, ItemIds.CerbereFang, ItemIds.CerbereFur },
            introKey = "cave", introDescription = "Ce chien des enfers s'est egare depuis que son maitre l'a laisse faire mumuse avec les ossements des enfers. Il semblerait bien qu'il soit ici maintenant, et qu'il en ait apres VOUS." },
        Biome.SkyCastle => new BossFamily { zoneName = "Aiglon Mecanique", villeName = "Aigle Royal Mecanique", regionName = "Rex Aquila, Seigneur des Cieux", color = new Color(0.75f, 0.7f, 0.55f), dropItemId = ItemIds.EagleCog, mask = AigleMask,
            resourceDropIds = new[] { ItemIds.AigleFeather, ItemIds.AigleTalon, ItemIds.AigleSensor },
            introKey = "skycastle", introDescription = "Construit par une civilisation disparue pour veiller sur les cieux, cet aigle mecanique patrouille encore ces ruines flottantes des siecles plus tard. Ses circuits viennent de designer une nouvelle cible, et il en a apres VOUS." },
        Biome.Backrooms => new BossFamily { zoneName = "Ombre Errante", villeName = "L'Arpenteur", regionName = "L'Arpenteur, Ancien des Couloirs", color = new Color(0.65f, 0.6f, 0.25f), dropItemId = ItemIds.WandererFragment, mask = ArpenteurMask,
            resourceDropIds = new[] { ItemIds.ArpenteurShadow, ItemIds.ArpenteurDust, ItemIds.ArpenteurEcho },
            introKey = "backrooms", introDescription = "Personne ne sait depuis combien de temps cette silhouette erre dans ces couloirs identiques, ni si elle a jamais ete humaine. Elle vient de s'arreter de marcher pour la premiere fois depuis des annees, et elle en a apres VOUS." },
        _ => new BossFamily { zoneName = "Chiot du Cerbere", villeName = "Cerbere", regionName = "Cerbere, Gardien des Enfers", color = new Color(0.15f, 0.1f, 0.1f), dropItemId = ItemIds.CerberusCollar, mask = CerbereMask,
            resourceDropIds = new[] { ItemIds.CerbereClaw, ItemIds.CerbereFang, ItemIds.CerbereFur },
            introKey = "cave", introDescription = "Ce chien des enfers s'est egare depuis que son maitre l'a laisse faire mumuse avec les ossements des enfers. Il semblerait bien qu'il soit ici maintenant, et qu'il en ait apres VOUS." },
    };

    // "quartier/ville/region" as spoken/written words (2026-09-21 request) - the boss's own
    // display name (BossNameFor) already differs per tier, this is just the category label the
    // intro title card/voice line appends after it.
    static string TierLabel(BossTier tier) => tier switch
    {
        BossTier.Zone => "quartier",
        BossTier.Ville => "ville",
        _ => "region",
    };

    static string BossNameFor(BossFamily family, BossTier tier) => tier switch
    {
        BossTier.Zone => family.zoneName,
        BossTier.Ville => family.villeName,
        _ => family.regionName,
    };

    // "un boss de zone facile drop 1/3 fois un item, un de ville difficile drop 1/2x un item et un
    // de region extremement difficile drop toujours un item" - stats scale with it too, since a
    // boss that's only harder to loot from but not to fight would be a strange difficulty curve.
    //
    // xpReward is floor-scaled (see RegionBossXpFor) instead of the old flat 6/10/16 - a flat
    // reward could never keep up with a curve where each floor needs several times the previous
    // floor's total XP. Ville/Zone keep the same 0.625/0.375 ratio to Region the old flat numbers
    // had (10/16, 6/16) - only Region's absolute size changed.
    // moveSpeed per tier is BaseSpeed * the player's own Vitesse formula (see
    // MonsterLeveling.BaseSpeed/PlayerStats.MoveSpeedMultiplierFor), same 2026-09-15 fix as regular
    // monsters - the old flat 1.3/1.5/1.8 put every boss well below the player's unbuffed walk
    // speed (5 u/s). Zone (-20 Vitesse, 3 u/s) and Ville (-10, 4 u/s) stay slower than the player
    // (a boss is meant to be out-run, not just out-fought, at the easier tiers); Region (0 Vitesse,
    // 5 u/s) ties the player's own base pace exactly, on top of which the Rapide modifier/enrage
    // still layer their existing multipliers unchanged.
    // health/contactDamage/volleyDamage re-rebalanced 2026-09-20 alongside every regular monster
    // (see BuildFloorAssetsPart1's assets.enemyPresets) - the 2026-09-15 pass below was already a
    // step in the right direction but still left a Region boss's 65 HP a trivial ~5 sword hits
    // against the now-205-total-HP player (see PlayerLimbs.BaseMaxFor) it's meant to threaten.
    // Bosses stay a single flat Health pool (no EnemyLimbs/limb layout) - they already carry their
    // own bespoke kit (fatigue stun, family abilities, modifiers), grafting per-limb targeting on
    // top wasn't asked for and would fight that existing design. shockwaveDamage/tentacleDamage/
    // rootDamage/poisonDamagePerTick (see SetupBossRoom) stay derived from contactDamage below, so
    // they keep scaling automatically.
    static BossTierStats BossTierStatsFor(BossTier tier, int floor)
    {
        int regionXp = RegionBossXpFor(floor);
        return tier switch
        {
            BossTier.Zone => new BossTierStats { health = 150, contactDamage = 15, chargeSpeed = 6f, volleyDamage = 6, volleyCount = 3, moveSpeed = MonsterLeveling.BaseSpeed * PlayerStats.MoveSpeedMultiplierFor(-20) * MonsterLeveling.MonsterSpeedScale, dropChance = 1f / 3f, xpReward = Mathf.Max(1, Mathf.RoundToInt(regionXp * 0.375f)) },
            BossTier.Ville => new BossTierStats { health = 260, contactDamage = 22, chargeSpeed = 8f, volleyDamage = 10, volleyCount = 5, moveSpeed = MonsterLeveling.BaseSpeed * PlayerStats.MoveSpeedMultiplierFor(-10) * MonsterLeveling.MonsterSpeedScale, dropChance = 0.5f, xpReward = Mathf.Max(1, Mathf.RoundToInt(regionXp * 0.625f)) },
            _ => new BossTierStats { health = 420, contactDamage = 32, chargeSpeed = 10f, volleyDamage = 15, volleyCount = 7, moveSpeed = MonsterLeveling.BaseSpeed * PlayerStats.MoveSpeedMultiplierFor(0) * MonsterLeveling.MonsterSpeedScale, dropChance = 1f, xpReward = regionXp },
        };
    }

    // A floor's "full clear" breakpoint level is 5*floor (see project_xp_monster_leveling_backlog:
    // 5->6 on floor 1, 10->11 on floor 2 - i.e. cap-without-Region is 5 on floor 1, 10 on floor 2).
    // The Region boss alone must give exactly enough XP to bridge from that cap to cap+1 - this is
    // the one piece of the spec that's both concrete AND mechanically exact (a single guaranteed
    // kill), unlike the "cap" itself which depends on how many regular monsters a procedurally
    // generated floor happens to contain and so can only be approximated (see the floor-scaled
    // regular monster xpReward in RoomController.SpawnEnemies).
    const int FloorLevelCap = 5;
    static int RegionBossXpFor(int floor)
    {
        int capLevel = FloorLevelCap * floor;
        return PlayerStats.CumulativeXpForLevel(capLevel + 1) - PlayerStats.CumulativeXpForLevel(capLevel);
    }

    // Every boss draws N distinct modifiers from the shared BossModifier pool - only the count
    // differs by tier (Zone 1, Ville 2, Region 3, per the backlog spec).
    static readonly BossModifier[] AllBossModifiers = (BossModifier[])System.Enum.GetValues(typeof(BossModifier));
    static List<BossModifier> RollBossModifiers(BossTier tier)
    {
        int count = tier switch { BossTier.Zone => 1, BossTier.Ville => 2, _ => 3 };
        List<BossModifier> pool = new List<BossModifier>(AllBossModifiers);
        List<BossModifier> picked = new List<BossModifier>(count);
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int index = Random.Range(0, pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return picked;
    }

    // Same icon pack StatusIconDisplay/EnemyController's Elite badges already draw from (see
    // LoadIconPackSprite) - Rapide reuses the exact SpeedUp icon for a consistent icon language.
    // Heart01 (outline) vs Heart02 (solid) tells Vampirique's life-drain apart from Colossal's flat
    // HP bump at a glance.
    static string IconNameFor(BossModifier modifier) => modifier switch
    {
        BossModifier.Rapide => "ThunderStrike_Bright",
        BossModifier.Colossal => "Heart02_Bright",
        BossModifier.Devastateur => "Sword_Bright",
        BossModifier.Rafale => "Bow_Bright",
        BossModifier.Blinde => "Shield_Bright",
        _ => "Heart01_Bright", // Vampirique
    };

    // Applies each rolled modifier's stat tweak directly to the just-configured boss/health, shows
    // a standing icon above the boss's head per modifier (same StatusIconDisplay every regular
    // enemy's Elite badge already uses - never expires, see ShowIcon's duration=-1 default, so it
    // just goes with the boss on death), and returns a short bracketed tag for the boss's display
    // name (see SetupBossRoom) as a second, text-only cue. See BossModifier.cs for what each one
    // does and why - deliberately kept to flat stat tweaks reusing existing fields, no new combat
    // hooks beyond Health.flatDamageReduction/BossController.lifestealFraction.
    static string ApplyBossModifiers(BossController boss, Health health, StatusIconDisplay statusIcons, List<BossModifier> modifiers)
    {
        if (modifiers.Count == 0) return "";

        string tags = "";
        foreach (BossModifier modifier in modifiers)
        {
            switch (modifier)
            {
                case BossModifier.Rapide:
                    boss.moveSpeed *= 1.3f;
                    boss.chargeSpeed *= 1.3f;
                    break;
                case BossModifier.Colossal:
                    health.maxHealth = Mathf.RoundToInt(health.maxHealth * 1.5f);
                    health.currentHealth = health.maxHealth;
                    boss.transform.localScale *= 1.15f;
                    break;
                case BossModifier.Devastateur:
                    boss.contactDamage += 1;
                    boss.volleyDamage += 1;
                    break;
                case BossModifier.Rafale:
                    boss.volleyProjectileCount += 2;
                    break;
                case BossModifier.Blinde:
                    health.flatDamageReduction += 1;
                    break;
                case BossModifier.Vampirique:
                    boss.lifestealFraction = 0.5f;
                    break;
            }
            tags += "[" + modifier + "] ";
            Sprite icon = LoadIconPackSprite(IconNameFor(modifier));
            if (icon != null) statusIcons.ShowIcon(modifier.ToString(), icon);
        }
        return tags;
    }

    static void SetupBossRoom(Vector2Int gridPos, List<Vector2Int> memberCells, int originX, int originY, Vector2 roomSize, Transform parent, Transform player,
        BossFamily family, BossTier tier, BossTierStats stats, Sprite bossProjectileSprite, Sprite bossSlobberPuddleSprite, Sprite doorBarrierSprite,
        List<(Vector2 pos, bool onVerticalWall)> doors, List<BossRoomController> controllers, bool startDefeated)
    {
        Vector2 roomOrigin = new Vector2(originX, originY);
        // Centered on the whole merged arena, not just the anchor cell, so a bigger Boss room
        // doesn't leave the boss sitting in a corner.
        Vector2 center = roomOrigin + roomSize / 2f;
        string bossName = BossNameFor(family, tier);

        // A fresh sprite per boss instance (cheap - see CreateMaskedSprite) rather than a shared
        // one baked once per floor: the family's own silhouette (snake/golem/eagle/...) at every
        // tier, darkened for the weak Zone version and lightened toward white for the imposing
        // Region one, on top of the size bump below - so Zone/Ville/Region read as visually
        // distinct without needing 3x as many hand-drawn silhouettes per family.
        Sprite bossSprite = CreateMaskedSprite("Assets/Art/Enemies/Boss_" + gridPos + ".png", family.mask, BossTierColorFor(family.color, tier));

        GameObject bossGO = new GameObject(bossName, typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(StatusIconDisplay), typeof(BossController));
        bossGO.transform.SetParent(parent);
        bossGO.transform.position = center;
        bossGO.transform.localScale = Vector3.one * (tier == BossTier.Region ? 2f : tier == BossTier.Ville ? 1.6f : 1.25f);

        // Taller than a regular enemy's badge height (0.7, see EnemyController) - a boss's own
        // transform scale (1.25-2x above) already stretches this world-space anyway, but the base
        // needs to clear the bigger sprite before that scaling even applies.
        StatusIconDisplay bossStatusIcons = bossGO.GetComponent<StatusIconDisplay>();
        bossStatusIcons.height = 1.1f;

        SpriteRenderer renderer = bossGO.GetComponent<SpriteRenderer>();
        renderer.sprite = bossSprite;
        renderer.sortingOrder = 0;
        bossGO.AddComponent<SpriteOutline>(); // same visibility fix as regular mobs, see RoomController.SpawnEnemies

        Rigidbody2D body = bossGO.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;

        bossGO.GetComponent<CircleCollider2D>().radius = 0.6f;

        Health health = bossGO.GetComponent<Health>();
        health.maxHealth = stats.health;
        health.currentHealth = health.maxHealth;

        BossController boss = bossGO.GetComponent<BossController>();
        boss.projectileSprite = bossProjectileSprite;
        // Loaded fresh here rather than threaded through FloorAssets/this method's own parameter
        // list - LoadIconPackSprite caches by name internally, so this costs nothing extra.
        boss.stunIconSprite = LoadIconPackSprite("Star01_Bright");
        boss.contactDamage = stats.contactDamage;
        boss.chargeSpeed = stats.chargeSpeed;
        boss.volleyDamage = stats.volleyDamage;
        boss.volleyProjectileCount = stats.volleyCount;
        boss.moveSpeed = stats.moveSpeed;
        // Family-specific ability damage (2026-09-15 rebalance) was a flat BossController default
        // regardless of tier before this - a Zone Golem's shockwave hit exactly as hard as a
        // Region Golem's. Derived from stats.contactDamage instead so every family's special
        // attack scales with tier automatically, same as contactDamage/volleyDamage just above.
        // Harmless to set on every boss even though only one kit ever reads each field.
        boss.shockwaveDamage = stats.contactDamage + 1;
        boss.tentacleDamage = stats.contactDamage + 1;
        boss.rootDamage = stats.contactDamage + 1;
        boss.poisonDamagePerTick = Mathf.Max(1, stats.contactDamage / 3);

        // Every family now has its own bespoke kit (see BossController.BossKit) instead of the
        // plain generic charge+volley pattern.
        switch (CurrentBiome)
        {
            case Biome.Cave:
                boss.kit = BossController.BossKit.Cerbere;
                boss.slobberPuddleSprite = bossSlobberPuddleSprite;
                break;
            case Biome.Jungle:
                boss.kit = BossController.BossKit.Anaconda;
                boss.slobberPuddleSprite = bossSlobberPuddleSprite;
                break;
            case Biome.Forest:
                boss.kit = BossController.BossKit.Ent;
                break;
            case Biome.City:
                boss.kit = BossController.BossKit.Golem;
                break;
            case Biome.Beach:
                boss.kit = BossController.BossKit.Kraken;
                break;
            case Biome.SkyCastle:
                boss.kit = BossController.BossKit.Aigle;
                break;
            case Biome.Backrooms:
                boss.kit = BossController.BossKit.Arpenteur;
                break;
        }

        List<BossModifier> modifiers = RollBossModifiers(tier);
        string modifierTags = ApplyBossModifiers(boss, health, bossStatusIcons, modifiers);
        string taggedBossName = modifierTags + bossName;
        if (modifiers.Count > 0) Debug.Log(taggedBossName + ": modificateurs " + string.Join(", ", modifiers));

        GameObject roomGO = new GameObject("BossRoom_" + gridPos, typeof(BossRoomController));
        roomGO.transform.SetParent(parent);

        BossRoomController controller = roomGO.GetComponent<BossRoomController>();
        controller.gridPos = gridPos;
        controller.memberCells = memberCells.ToArray();
        controller.boss = boss;
        controller.player = player;
        controller.roomOrigin = roomOrigin;
        controller.roomSize = roomSize;
        controller.startDefeated = startDefeated;
        controller.bossName = taggedBossName;
        // Plain (untagged) name + lore for the intro cutscene (2026-09-21 request) - modifier tags
        // like "[Colossal]" belong on the health bar/corpse name, not spoken/written in the title
        // card.
        controller.introName = bossName;
        controller.tierLabel = TierLabel(tier);
        controller.introDescription = family.introDescription;
        controller.introVoiceKey = family.introKey + "_" + tier.ToString().ToLowerInvariant();
        controller.dropItemId = family.dropItemId;
        controller.resourceDropIds = family.resourceDropIds;
        controller.dropChance = stats.dropChance;
        controller.xpReward = stats.xpReward;
        controller.tier = tier; // QuestNpc's boss-kill quests need to know which tier died (2026-09-21)
        // See the matching subscription in SetupMonsterRoom - keeps DungeonGenerator's live
        // tracking accurate even for a boss restored as already-dead (Start() re-fires this, see
        // BossRoomController), so a later re-save still reflects it.
        controller.OnBossDefeated += () => bossDefeatedThisFloor = true;

        foreach ((Vector2 pos, bool onVerticalWall) door in doors)
        {
            GameObject blocker = SpawnDoorBlocker(door.pos, door.onVerticalWall, doorBarrierSprite, roomGO.transform);
            controller.doorBlockers.Add(blocker);
        }

        controllers.Add(controller);
    }

    // Darker/duller for the weak Zone tier, the family's plain color for Ville, lightened toward
    // white for the imposing Region tier - keeps alpha untouched (a plain Color * float would also
    // scale it, silently making Zone bosses partly transparent).
    static Color BossTierColorFor(Color baseColor, BossTier tier) => tier switch
    {
        BossTier.Zone => new Color(baseColor.r * 0.7f, baseColor.g * 0.7f, baseColor.b * 0.7f, baseColor.a),
        BossTier.Region => Color.Lerp(baseColor, Color.white, 0.35f),
        _ => baseColor,
    };

    static GameObject SpawnDoorBlocker(Vector2 center, bool onVerticalWall, Sprite sprite, Transform parent)
    {
        GameObject blocker = new GameObject("DoorBlocker", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(DoorBlocker));
        blocker.layer = BlockingLayer; // a flying enemy passes through (see BlockingLayer/FlyingLayer)
        blocker.transform.SetParent(parent);
        blocker.transform.position = center;
        blocker.transform.localScale = onVerticalWall ? new Vector3(1f, DoorWidth, 1f) : new Vector3(DoorWidth, 1f, 1f);

        SpriteRenderer renderer = blocker.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        blocker.GetComponent<BoxCollider2D>().size = Vector2.one;

        return blocker;
    }

    static Sprite CreateSolidSprite(string path, Color color)
    {
        Texture2D tex = new Texture2D(TilePixelSize, TilePixelSize, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[TilePixelSize * TilePixelSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    // Average brightness of the Kenney stone tiles sliced by KenneyDungeonTileSlicer (measured by
    // sampling them directly) - lets TintFor reproduce each biome's intended flat color on average
    // while the tile's own pixels (mortar lines, subtle noise) still show through as real texture,
    // instead of guessing a tint and hoping it looks right.
    static readonly Color FloorTileAvg = new Color(0.605f, 0.655f, 0.659f);
    static readonly Color WallTileAvg = new Color(0.579f, 0.626f, 0.630f);

    static Color TintFor(Color desired, Color textureAvg)
    {
        return new Color(
            desired.r / Mathf.Max(textureAvg.r, 0.05f),
            desired.g / Mathf.Max(textureAvg.g, 0.05f),
            desired.b / Mathf.Max(textureAvg.b, 0.05f),
            1f);
    }

    // Alpha forced to 1 - a couple of pixels in the source tile (a highlight/damage notch baked
    // into the art) are partially transparent, which would otherwise punch stray see-through
    // pixels into what's meant to be a solid architectural surface.
    static Color SampleTile(Sprite tile, int x, int y)
    {
        Rect r = tile.rect;
        Color c = tile.texture.GetPixel((int)r.x + x, (int)r.y + y);
        c.a = 1f;
        return c;
    }

    // Real Kenney stone texture (see KenneyDungeonTileSlicer) tinted to the biome's floor color
    // (see TintFor) instead of a single flat-filled pixel - falls back to the old flat fill if the
    // slice/bake step was never run, same "never means an invisible tile" pattern as PlayerHero.
    static Sprite CreateTexturedFloorSprite(string path, Color desiredColor)
    {
        Sprite tile = LoadIconPackSprite("DungeonFloor");
        if (tile == null) return CreateSolidSprite(path, desiredColor);

        Color tint = TintFor(desiredColor, FloorTileAvg);
        Texture2D tex = new Texture2D(TilePixelSize, TilePixelSize, TextureFormat.RGBA32, false);
        for (int y = 0; y < TilePixelSize; y++)
            for (int x = 0; x < TilePixelSize; x++)
                tex.SetPixel(x, y, SampleTile(tile, x, y) * tint);
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    static Sprite CreateWallSprite(string path, Color faceColor, Color topColor, Color edgeHighlight)
    {
        Sprite faceTile = LoadIconPackSprite("DungeonWallFace");
        Sprite topTile = LoadIconPackSprite("DungeonFloor"); // wall's top cap, seen from above - a stone floor tile reads fine for this
        Color faceTint = TintFor(faceColor, WallTileAvg);
        Color topTint = TintFor(topColor, FloorTileAvg);

        int height = TilePixelSize + WallExtraHeight;
        Texture2D tex = new Texture2D(TilePixelSize, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < TilePixelSize; x++)
            {
                Color rowColor;
                if (y >= height - 1) rowColor = edgeHighlight;    // 1px lit edge along the very top
                else if (y >= TilePixelSize)                       // raised top face, seen from above
                    rowColor = topTile != null ? SampleTile(topTile, x, y - TilePixelSize) * topTint : topColor;
                else                                                // shadowed front face, at floor level
                    rowColor = faceTile != null ? SampleTile(faceTile, x, y) * faceTint : faceColor;

                tex.SetPixel(x, y, rowColor);
            }
        }
        tex.Apply();
        // Pivot at the bottom so the extra height pokes upward out of the tile's own cell.
        return SaveTextureAsSprite(tex, path, new Vector2(0.5f, 0f));
    }

    static Sprite CreateRectSprite(string path, Color color)
    {
        // Narrow and tall: rotated to face the aim direction, this reads as a short slash/swing.
        int width = TilePixelSize / 2;
        int height = TilePixelSize;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    static Sprite CreateCircleSprite(string path, Color color)
    {
        Texture2D tex = new Texture2D(TilePixelSize, TilePixelSize, TextureFormat.RGBA32, false);
        Color clear = new Color(0f, 0f, 0f, 0f);
        float radius = TilePixelSize / 2f - 1f;
        Vector2 center = new Vector2(TilePixelSize / 2f, TilePixelSize / 2f);
        for (int x = 0; x < TilePixelSize; x++)
        {
            for (int y = 0; y < TilePixelSize; y++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                tex.SetPixel(x, y, dist <= radius ? color : clear);
            }
        }
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    // A soft white radial falloff (opaque center, transparent edge) meant to be tinted via
    // SpriteRenderer.color - one shared glow sprite recolored per elite modifier.
    static Sprite CreateGlowSprite(string path)
    {
        int size = TilePixelSize * 2;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(1f - dist / radius);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
            }
        }
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    // Single-color version of CreateHeartSprite's mask-to-texture approach, for arbitrary icon shapes.
    static Sprite CreateMaskedSprite(string path, string[] mask, Color color)
    {
        int w = mask[0].Length;
        int h = mask.Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < h; y++)
        {
            string row = mask[h - 1 - y]; // texture row 0 is the bottom; mask row 0 is the visual top
            for (int x = 0; x < w; x++)
            {
                tex.SetPixel(x, y, row[x] == 'X' ? color : clear);
            }
        }
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    // A hollow square (solid border, transparent center) drawn plain white so the minimap can tint
    // it to any room-type outline color via Image.color instead of baking a separate texture per color.
    static Sprite CreateRingSprite(string path, int size, int thickness, Color color)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                bool isBorder = x < thickness || x >= size - thickness || y < thickness || y >= size - thickness;
                tex.SetPixel(x, y, isBorder ? color : clear);
            }
        }
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    static Dictionary<string, Sprite> iconPackCache;

    // Looks up a named sub-sprite (e.g. "Heart02_Bright") from IconPackData, a small
    // ScriptableObject baked once in the Editor (Dungeon/Rebuild Icon Pack Data,
    // Assets/Editor/DungeonBootstrap.cs) from the imported "Modern GDR - Free icons pack" atlas -
    // that bake step needs AssetDatabase (Editor-only), but the lookup here is a plain
    // Resources.Load, so it works identically in the Editor and in a build. Bright (white
    // silhouette) rather than Dark (black) specifically because white is what lets Image.color
    // actually tint it - black multiplied by any tint is still black.
    static Sprite LoadIconPackSprite(string name)
    {
        if (iconPackCache == null)
        {
            iconPackCache = new Dictionary<string, Sprite>();
            IconPackData data = Resources.Load<IconPackData>("IconPackData");
            if (data != null)
            {
                foreach (IconPackData.Entry entry in data.entries) iconPackCache[entry.name] = entry.sprite;
            }
            else
            {
                Debug.LogWarning("DungeonGenerator: Resources/IconPackData.asset not found - run Dungeon/Rebuild Icon Pack Data in the Editor first.");
            }
        }
        if (!iconPackCache.TryGetValue(name, out Sprite result))
            Debug.LogWarning("DungeonGenerator: icon pack sprite '" + name + "' not found.");
        return result;
    }

    // `name` used to be a real file path when this baked a PNG asset to disk - kept only as a
    // debug label now that the sprite lives purely in memory (Editor and build alike), so none of
    // the ~80 call sites needed to change.
    static Sprite SaveTextureAsSprite(Texture2D tex, string name, Vector2? pivot = null)
    {
        tex.filterMode = FilterMode.Point;
        tex.name = name;
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), pivot ?? new Vector2(0.5f, 0.5f), TilePixelSize);
        sprite.name = name;
        return sprite;
    }

    static Tile CreateTileAsset(string name, Sprite sprite, Tile.ColliderType colliderType)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = name;
        tile.sprite = sprite;
        tile.colliderType = colliderType;
        return tile;
    }
}
