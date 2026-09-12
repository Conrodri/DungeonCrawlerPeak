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
    enum RoomType { Start, Empty, Monster, Shop, Treasure, Secret, Gamble, Boss, Event, Safe }

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
    const int TargetNormalRooms = 8; // includes the Start room
    const float EliteChance = 0.05f;

    const int TilePixelSize = 16;
    const int WallExtraHeight = 8;

    // Spawn placement tuning (see GenerateEnemySpawnPositions): distance kept from walls, doors,
    // and other spawned enemies.
    const float EnemySpawnWallMargin = 3f;
    const float EnemySpawnMinDoorDistance = 3f;
    const float EnemySpawnMinSpacing = 3f;
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

    // The seed the currently-loaded floor was built with - a Safe room's "rest" option saves this
    // alongside player state, so DungeonGenerator.Build(CurrentSeed) recreates the exact same floor.
    public static int CurrentSeed { get; private set; }

    public static void Build(int seed)
    {
        CurrentSeed = seed;
        Random.InitState(seed);

        Sprite floorSprite = CreateSolidSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", new Color(0.10f, 0.09f, 0.11f), new Color(0.34f, 0.31f, 0.36f), new Color(0.55f, 0.52f, 0.58f));
        Sprite playerSprite = CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));
        Sprite zombieSprite = CreateCircleSprite("Assets/Art/Enemies/Zombie.png", new Color(0.25f, 0.4f, 0.2f));
        Sprite chauveSourisSprite = CreateCircleSprite("Assets/Art/Enemies/ChauveSouris.png", new Color(0.3f, 0.15f, 0.35f));
        Sprite larveSprite = CreateCircleSprite("Assets/Art/Enemies/Larve.png", new Color(0.8f, 0.85f, 0.5f));
        Sprite cerberusSprite = CreateCircleSprite("Assets/Art/Enemies/Cerberus.png", new Color(0.15f, 0.1f, 0.1f));
        Sprite bossProjectileSprite = CreateCircleSprite("Assets/Art/Fx/BossProjectile.png", new Color(0.85f, 0.2f, 0.15f));
        Sprite stoneBlockSprite = CreateSolidSprite("Assets/Art/Decor/StoneBlock.png", new Color(0.42f, 0.4f, 0.38f));
        Sprite woodDebrisSprite = CreateSolidSprite("Assets/Art/Decor/WoodDebris.png", new Color(0.55f, 0.4f, 0.25f));
        Sprite metalDebrisSprite = CreateSolidSprite("Assets/Art/Decor/MetalDebris.png", new Color(0.5f, 0.53f, 0.58f));
        Sprite barrelSprite = CreateSolidSprite("Assets/Art/Decor/Barrel.png", new Color(0.5f, 0.25f, 0.15f));
        Sprite fuelPuddleSprite = CreateCircleSprite("Assets/Art/Decor/FuelPuddle.png", new Color(0.12f, 0.1f, 0.08f));
        Sprite floorTrapSprite = CreateCircleSprite("Assets/Art/Decor/FloorTrap.png", new Color(0.2f, 0.08f, 0.08f));
        DecorSprites decorSprites = new DecorSprites
        {
            stoneBlock = stoneBlockSprite,
            woodDebris = woodDebrisSprite,
            metalDebris = metalDebrisSprite,
            barrel = barrelSprite,
            fuelPuddle = fuelPuddleSprite,
            floorTrap = floorTrapSprite,
        };
        Sprite enemyGlowSprite = CreateGlowSprite("Assets/Art/Fx/EnemyGlow.png");
        Sprite speedUpBadge = LoadIconPackSprite("ThunderStrike_Bright");
        Sprite hpUpBadge = LoadIconPackSprite("Heart02_Bright");

        RoomController.EnemyPresetEntry[] enemyPresets =
        {
            new RoomController.EnemyPresetEntry { type = EnemyType.Zombie, sprite = zombieSprite, moveSpeed = 1.2f, maxHealth = 4, contactDamage = 1, isFlying = false },
            new RoomController.EnemyPresetEntry { type = EnemyType.ChauveSouris, sprite = chauveSourisSprite, moveSpeed = 3.2f, maxHealth = 1, contactDamage = 1, isFlying = true },
            new RoomController.EnemyPresetEntry { type = EnemyType.Larve, sprite = larveSprite, moveSpeed = 1.6f, maxHealth = 1, contactDamage = 1, isFlying = false },
        };

        // A whole floor sometimes commits to a single creature type, so every Monster room draws
        // from it instead of picking its own encounter pattern.
        EnemyType? floorTheme = Random.value < 0.3f ? (EnemyType?)(EnemyType)Random.Range(0, 3) : null;

        Sprite shopMarker = CreateMaskedSprite("Assets/Art/Markers/Shop.png", ChestMask, new Color(0.75f, 0.55f, 0.15f));
        Sprite treasureMarker = CreateSolidSprite("Assets/Art/Markers/Treasure.png", new Color(0.85f, 0.7f, 0.2f));
        Sprite secretMarker = CreateMaskedSprite("Assets/Art/Markers/Secret.png", QuestionMask, new Color(0.85f, 0.85f, 0.9f));
        Sprite gambleMarker = CreateSolidSprite("Assets/Art/Markers/Gamble.png", new Color(0.85f, 0.35f, 0.15f));
        Sprite bossMarker = CreateMaskedSprite("Assets/Art/Markers/Boss.png", SkullMask, new Color(0.9f, 0.9f, 0.92f));
        Sprite eventMarker = CreateMaskedSprite("Assets/Art/Markers/Event.png", ExclamationMask, new Color(0.55f, 0.25f, 0.85f));
        Sprite safeMarker = LoadIconPackSprite("Shield_Bright");
        Sprite craftingTableSprite = CreateSolidSprite("Assets/Art/Decor/CraftingTable.png", new Color(0.45f, 0.32f, 0.2f));

        Sprite projectileSprite = CreateCircleSprite("Assets/Art/Projectile.png", new Color(0.6f, 0.85f, 0.95f));
        Sprite swordPickupSprite = CreateSolidSprite("Assets/Art/Items/Sword.png", new Color(0.75f, 0.78f, 0.82f));
        Sprite staffPickupSprite = CreateSolidSprite("Assets/Art/Items/Staff.png", new Color(0.5f, 0.25f, 0.65f));
        Sprite goldSprite = LoadIconPackSprite("Coin_Bright");
        Sprite shurikenSprite = CreateSolidSprite("Assets/Art/Items/Shuriken.png", new Color(0.6f, 0.6f, 0.65f));
        Sprite caillouSprite = CreateCircleSprite("Assets/Art/Items/Caillou.png", new Color(0.45f, 0.42f, 0.4f));
        Sprite batonSprite = CreateSolidSprite("Assets/Art/Items/Baton.png", new Color(0.5f, 0.35f, 0.2f));

        Sprite fistVisualSprite = CreateCircleSprite("Assets/Art/Fx/FistHit.png", new Color(0.95f, 0.95f, 0.9f));
        Sprite swordVisualSprite = CreateRectSprite("Assets/Art/Fx/SwordSlash.png", new Color(0.85f, 0.9f, 0.95f));

        Sprite bombSprite = CreateCircleSprite("Assets/Art/Items/Bomb.png", new Color(0.15f, 0.15f, 0.17f));
        Sprite explosionSprite = CreateCircleSprite("Assets/Art/Fx/Explosion.png", new Color(0.95f, 0.55f, 0.15f));
        decorSprites.explosion = explosionSprite;

        // Registered immediately (usable right away by SpawnItemPickup etc. below) and also baked
        // into an ItemCatalog placed in the scene, so the same data re-registers itself into
        // ItemDatabase when the game is actually played later, without DungeonBootstrap running.
        ItemDatabase.Clear();
        var itemEntries = new List<ItemCatalog.Entry>();
        RegisterItem(itemEntries, ItemIds.Gold, "Or", ItemCategory.Currency, 100, goldSprite);
        RegisterItem(itemEntries, ItemIds.Shuriken, "Shuriken", ItemCategory.Throwable, 10, shurikenSprite);
        RegisterItem(itemEntries, ItemIds.Caillou, "Caillou", ItemCategory.Throwable, 10, caillouSprite);
        RegisterItem(itemEntries, ItemIds.Baton, "Baton", ItemCategory.Throwable, 10, batonSprite);
        RegisterItem(itemEntries, ItemIds.Bomb, "Bombe", ItemCategory.Throwable, 10, bombSprite);

        // Special ground-only items showcasing weight/curse/trap - placed rarely by SpawnRoomDecor,
        // never in the common LootTable drop pool.
        Sprite anvilSprite = CreateSolidSprite("Assets/Art/Items/Anvil.png", new Color(0.2f, 0.2f, 0.22f));
        Sprite cursedSwordSprite = CreateSolidSprite("Assets/Art/Items/CursedSword.png", new Color(0.35f, 0.1f, 0.4f));
        Sprite trapSackSprite = CreateSolidSprite("Assets/Art/Items/TrapSack.png", new Color(0.5f, 0.4f, 0.3f));
        RegisterItem(itemEntries, ItemIds.Anvil, "Enclume", ItemCategory.Misc, 1, anvilSprite,
            "Une lourde enclume de forgeron.", weight: 5);
        RegisterItem(itemEntries, ItemIds.CursedSword, "Epee Maudite", ItemCategory.Weapon, 1, cursedSwordSprite,
            "Une lame ancienne. Une presence malveillante semble y sommeiller.",
            isCursed: true, hasCursedWeapon: true, cursedWeaponType: PlayerController.WeaponType.Sword);
        RegisterItem(itemEntries, ItemIds.TrapSack, "Sac Abandonne", ItemCategory.Misc, 1, trapSackSprite,
            "Un petit sac abandonne. Qui l'aurait laisse la ?", isTrap: true);

        Sprite potionSprite = CreateCircleSprite("Assets/Art/Items/Potion.png", new Color(0.8f, 0.15f, 0.35f));
        RegisterItem(itemEntries, ItemIds.HealthPotion, "Potion de Soin", ItemCategory.Misc, 5, potionSprite,
            "Restaure un peu de vie.", healAmount: 3);

        Sprite cerberusCollarSprite = CreateSolidSprite("Assets/Art/Items/CerberusCollar.png", new Color(0.75f, 0.6f, 0.15f));
        RegisterItem(itemEntries, ItemIds.CerberusCollar, "Collier Infernal du Cerbere", ItemCategory.Misc, 1, cerberusCollarSprite,
            "Un collier de bronze encore chaud, arrache au Cerbere. Un trophee de votre victoire.");

        // Crafting materials - guaranteed drops from the matching decor material (see
        // SpawnRoomDecor/DestructibleObject.guaranteedDropItemId), spent at the Safe room's
        // crafting table (SpawnCraftingTable).
        Sprite woodMaterialSprite = CreateSolidSprite("Assets/Art/Items/Wood.png", new Color(0.55f, 0.4f, 0.25f));
        Sprite metalMaterialSprite = CreateSolidSprite("Assets/Art/Items/Metal.png", new Color(0.5f, 0.53f, 0.58f));
        Sprite stoneMaterialSprite = CreateSolidSprite("Assets/Art/Items/Stone.png", new Color(0.42f, 0.4f, 0.38f));
        RegisterItem(itemEntries, ItemIds.Wood, "Bois", ItemCategory.Misc, 20, woodMaterialSprite, "Du bois recupere sur des debris.");
        RegisterItem(itemEntries, ItemIds.Metal, "Metal", ItemCategory.Misc, 20, metalMaterialSprite, "Du metal recupere sur des debris.");
        RegisterItem(itemEntries, ItemIds.Stone, "Pierre", ItemCategory.Misc, 20, stoneMaterialSprite, "De la pierre recuperee sur un bloc.");

        Sprite doorBarrierSprite = CreateSolidSprite("Assets/Art/Fx/DoorBarrier.png", new Color(0.6f, 0.15f, 0.15f));
        // Same face color as the wall itself, so a secret room's bombable wall blends in - no
        // visual hint, on purpose (detection items are a separate future feature).
        Sprite secretWallSprite = CreateSolidSprite("Assets/Art/Fx/SecretWall.png", new Color(0.10f, 0.09f, 0.11f));
        Sprite outlineRingSprite = CreateRingSprite("Assets/Art/Markers/OutlineRing.png", TilePixelSize, 2, Color.white);
        Sprite npcSprite = CreateCircleSprite("Assets/Art/Npc.png", new Color(0.35f, 0.55f, 0.75f));
        // Ground items are flat colored circles/squares too (no dedicated art beyond a handful of
        // icon-pack sprites) - without a marker, an NPC reads as just another item on the floor.
        // A floating "!" above the head (same convention as EnemyController's elite badges) fixes
        // that at a glance without needing new art.
        Sprite npcBadgeSprite = LoadIconPackSprite("Quest01_Bright");

        // The pack only has two heart styles (solid + outline), no dedicated half/empty art, so a
        // third state is faked by re-using the outline shape at a dimmer color for "empty".
        Color heartRed = new Color(0.85f, 0.15f, 0.2f);
        Color heartEmpty = new Color(0.4f, 0.38f, 0.4f);
        Sprite fullHeart = LoadIconPackSprite("Heart02_Bright");
        Sprite halfHeart = LoadIconPackSprite("Heart01_Bright");
        Sprite emptyHeart = LoadIconPackSprite("Heart01_Bright");

        Sprite forceIcon = LoadIconPackSprite("Sword_Bright");
        Sprite agiliteIcon = LoadIconPackSprite("Bow_Bright");
        Sprite intelligenceIcon = LoadIconPackSprite("Gear01_Bright");
        Sprite vitesseIcon = LoadIconPackSprite("Thunder_Bright");
        Sprite charismeIcon = LoadIconPackSprite("Star01_Bright");
        Sprite enduranceIcon = LoadIconPackSprite("Watch_Bright");

        Tile floorTile = CreateTileAsset("Assets/Art/Tiles/FloorTile.asset", floorSprite, Tile.ColliderType.None);
        Tile wallTile = CreateTileAsset("Assets/Art/Tiles/WallTile.asset", wallSprite, Tile.ColliderType.Grid);

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

        Dictionary<Vector2Int, RoomType> layout = GenerateLayout(out Dictionary<Vector2Int, RectInt> cellGroups);

        // Carve the room geometry and cut door openings for every adjacent pair.
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            BuildRoomGeometry(kv.Key.x * StepX, kv.Key.y * StepY, floorMap, wallsMap, floorTile, wallTile);
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
                    // Two cells of the same merged room - no door, just one continuous floor.
                    OpenFullHorizontalSeam(cell.x * StepX, rightCell.x * StepX, cell.y * StepY, floorMap, wallsMap, floorTile, wallTile);
                }
                else
                {
                    bool leftIsSecret = kv.Value == RoomType.Secret;
                    bool rightIsSecret = layout[rightCell] == RoomType.Secret;
                    // A secret wall always sits at the dead center of the wall - the wall itself
                    // gives no visual hint either way, but at least a player who suspects a given
                    // wall only needs to bomb the one predictable spot instead of the whole length of it.
                    int doorY = (leftIsSecret || rightIsSecret ? CenteredDoorOffset(RoomHeight) : RandomDoorOffset(RoomHeight)) + cell.y * StepY;
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
                    OpenFullVerticalSeam(cell.y * StepY, upCell.y * StepY, cell.x * StepX, floorMap, wallsMap, floorTile, wallTile);
                }
                else
                {
                    bool bottomIsSecret = kv.Value == RoomType.Secret;
                    bool topIsSecret = layout[upCell] == RoomType.Secret;
                    int doorX = (bottomIsSecret || topIsSecret ? CenteredDoorOffset(RoomWidth) : RandomDoorOffset(RoomWidth)) + cell.x * StepX;
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

        // --- Player ---
        Vector2Int startCell = Vector2Int.zero;
        Vector2 startWorld = new Vector2(startCell.x * StepX + RoomWidth / 2f, startCell.y * StepY + RoomHeight / 2f);

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(Stamina), typeof(PlayerInventory), typeof(PlayerStats), typeof(PlayerController));
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
        PlayerInventory playerInventory = player.GetComponent<PlayerInventory>();
        // Starting hotbar loadout - the player can rearrange these later via drag & drop.
        playerInventory.hotbarSlots[0] = ItemIds.Shuriken;
        playerInventory.hotbarSlots[1] = ItemIds.Caillou;
        playerInventory.hotbarSlots[2] = ItemIds.Baton;
        playerInventory.hotbarSlots[3] = ItemIds.Bomb;
        PlayerStats playerStats = player.GetComponent<PlayerStats>();
        Health playerHealth = player.GetComponent<Health>();
        Stamina playerStamina = player.GetComponent<Stamina>();
        // Health is tracked in half-heart units: 3 hearts = 6 units. Normal hits cost 1 (half a
        // heart), elite hits cost 2 (a full heart).
        playerHealth.maxHealth = 6;
        playerHealth.currentHealth = playerHealth.maxHealth;

        // --- Room content (enemies / special-room markers) ---
        var roomEntries = new List<RoomCameraController.RoomEntry>();
        var monsterRoomControllers = new List<RoomController>();
        var bossRoomControllers = new List<BossRoomController>();
        int eliteRoomCount = 0;
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            int originX = kv.Key.x * StepX;
            int originY = kv.Key.y * StepY;
            roomEntries.Add(new RoomCameraController.RoomEntry { gridPos = kv.Key, rect = new Rect(originX, originY, RoomWidth, RoomHeight) });

            if (kv.Value == RoomType.Monster)
            {
                // A multi-cell room is only ever set up once, from its anchor (top/bottom-left)
                // cell - the other member cells contribute nothing here beyond the camera-rect
                // entry already added above, which is what lets the camera scroll across them.
                if (!IsGroupAnchor(kv.Key, cellGroups)) continue;

                RectInt group = cellGroups[kv.Key];
                var memberCells = new List<Vector2Int>();
                var doors = new List<(Vector2 pos, bool onVerticalWall)>();
                for (int gx = 0; gx < group.width; gx++)
                {
                    for (int gy = 0; gy < group.height; gy++)
                    {
                        Vector2Int member = new Vector2Int(group.xMin + gx, group.yMin + gy);
                        memberCells.Add(member);
                        if (doorsByRoom.TryGetValue(member, out var memberDoors)) doors.AddRange(memberDoors);
                    }
                }
                Vector2 groupSize = new Vector2(
                    group.width * RoomWidth + (group.width - 1) * Gap,
                    group.height * RoomHeight + (group.height - 1) * Gap);

                bool hasElite = SetupMonsterRoom(memberCells, originX, originY, groupSize, root.transform, player.transform,
                    enemyPresets, speedUpBadge, hpUpBadge, enemyGlowSprite, doorBarrierSprite, decorSprites, floorTheme, doors, monsterRoomControllers);
                if (hasElite) eliteRoomCount++;
            }
            else if (kv.Value == RoomType.Boss)
            {
                List<(Vector2 pos, bool onVerticalWall)> doors = doorsByRoom.TryGetValue(kv.Key, out var bd) ? bd : new List<(Vector2, bool)>();
                PopulateRoom(kv.Value, originX, originY, root.transform, player.transform,
                    shopMarker, treasureMarker, secretMarker, gambleMarker, bossMarker, eventMarker, safeMarker,
                    swordPickupSprite, staffPickupSprite);
                SetupBossRoom(kv.Key, originX, originY, root.transform, player.transform,
                    cerberusSprite, bossProjectileSprite, doorBarrierSprite, doors, bossRoomControllers);
            }
            else
            {
                PopulateRoom(kv.Value, originX, originY, root.transform, player.transform,
                    shopMarker, treasureMarker, secretMarker, gambleMarker, bossMarker, eventMarker, safeMarker,
                    swordPickupSprite, staffPickupSprite);

                if (kv.Value == RoomType.Event)
                {
                    Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    SpawnExampleNpc(center + new Vector2(2f, 0f), npcSprite, npcBadgeSprite, root.transform);
                }

                if (kv.Value == RoomType.Safe)
                {
                    Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    SpawnTavernNpc(center + new Vector2(2f, 0f), npcSprite, npcBadgeSprite, root.transform);
                    SpawnCraftingTable(center + new Vector2(-2f, 0f), craftingTableSprite, npcBadgeSprite, root.transform);
                }

                if (kv.Value == RoomType.Shop)
                {
                    Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    SpawnMerchantNpc(center, npcSprite, npcBadgeSprite, root.transform);
                }

                // Stone blocks everywhere except Start (no obstacles blocking the initial pickups),
                // Boss (kept clear for the fight), Safe and Shop (both guaranteed hazard-free by design).
                if (kv.Value != RoomType.Start && kv.Value != RoomType.Boss && kv.Value != RoomType.Safe && kv.Value != RoomType.Shop)
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
            CreateDoorLink(link.posA, link.inwardA, triggersA, link.posB, link.inwardB, triggersB, root.transform);
        }

        // --- Camera: locked per-room instead of following the player continuously ---
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            // Matches the room's own height exactly, so it fills the screen vertically with no
            // letterboxing (a 22-wide room then slightly overscans a 16:9 view horizontally).
            cam.orthographicSize = RoomHeight / 2f;
            cam.transform.position = new Vector3(startWorld.x, startWorld.y, cam.transform.position.z);

            RoomCameraController roomCam = cam.GetComponent<RoomCameraController>();
            if (roomCam == null) roomCam = cam.gameObject.AddComponent<RoomCameraController>();
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

        // Required for UI pointer/drag events (inventory drag & drop) - the project's Active Input
        // Handling is Input System (New) only, so the legacy StandaloneInputModule doesn't work.
        // Reuses one if it already exists (MainMenuController creates one to run its own buttons
        // before a floor exists at all) - a second EventSystem in the scene fights the first one.
        GameObject eventSystemGO = GameObject.Find("EventSystem");
        if (eventSystemGO == null) eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemGO.transform.SetParent(root.transform);

        GameObject hudGO = new GameObject("HeartHUD", typeof(RectTransform), typeof(HeartHUD));
        hudGO.transform.SetParent(canvasGO.transform, false);
        RectTransform hudRect = hudGO.GetComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        HeartHUD hud = hudGO.GetComponent<HeartHUD>();
        hud.target = playerHealth;
        hud.fullHeart = fullHeart;
        hud.halfHeart = halfHeart;
        hud.emptyHeart = emptyHeart;
        hud.fullColor = heartRed;
        hud.halfColor = heartRed;
        hud.emptyColor = heartEmpty;
        hud.maxHeartSlots = playerHealth.maxHealth / 2;

        // --- Stamina bar (directly under the hearts, always visible) ---
        GameObject staminaBarGO = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(StaminaBarUI));
        staminaBarGO.transform.SetParent(canvasGO.transform, false);
        Image staminaBarBackground = staminaBarGO.GetComponent<Image>();
        staminaBarBackground.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        RectTransform staminaBarRect = staminaBarBackground.rectTransform;
        staminaBarRect.anchorMin = staminaBarRect.anchorMax = new Vector2(0f, 1f);
        staminaBarRect.pivot = new Vector2(0f, 1f);
        staminaBarRect.anchoredPosition = new Vector2(20f, -58f);
        staminaBarRect.sizeDelta = new Vector2(160f, 16f);

        GameObject staminaFillGO = new GameObject("Fill", typeof(Image));
        staminaFillGO.transform.SetParent(staminaBarGO.transform, false);
        Image staminaFill = staminaFillGO.GetComponent<Image>();
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
        staminaLabelRect.anchoredPosition = new Vector2(188f, -58f);
        staminaLabelRect.sizeDelta = new Vector2(80f, 16f);

        StaminaBarUI staminaBar = staminaBarGO.GetComponent<StaminaBarUI>();
        staminaBar.target = playerStamina;
        staminaBar.fill = staminaFill;
        staminaBar.label = staminaLabel;

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
        goldCounter.yOffset = -82f; // leaves room for the stamina bar sitting just under the hearts

        // --- Stats column (below the gold counter) ---
        GameObject statsGO = new GameObject("StatsUI", typeof(RectTransform), typeof(StatsUI));
        statsGO.transform.SetParent(canvasGO.transform, false);
        RectTransform statsRect = statsGO.GetComponent<RectTransform>();
        statsRect.anchorMin = Vector2.zero;
        statsRect.anchorMax = Vector2.one;
        statsRect.offsetMin = Vector2.zero;
        statsRect.offsetMax = Vector2.zero;

        StatsUI statsUI = statsGO.GetComponent<StatsUI>();
        statsUI.stats = playerStats;
        statsUI.constitutionIcon = fullHeart;
        statsUI.forceIcon = forceIcon;
        statsUI.agiliteIcon = agiliteIcon;
        statsUI.intelligenceIcon = intelligenceIcon;
        statsUI.vitesseIcon = vitesseIcon;
        statsUI.charismeIcon = charismeIcon;
        statsUI.enduranceIcon = enduranceIcon;

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
        inventoryUI.slotSize = 72f;
        inventoryUI.spacing = 84f;
        inventoryUI.fontSize = 32;

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
        foreach (KeyValuePair<Vector2Int, RoomType> kv2 in layout)
        {
            if (kv2.Value == RoomType.Secret) minimap.secretRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Boss) minimap.bossRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Shop) minimap.shopRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Event) minimap.eventRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Treasure) minimap.treasureRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Safe) minimap.safeRoomGridPositions.Add(kv2.Key);
        }
        minimap.bossIconSprite = bossMarker;
        minimap.shopIconSprite = shopMarker;
        minimap.eventIconSprite = eventMarker;
        minimap.secretIconSprite = secretMarker;
        minimap.safeIconSprite = safeMarker;
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
        dialogueManager.playerController = playerController;
        dialogueManager.playerHealth = playerHealth;
        dialogueManager.playerStamina = playerStamina;
        dialogueManager.diceRoll = diceRollUI;
        dialogueManager.promptGO = promptGO;
        dialogueManager.panel = dialoguePanel;
        dialogueManager.nameText = npcNameText;
        dialogueManager.bodyText = bodyText;
        dialogueManager.optionsText = optionsText;

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

        // --- Boss health bar (top-center, hidden until a boss binds to it) ---
        GameObject bossBarGO = new GameObject("BossHealthBar", typeof(RectTransform), typeof(Image), typeof(BossHealthBarUI));
        bossBarGO.transform.SetParent(canvasGO.transform, false);
        Image bossBarBackground = bossBarGO.GetComponent<Image>();
        bossBarBackground.color = new Color(0.15f, 0.05f, 0.05f, 0.85f);
        RectTransform bossBarRect = bossBarBackground.rectTransform;
        bossBarRect.anchorMin = bossBarRect.anchorMax = new Vector2(0.5f, 1f);
        bossBarRect.pivot = new Vector2(0.5f, 1f);
        bossBarRect.anchoredPosition = new Vector2(0f, -30f);
        bossBarRect.sizeDelta = new Vector2(900f, 40f);
        bossBarGO.SetActive(false);

        GameObject bossBarFillGO = new GameObject("Fill", typeof(Image));
        bossBarFillGO.transform.SetParent(bossBarGO.transform, false);
        Image bossBarFill = bossBarFillGO.GetComponent<Image>();
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

        foreach (BossRoomController bossRoomController in bossRoomControllers)
        {
            bossRoomController.victoryBanner = victoryBanner;
            bossRoomController.healthBar = bossHealthBar;
        }

        Debug.Log("DungeonGenerator: floor generated with " + layout.Count + " rooms (" + eliteRoomCount + " with an elite).");
    }

    // Chance a still-plain Monster cell tries to absorb neighboring free cells into one bigger
    // room instead of staying single-cell - purely to let some encounters use a bigger arena for
    // more spectacular formations (see MergeMultiCellMonsterRooms).
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

    static Dictionary<Vector2Int, RoomType> GenerateLayout(out Dictionary<Vector2Int, RectInt> cellGroups)
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

        // Never anchored on the Secret room (so its one connection - the only one its bombable wall
        // assumes - is never touched again), and never allowed to reach as far from Start as the
        // Boss room (so it stays unambiguously the single farthest room on the floor).
        PlaceSpecialRoom(rooms, RoomType.Treasure, secretCell, start, bossDistance);
        PlaceSpecialRoom(rooms, RoomType.Shop, secretCell, start, bossDistance);
        if (Random.value < 0.5f) PlaceSpecialRoom(rooms, RoomType.Event, secretCell, start, bossDistance); // 1-in-2 chance per floor
        PlaceSpecialRoom(rooms, RoomType.Gamble, secretCell, start, bossDistance);
        // Guaranteed, like Treasure/Shop - a save point must always be reachable.
        PlaceSpecialRoom(rooms, RoomType.Safe, secretCell, start, bossDistance);

        // Every cell defaults to its own 1x1 group; the merge pass below (run last, once every
        // other room type is already placed) may absorb some plain Monster cells' free neighbors
        // into a bigger shared group.
        cellGroups = new Dictionary<Vector2Int, RectInt>();
        foreach (Vector2Int cell in rooms.Keys) cellGroups[cell] = new RectInt(cell.x, cell.y, 1, 1);
        MergeMultiCellMonsterRooms(rooms, cellGroups);

        return rooms;
    }

    // Opportunistically grows some plain Monster cells into a bigger rectangular room (2/4/6/8
    // cells) by claiming currently-free neighboring cells - never touches Start/Boss/Secret/
    // Treasure/Shop/Event/Gamble/Safe, and never a cell already absorbed by an earlier merge.
    static void MergeMultiCellMonsterRooms(Dictionary<Vector2Int, RoomType> rooms, Dictionary<Vector2Int, RectInt> cellGroups)
    {
        var anchors = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in rooms) if (kv.Value == RoomType.Monster) anchors.Add(kv.Key);
        Shuffle(anchors);

        foreach (Vector2Int anchor in anchors)
        {
            if (cellGroups[anchor].width * cellGroups[anchor].height > 1) continue; // already absorbed
            if (Random.value > MultiCellRoomChance) continue;

            int startTier = Random.Range(0, MultiCellShapeTiers.Length);
            for (int tier = startTier; tier < MultiCellShapeTiers.Length; tier++)
            {
                if (TryPlaceMergedRoom(rooms, cellGroups, anchor, MultiCellShapeTiers[tier])) break;
                // This tier's shapes don't fit around this anchor at all - fall back to a smaller
                // one rather than leaving the anchor single-cell just because the biggest roll missed.
            }
        }
    }

    // Tries every shape in the tier (random order), each as all 4 rectangle corners (random
    // order) so a cell whose only free neighbors are, say, up-and-left can still find a valid
    // orientation. Claims the first orientation whose extra cells are all free and touch nothing
    // outside the new rectangle.
    static bool TryPlaceMergedRoom(Dictionary<Vector2Int, RoomType> rooms, Dictionary<Vector2Int, RectInt> cellGroups, Vector2Int anchor, Vector2Int[] shapes)
    {
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
                        rooms[cell] = RoomType.Monster;
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
    static void PlaceSpecialRoom(Dictionary<Vector2Int, RoomType> rooms, RoomType type, Vector2Int? protectedAnchor, Vector2Int start, int maxDistanceFromStart)
    {
        Dictionary<Vector2Int, int> dist = ComputeDistances(rooms, start);
        Vector2Int? chosen = FindPlacementCandidate(rooms, protectedAnchor, dist, maxDistanceFromStart)
            ?? FindPlacementCandidate(rooms, protectedAnchor, dist, maxDistanceFromStart + 1)
            ?? FindPlacementCandidate(rooms, protectedAnchor, dist, int.MaxValue);
        if (chosen.HasValue) rooms[chosen.Value] = type;
    }

    static Vector2Int? FindPlacementCandidate(Dictionary<Vector2Int, RoomType> rooms, Vector2Int? protectedAnchor, Dictionary<Vector2Int, int> dist, int maxDistanceFromStart)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var candidates = new List<Vector2Int>();

        foreach (Vector2Int cell in rooms.Keys)
        {
            if (cell == protectedAnchor) continue;
            if (dist.TryGetValue(cell, out int cellDist) && cellDist + 1 >= maxDistanceFromStart) continue;
            foreach (Vector2Int d in dirs)
            {
                Vector2Int next = cell + d;
                if (rooms.ContainsKey(next)) continue;

                int neighborCount = 0;
                foreach (Vector2Int d2 in dirs) if (rooms.ContainsKey(next + d2)) neighborCount++;
                if (neighborCount == 1) candidates.Add(next);
            }
        }

        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : (Vector2Int?)null;
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

    // Dead center of the wall, used only for a secret room's wall so there's exactly one spot to
    // bomb along its length instead of a random one.
    static int CenteredDoorOffset(int wallLength)
    {
        return (wallLength - DoorWidth) / 2;
    }

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

    // Merges two adjacent cells of the SAME multi-cell room into one seamless space: removes both
    // rooms' facing interior wall columns and opens the gap between them the same way a normal
    // room's interior is built (floor in the middle, wall only at the outer top/bottom border) -
    // no door, no teleport, just one continuous floor across the former seam.
    static void OpenFullHorizontalSeam(int leftOriginX, int rightOriginX, int originY, Tilemap floorMap, Tilemap wallsMap, Tile floorTile, Tile wallTile)
    {
        for (int y = 1; y < RoomHeight - 1; y++)
        {
            wallsMap.SetTile(new Vector3Int(leftOriginX + RoomWidth - 1, originY + y, 0), null);
            floorMap.SetTile(new Vector3Int(leftOriginX + RoomWidth - 1, originY + y, 0), floorTile);
            wallsMap.SetTile(new Vector3Int(rightOriginX, originY + y, 0), null);
            floorMap.SetTile(new Vector3Int(rightOriginX, originY + y, 0), floorTile);
        }
        for (int gx = 0; gx < Gap; gx++)
        {
            int worldX = leftOriginX + RoomWidth + gx;
            for (int y = 0; y < RoomHeight; y++)
            {
                bool isBorderRow = y == 0 || y == RoomHeight - 1;
                wallsMap.SetTile(new Vector3Int(worldX, originY + y, 0), isBorderRow ? wallTile : null);
                floorMap.SetTile(new Vector3Int(worldX, originY + y, 0), isBorderRow ? null : floorTile);
            }
        }
    }

    // Same as OpenFullHorizontalSeam, for two cells stacked vertically in the same merged room.
    static void OpenFullVerticalSeam(int bottomOriginY, int topOriginY, int originX, Tilemap floorMap, Tilemap wallsMap, Tile floorTile, Tile wallTile)
    {
        for (int x = 1; x < RoomWidth - 1; x++)
        {
            wallsMap.SetTile(new Vector3Int(originX + x, bottomOriginY + RoomHeight - 1, 0), null);
            floorMap.SetTile(new Vector3Int(originX + x, bottomOriginY + RoomHeight - 1, 0), floorTile);
            wallsMap.SetTile(new Vector3Int(originX + x, topOriginY, 0), null);
            floorMap.SetTile(new Vector3Int(originX + x, topOriginY, 0), floorTile);
        }
        for (int gy = 0; gy < Gap; gy++)
        {
            int worldY = bottomOriginY + RoomHeight + gy;
            for (int x = 0; x < RoomWidth; x++)
            {
                bool isBorderCol = x == 0 || x == RoomWidth - 1;
                wallsMap.SetTile(new Vector3Int(originX + x, worldY, 0), isBorderCol ? wallTile : null);
                floorMap.SetTile(new Vector3Int(originX + x, worldY, 0), isBorderCol ? null : floorTile);
            }
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

    static void CreateDoorLink(Vector2 posA, Vector2 inwardA, List<DoorTrigger> exitTriggersA, Vector2 posB, Vector2 inwardB, List<DoorTrigger> exitTriggersB, Transform parent)
    {
        const float landingDepth = 1.5f;
        SpawnDoorTrigger(posA, inwardA, posB + inwardB * landingDepth, exitTriggersA, parent);
        SpawnDoorTrigger(posB, inwardB, posA + inwardA * landingDepth, exitTriggersB, parent);
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
        Sprite swordSprite, Sprite staffSprite)
    {
        Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);

        switch (type)
        {
            case RoomType.Shop:
                SpawnMarker("ShopMarker", center, shopMarker, parent);
                break;
            case RoomType.Treasure:
                SpawnMarker("TreasureMarker", center, treasureMarker, parent);
                // The treasure room guarantees both weapons are reachable on every floor.
                SpawnWeaponPickup("SwordPickup", center + new Vector2(-1.5f, 0f), swordSprite, PlayerController.WeaponType.Sword, parent);
                SpawnWeaponPickup("StaffPickup", center + new Vector2(1.5f, 0f), staffSprite, PlayerController.WeaponType.Staff, parent);
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

    static void RegisterItem(List<ItemCatalog.Entry> entries, string id, string displayName, ItemCategory category, int maxStack, Sprite icon,
        string description = "", int weight = 0, bool isCursed = false, bool hasCursedWeapon = false,
        PlayerController.WeaponType cursedWeaponType = PlayerController.WeaponType.Fist, bool isTrap = false, int healAmount = 0)
    {
        ItemDatabase.Register(new ItemDefinition
        {
            Id = id, DisplayName = displayName, Category = category, MaxStack = maxStack, Icon = icon,
            Description = description, Weight = weight, IsCursed = isCursed, HasCursedWeapon = hasCursedWeapon,
            CursedWeaponType = cursedWeaponType, IsTrap = isTrap, HealAmount = healAmount
        });
        entries.Add(new ItemCatalog.Entry
        {
            id = id, displayName = displayName, category = category, maxStack = maxStack, icon = icon,
            description = description, weight = weight, isCursed = isCursed, hasCursedWeapon = hasCursedWeapon,
            cursedWeaponType = cursedWeaponType, isTrap = isTrap, healAmount = healAmount
        });
    }

    static void SpawnItemPickup(string name, Vector2 position, string itemId, int amount, Transform parent)
    {
        GameObject pickup = ItemPickup.SpawnAt(position, itemId, amount);
        pickup.name = name;
        pickup.transform.SetParent(parent);
    }

    static void SpawnWeaponPickup(string name, Vector2 position, Sprite sprite, PlayerController.WeaponType weapon, Transform parent)
    {
        GameObject pickup = new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(WeaponPickup));
        pickup.transform.SetParent(parent);
        pickup.transform.position = position;
        pickup.transform.localScale = Vector3.one * 0.7f;

        SpriteRenderer renderer = pickup.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        CircleCollider2D collider = pickup.GetComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.4f;

        pickup.GetComponent<WeaponPickup>().weapon = weapon;
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

    // The Safe room's NPC: rest (heal + save - see SaveManager/DialogueManager.savesGame), buy a
    // representative consumable (purchase flow reused as-is by future Shop rooms), and advice for
    // newer players - covers "salle securisee" + "PNJ conseils" from the backlog in one NPC.
    static void SpawnTavernNpc(Vector2 position, Sprite sprite, Sprite badgeSprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
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
                text = "Se reposer (soigne et sauvegarde la partie)",
                checkStat = StatType.None,
                onSuccess = new DialogueOutcome
                {
                    message = "Vous vous reposez un moment. Votre progression est sauvegardee.",
                    savesGame = true,
                },
            },
            new DialogueOption
            {
                text = "Acheter une Potion de Soin (" + HealthPotionPrice + " or)",
                isPurchase = true,
                purchaseItemId = ItemIds.HealthPotion,
                costItemId = ItemIds.Gold,
                costAmount = HealthPotionPrice,
            },
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
        table.greeting = "Des materiaux et des outils sont poses ici.";
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
        };
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
        renderer.sortingOrder = 0;
        AddNpcBadge(go, badgeSprite);

        go.GetComponent<CircleCollider2D>().radius = 1.5f;

        NpcInteractable npc = go.GetComponent<NpcInteractable>();
        npc.npcName = "Marchand";
        npc.greeting = "Jetez un oeil, tout est a vendre.";
        npc.options = new List<DialogueOption>
        {
            BuyOption("Acheter un Shuriken (3 or)", ItemIds.Shuriken, 3),
            BuyOption("Acheter un Caillou (2 or)", ItemIds.Caillou, 2),
            BuyOption("Acheter un Baton (4 or)", ItemIds.Baton, 4),
            BuyOption("Acheter une Bombe (8 or)", ItemIds.Bomb, 8),
            BuyOption("Acheter une Potion de Soin (" + HealthPotionPrice + " or)", ItemIds.HealthPotion, HealthPotionPrice),
        };
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

    // A "collés" formation: one anchor placed with the same wall/door rejection sampling as
    // GenerateEnemySpawnPositions, then the rest of the group scattered tightly around it.
    static List<Vector2> GenerateClusteredPositions(int count, Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors)
    {
        const float ClusterRadius = 1.2f;
        Vector2 center = GenerateEnemySpawnPositions(1, roomOrigin, roomSize, doors)[0];

        List<Vector2> positions = new List<Vector2> { center };
        for (int i = 1; i < count; i++)
        {
            positions.Add(center + Random.insideUnitCircle * ClusterRadius);
        }
        return positions;
    }

    static List<Vector2> GenerateDecorPositions(int count, Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, List<Vector2> avoid)
        => GeneratePlacementPositions(count, roomOrigin, roomSize, doors, avoid, DecorWallMargin, DecorMinSpacing, DecorMinDoorDistance, DecorMinAvoidDistance);

    enum DecorType { StoneBlock, WoodDebris, MetalDebris, ExplosiveBarrel, FuelPuddle, LootPickup, FloorTrap }

    // Chance a LootPickup slot spawns one of the special weight/curse/trap items instead of the
    // common LootTable pool - rare environmental finds, never a kill/break reward.
    const float SpecialLootChance = 0.15f;
    static readonly string[] SpecialLootIds = { ItemIds.Anvil, ItemIds.CursedSword, ItemIds.TrapSack };

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
    }

    // 0-2 decor pieces per cell (a multi-cell room scales up via cellCount), kept away from walls/
    // doors and from `avoid` (already-placed enemies, or a special room's marker/NPC at its
    // center). Each position gets a random decor type.
    static void SpawnRoomDecor(Vector2 roomOrigin, Vector2 roomSize, List<(Vector2 pos, bool onVerticalWall)> doors, List<Vector2> avoid, DecorSprites sprites, Transform parent, int cellCount = 1)
    {
        int count = Random.Range(0, 3) * cellCount;
        if (count == 0) return;

        foreach (Vector2 pos in GenerateDecorPositions(count, roomOrigin, roomSize, doors, avoid))
        {
            switch ((DecorType)Random.Range(0, 7))
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
            }
        }
    }

    static void SpawnDestructible(string name, Vector2 position, Sprite sprite, int maxHealth, int requiredForce, Transform parent, string guaranteedDropItemId = null)
    {
        GameObject go = new GameObject(name, typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(DestructibleObject));
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
    }

    // Encounter compositions a Monster room can roll (used unless a floor-wide theme is active) -
    // paired 1:1 with EncounterPatternClustered.
    static readonly EnemyType[][] EncounterPatterns =
    {
        new[] { EnemyType.Zombie, EnemyType.Zombie, EnemyType.Zombie },
        new[] { EnemyType.ChauveSouris, EnemyType.ChauveSouris, EnemyType.ChauveSouris },
        new[] { EnemyType.ChauveSouris, EnemyType.ChauveSouris, EnemyType.Zombie },
        new[] { EnemyType.Larve, EnemyType.Larve, EnemyType.Larve, EnemyType.Larve, EnemyType.Larve },
    };
    static readonly bool[] EncounterPatternClustered = { true, false, false, true };

    static bool SetupMonsterRoom(List<Vector2Int> memberCells, int originX, int originY, Vector2 roomSize, Transform parent, Transform player,
        RoomController.EnemyPresetEntry[] presets, Sprite speedUpBadge, Sprite hpUpBadge, Sprite glowSprite, Sprite doorBarrierSprite,
        DecorSprites decorSprites, EnemyType? floorTheme, List<(Vector2 pos, bool onVerticalWall)> doors, List<RoomController> controllers)
    {
        Vector2Int gridPos = memberCells[0];
        int cellCount = memberCells.Count;
        // A multi-cell room repeats its base composition instead of a single bigger pattern - 2
        // cells keeps the original size (a modest bump isn't worth dedicated content), 4/6/8 scale
        // up proportionally for the bigger, more spectacular formations this feature exists for.
        int scaleFactor = Mathf.Max(1, cellCount / 2);

        EnemyType[] baseComposition;
        bool clustered;
        if (floorTheme.HasValue)
        {
            baseComposition = new EnemyType[Random.Range(2, 4)];
            for (int i = 0; i < baseComposition.Length; i++) baseComposition[i] = floorTheme.Value;
            clustered = false;
        }
        else
        {
            int patternIndex = Random.Range(0, EncounterPatterns.Length);
            baseComposition = EncounterPatterns[patternIndex];
            clustered = EncounterPatternClustered[patternIndex];
        }
        // A tight cluster only makes sense at the original single-cell scale - a merged room's
        // whole point is using the extra space, so it always spreads out instead.
        if (cellCount > 1) clustered = false;

        EnemyType[] composition = new EnemyType[baseComposition.Length * scaleFactor];
        for (int i = 0; i < composition.Length; i++) composition[i] = baseComposition[i % baseComposition.Length];

        int count = composition.Length;
        bool hasElite = Random.value < EliteChance;
        int eliteIndex = hasElite ? Random.Range(0, count) : -1;
        EliteModifier eliteModifier = hasElite ? (Random.value < 0.5f ? EliteModifier.SpeedUp : EliteModifier.HpUp) : EliteModifier.None;

        Vector2 roomOrigin = new Vector2(originX, originY);
        List<Vector2> positions = clustered
            ? GenerateClusteredPositions(count, roomOrigin, roomSize, doors)
            : GenerateEnemySpawnPositions(count, roomOrigin, roomSize, doors);

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

        foreach ((Vector2 pos, bool onVerticalWall) door in doors)
        {
            GameObject blocker = SpawnDoorBlocker(door.pos, door.onVerticalWall, doorBarrierSprite, roomGO.transform);
            controller.doorBlockers.Add(blocker);
        }

        SpawnRoomDecor(roomOrigin, roomSize, doors, positions, decorSprites, roomGO.transform, cellCount);

        controllers.Add(controller);
        return hasElite;
    }

    const int BossHealth = 40;

    static void SetupBossRoom(Vector2Int gridPos, int originX, int originY, Transform parent, Transform player,
        Sprite bossSprite, Sprite bossProjectileSprite, Sprite doorBarrierSprite,
        List<(Vector2 pos, bool onVerticalWall)> doors, List<BossRoomController> controllers)
    {
        Vector2 roomOrigin = new Vector2(originX, originY);
        Vector2 center = roomOrigin + new Vector2(RoomWidth / 2f, RoomHeight / 2f);

        GameObject bossGO = new GameObject("Cerberus", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(BossController));
        bossGO.transform.SetParent(parent);
        bossGO.transform.position = center;
        bossGO.transform.localScale = Vector3.one * 1.6f;

        SpriteRenderer renderer = bossGO.GetComponent<SpriteRenderer>();
        renderer.sprite = bossSprite;
        renderer.sortingOrder = 0;

        Rigidbody2D body = bossGO.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;

        bossGO.GetComponent<CircleCollider2D>().radius = 0.6f;

        Health health = bossGO.GetComponent<Health>();
        health.maxHealth = BossHealth;
        health.currentHealth = health.maxHealth;

        BossController boss = bossGO.GetComponent<BossController>();
        boss.projectileSprite = bossProjectileSprite;

        GameObject roomGO = new GameObject("BossRoom_" + gridPos, typeof(BossRoomController));
        roomGO.transform.SetParent(parent);

        BossRoomController controller = roomGO.GetComponent<BossRoomController>();
        controller.gridPos = gridPos;
        controller.boss = boss;
        controller.player = player;
        controller.roomOrigin = roomOrigin;
        controller.roomSize = new Vector2(RoomWidth, RoomHeight);

        foreach ((Vector2 pos, bool onVerticalWall) door in doors)
        {
            GameObject blocker = SpawnDoorBlocker(door.pos, door.onVerticalWall, doorBarrierSprite, roomGO.transform);
            controller.doorBlockers.Add(blocker);
        }

        controllers.Add(controller);
    }

    static GameObject SpawnDoorBlocker(Vector2 center, bool onVerticalWall, Sprite sprite, Transform parent)
    {
        GameObject blocker = new GameObject("DoorBlocker", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(DoorBlocker));
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

    static Sprite CreateWallSprite(string path, Color faceColor, Color topColor, Color edgeHighlight)
    {
        int height = TilePixelSize + WallExtraHeight;
        Texture2D tex = new Texture2D(TilePixelSize, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            Color rowColor;
            if (y >= height - 1) rowColor = edgeHighlight;       // 1px lit edge along the very top
            else if (y >= TilePixelSize) rowColor = topColor;    // raised top face, seen from above
            else rowColor = faceColor;                            // shadowed front face, at floor level

            for (int x = 0; x < TilePixelSize; x++) tex.SetPixel(x, y, rowColor);
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
