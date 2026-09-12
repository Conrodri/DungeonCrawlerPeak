using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class DungeonBootstrap
{
    enum RoomType { Start, Empty, Monster, Shop, Treasure, Secret, Gamble, Boss, Event }

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

    static readonly Vector2Int[] EnemySpawnOffsets =
    {
        new Vector2Int(5, 6), new Vector2Int(16, 6), new Vector2Int(11, 3),
        new Vector2Int(11, 9), new Vector2Int(8, 9), new Vector2Int(14, 3),
    };

    static readonly string[] HeartMask =
    {
        "  XX  XX  ",
        " XXXXXXXX ",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        " XXXXXXXX ",
        "  XXXXXX  ",
        "   XXXX   ",
        "    XX    ",
    };

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

    [MenuItem("Dungeon/Generate Floor")]
    public static void Build()
    {
        Sprite floorSprite = CreateSolidSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", new Color(0.10f, 0.09f, 0.11f), new Color(0.34f, 0.31f, 0.36f), new Color(0.55f, 0.52f, 0.58f));
        Sprite playerSprite = CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));
        Sprite enemySprite = CreateCircleSprite("Assets/Art/Enemy.png", new Color(0.75f, 0.15f, 0.15f));
        Sprite eliteSprite = CreateCircleSprite("Assets/Art/EnemyElite.png", new Color(0.95f, 0.55f, 0.05f));

        Sprite shopMarker = CreateMaskedSprite("Assets/Art/Markers/Shop.png", ChestMask, new Color(0.75f, 0.55f, 0.15f));
        Sprite treasureMarker = CreateSolidSprite("Assets/Art/Markers/Treasure.png", new Color(0.85f, 0.7f, 0.2f));
        Sprite secretMarker = CreateMaskedSprite("Assets/Art/Markers/Secret.png", QuestionMask, new Color(0.85f, 0.85f, 0.9f));
        Sprite gambleMarker = CreateSolidSprite("Assets/Art/Markers/Gamble.png", new Color(0.85f, 0.35f, 0.15f));
        Sprite bossMarker = CreateMaskedSprite("Assets/Art/Markers/Boss.png", SkullMask, new Color(0.9f, 0.9f, 0.92f));
        Sprite eventMarker = CreateMaskedSprite("Assets/Art/Markers/Event.png", ExclamationMask, new Color(0.55f, 0.25f, 0.85f));

        Sprite projectileSprite = CreateCircleSprite("Assets/Art/Projectile.png", new Color(0.6f, 0.85f, 0.95f));
        Sprite swordPickupSprite = CreateSolidSprite("Assets/Art/Items/Sword.png", new Color(0.75f, 0.78f, 0.82f));
        Sprite staffPickupSprite = CreateSolidSprite("Assets/Art/Items/Staff.png", new Color(0.5f, 0.25f, 0.65f));
        Sprite goldSprite = CreateCircleSprite("Assets/Art/Items/Gold.png", new Color(0.95f, 0.82f, 0.15f));
        Sprite shurikenSprite = CreateSolidSprite("Assets/Art/Items/Shuriken.png", new Color(0.6f, 0.6f, 0.65f));
        Sprite caillouSprite = CreateCircleSprite("Assets/Art/Items/Caillou.png", new Color(0.45f, 0.42f, 0.4f));
        Sprite batonSprite = CreateSolidSprite("Assets/Art/Items/Baton.png", new Color(0.5f, 0.35f, 0.2f));

        Sprite fistVisualSprite = CreateCircleSprite("Assets/Art/Fx/FistHit.png", new Color(0.95f, 0.95f, 0.9f));
        Sprite swordVisualSprite = CreateRectSprite("Assets/Art/Fx/SwordSlash.png", new Color(0.85f, 0.9f, 0.95f));

        Sprite bombSprite = CreateCircleSprite("Assets/Art/Items/Bomb.png", new Color(0.15f, 0.15f, 0.17f));
        Sprite explosionSprite = CreateCircleSprite("Assets/Art/Fx/Explosion.png", new Color(0.95f, 0.55f, 0.15f));
        Sprite doorBarrierSprite = CreateSolidSprite("Assets/Art/Fx/DoorBarrier.png", new Color(0.6f, 0.15f, 0.15f));
        // Same face color as the wall itself, so a secret room's bombable wall blends in - no
        // visual hint, on purpose (detection items are a separate future feature).
        Sprite secretWallSprite = CreateSolidSprite("Assets/Art/Fx/SecretWall.png", new Color(0.10f, 0.09f, 0.11f));
        Sprite outlineRingSprite = CreateRingSprite("Assets/Art/Markers/OutlineRing.png", TilePixelSize, 2, Color.white);
        Sprite npcSprite = CreateCircleSprite("Assets/Art/Npc.png", new Color(0.35f, 0.55f, 0.75f));

        Color heartRed = new Color(0.85f, 0.15f, 0.2f);
        Color heartEmpty = new Color(0.25f, 0.22f, 0.24f);
        Sprite fullHeart = CreateHeartSprite("Assets/Art/UI/HeartFull.png", heartRed, heartRed);
        Sprite halfHeart = CreateHeartSprite("Assets/Art/UI/HeartHalf.png", heartRed, heartEmpty);
        Sprite emptyHeart = CreateHeartSprite("Assets/Art/UI/HeartEmpty.png", heartEmpty, heartEmpty);

        Tile floorTile = CreateTileAsset("Assets/Art/Tiles/FloorTile.asset", floorSprite, Tile.ColliderType.None);
        Tile wallTile = CreateTileAsset("Assets/Art/Tiles/WallTile.asset", wallSprite, Tile.ColliderType.Grid);

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);

        GameObject root = new GameObject("DungeonRoot");

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

        Dictionary<Vector2Int, RoomType> layout = GenerateLayout();

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
                bool leftIsSecret = kv.Value == RoomType.Secret;
                bool rightIsSecret = layout[rightCell] == RoomType.Secret;
                // A secret wall always sits at the dead center of the wall - the wall itself gives
                // no visual hint either way, but at least a player who suspects a given wall only
                // needs to bomb the one predictable spot instead of the whole length of it.
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
            Vector2Int upCell = cell + Vector2Int.up;
            if (layout.ContainsKey(upCell))
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

        // --- Player ---
        Vector2Int startCell = Vector2Int.zero;
        Vector2 startWorld = new Vector2(startCell.x * StepX + RoomWidth / 2f, startCell.y * StepY + RoomHeight / 2f);

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(PlayerInventory), typeof(PlayerStats), typeof(PlayerController));
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
        playerController.shurikenSprite = shurikenSprite;
        playerController.caillouSprite = caillouSprite;
        playerController.batonSprite = batonSprite;
        playerController.bombSprite = bombSprite;
        playerController.explosionSprite = explosionSprite;
        PlayerInventory playerInventory = player.GetComponent<PlayerInventory>();
        PlayerStats playerStats = player.GetComponent<PlayerStats>();
        Health playerHealth = player.GetComponent<Health>();
        // Health is tracked in half-heart units: 3 hearts = 6 units. Normal hits cost 1 (half a
        // heart), elite hits cost 2 (a full heart).
        playerHealth.maxHealth = 6;
        playerHealth.currentHealth = playerHealth.maxHealth;

        // --- Room content (enemies / special-room markers) ---
        var roomEntries = new List<RoomCameraController.RoomEntry>();
        var monsterRoomControllers = new List<RoomController>();
        int eliteRoomCount = 0;
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            int originX = kv.Key.x * StepX;
            int originY = kv.Key.y * StepY;
            roomEntries.Add(new RoomCameraController.RoomEntry { gridPos = kv.Key, rect = new Rect(originX, originY, RoomWidth, RoomHeight) });

            if (kv.Value == RoomType.Monster)
            {
                List<(Vector2 pos, bool onVerticalWall)> doors = doorsByRoom.TryGetValue(kv.Key, out var d) ? d : new List<(Vector2, bool)>();
                bool hasElite = SetupMonsterRoom(kv.Key, originX, originY, root.transform, player.transform,
                    enemySprite, eliteSprite, doorBarrierSprite, doors, monsterRoomControllers);
                if (hasElite) eliteRoomCount++;
            }
            else
            {
                PopulateRoom(kv.Value, originX, originY, root.transform, player.transform,
                    shopMarker, treasureMarker, secretMarker, gambleMarker, bossMarker, eventMarker,
                    swordPickupSprite, staffPickupSprite, goldSprite, shurikenSprite, caillouSprite, batonSprite, bombSprite);

                if (kv.Value == RoomType.Event)
                {
                    Vector2 center = new Vector2(originX + RoomWidth / 2f, originY + RoomHeight / 2f);
                    SpawnExampleNpc(center + new Vector2(2f, 0f), npcSprite, root.transform);
                }
            }
        }

        // Now that every Monster room's RoomController exists, create the door triggers and wire
        // each one to the lock state of the room it teleports into.
        var monsterControllers = new Dictionary<Vector2Int, RoomController>();
        foreach (RoomController rc in monsterRoomControllers) monsterControllers[rc.gridPos] = rc;
        foreach (var link in pendingDoorLinks)
        {
            monsterControllers.TryGetValue(link.cellA, out RoomController roomA);
            monsterControllers.TryGetValue(link.cellB, out RoomController roomB);
            CreateDoorLink(link.posA, link.inwardA, roomA, link.posB, link.inwardB, roomB, root.transform);
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

            // The old CameraFollow2D script was removed in favor of RoomCameraController; drop
            // any leftover "missing script" component before attaching the new one.
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(cam.gameObject);

            RoomCameraController roomCam = cam.GetComponent<RoomCameraController>();
            if (roomCam == null) roomCam = cam.gameObject.AddComponent<RoomCameraController>();
            roomCam.target = player.transform;
            roomCam.rooms = roomEntries.ToArray();

            foreach (RoomController rc in monsterRoomControllers) rc.roomCamera = roomCam;

            // Sort same-order sprites by world Y (further up the screen = further away) so the
            // player correctly passes behind tall wall tops and in front of near ones, Isaac-style.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
        }

        // --- Heart HUD ---
        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform);
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

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
        hud.maxHeartSlots = playerHealth.maxHealth / 2;

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
        goldCounter.goldSprite = goldSprite;

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
        hotbar.shurikenSprite = shurikenSprite;
        hotbar.caillouSprite = caillouSprite;
        hotbar.batonSprite = batonSprite;
        hotbar.bombSprite = bombSprite;
        hotbar.slotSize = 56f;
        hotbar.spacing = 64f;
        hotbar.fontSize = 36;

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
        foreach (KeyValuePair<Vector2Int, RoomType> kv2 in layout)
        {
            if (kv2.Value == RoomType.Secret) minimap.secretRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Boss) minimap.bossRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Shop) minimap.shopRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Event) minimap.eventRoomGridPositions.Add(kv2.Key);
            if (kv2.Value == RoomType.Treasure) minimap.treasureRoomGridPositions.Add(kv2.Key);
        }
        minimap.bossIconSprite = bossMarker;
        minimap.shopIconSprite = shopMarker;
        minimap.eventIconSprite = eventMarker;
        minimap.secretIconSprite = secretMarker;
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
        dialogueManager.diceRoll = diceRollUI;
        dialogueManager.promptGO = promptGO;
        dialogueManager.panel = dialoguePanel;
        dialogueManager.nameText = npcNameText;
        dialogueManager.bodyText = bodyText;
        dialogueManager.optionsText = optionsText;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("DungeonBootstrap: floor generated with " + layout.Count + " rooms (" + eliteRoomCount + " with an elite).");
    }

    static Dictionary<Vector2Int, RoomType> GenerateLayout()
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

        return rooms;
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

    // Wires both directions of a door-to-door teleport: fully crossing either threshold lands
    // the player just inside the other room, past its own threshold. roomA/roomB (nullable) are
    // the RoomControllers of the rooms on each side - each trigger checks its OWN room's lock
    // state (the room it physically sits in), since entering a locked room is always allowed;
    // only leaving one before it's cleared is blocked.
    static void CreateDoorLink(Vector2 posA, Vector2 inwardA, RoomController roomA, Vector2 posB, Vector2 inwardB, RoomController roomB, Transform parent)
    {
        const float landingDepth = 1.5f;
        SpawnDoorTrigger(posA, inwardA, posB + inwardB * landingDepth, roomA, parent);
        SpawnDoorTrigger(posB, inwardB, posA + inwardA * landingDepth, roomB, parent);
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

    static DoorTrigger SpawnDoorTrigger(Vector2 pos, Vector2 inward, Vector2 destination, RoomController ownerRoom, Transform parent, bool locked = false)
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
        trigger.ownerRoom = ownerRoom;

        if (ownerRoom != null) ownerRoom.exitTriggers.Add(trigger);
        return trigger;
    }

    static void PopulateRoom(RoomType type, int originX, int originY, Transform parent, Transform player,
        Sprite shopMarker, Sprite treasureMarker, Sprite secretMarker, Sprite gambleMarker, Sprite bossMarker, Sprite eventMarker,
        Sprite swordSprite, Sprite staffSprite, Sprite goldSprite, Sprite shurikenSprite, Sprite caillouSprite, Sprite batonSprite, Sprite bombSprite)
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
            case RoomType.Start:
                SpawnItemPickup("GoldPickup", center + new Vector2(-2f, 1.5f), goldSprite, ItemType.Gold, 5, parent);
                SpawnItemPickup("ShurikenPickup", center + new Vector2(-0.7f, 1.5f), shurikenSprite, ItemType.Shuriken, 3, parent);
                SpawnItemPickup("CaillouPickup", center + new Vector2(0.7f, 1.5f), caillouSprite, ItemType.Caillou, 3, parent);
                SpawnItemPickup("BatonPickup", center + new Vector2(2f, 1.5f), batonSprite, ItemType.Baton, 3, parent);
                SpawnItemPickup("BombPickup", center + new Vector2(0f, 2.7f), bombSprite, ItemType.Bomb, 3, parent);
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

    static void SpawnItemPickup(string name, Vector2 position, Sprite sprite, ItemType type, int amount, Transform parent)
    {
        GameObject pickup = new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(ItemPickup));
        pickup.transform.SetParent(parent);
        pickup.transform.position = position;
        pickup.transform.localScale = Vector3.one * 0.5f;

        SpriteRenderer renderer = pickup.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        CircleCollider2D collider = pickup.GetComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.4f;

        ItemPickup itemPickup = pickup.GetComponent<ItemPickup>();
        itemPickup.itemType = type;
        itemPickup.amount = amount;
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
    static void SpawnExampleNpc(Vector2 position, Sprite sprite, Transform parent)
    {
        GameObject go = new GameObject("Npc", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(NpcInteractable));
        go.transform.SetParent(parent);
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

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
                    itemRewardPool = new[] { ItemType.Shuriken, ItemType.Caillou, ItemType.Baton, ItemType.Bomb, ItemType.Gold },
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
                    message = "Il n'a pas de temps a perdre avec des illettres, et s'en va.",
                    npcDisappearsForever = true,
                },
            },
            new DialogueOption
            {
                text = "Voir sa marchandise",
                checkStat = StatType.None,
                onSuccess = new DialogueOutcome { message = "Il vous montre ses articles." },
            },
        };
    }

    // Builds a fixed enemy "recipe" for the room (positions + elite flag) and hands it to a
    // RoomController, which does the actual spawning (and re-spawning on reset) at play time.
    static bool SetupMonsterRoom(Vector2Int gridPos, int originX, int originY, Transform parent, Transform player,
        Sprite enemySprite, Sprite eliteSprite, Sprite doorBarrierSprite,
        List<(Vector2 pos, bool onVerticalWall)> doors, List<RoomController> controllers)
    {
        List<Vector2Int> offsets = new List<Vector2Int>(EnemySpawnOffsets);
        for (int i = offsets.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (offsets[i], offsets[j]) = (offsets[j], offsets[i]);
        }

        int count = Random.Range(2, 4);
        bool hasElite = Random.value < EliteChance;
        int eliteIndex = hasElite ? Random.Range(0, count) : -1;

        RoomController.EnemySpawn[] recipe = new RoomController.EnemySpawn[count];
        for (int i = 0; i < count; i++)
        {
            recipe[i] = new RoomController.EnemySpawn { localOffset = offsets[i], isElite = i == eliteIndex };
        }

        GameObject roomGO = new GameObject("MonsterRoom_" + gridPos, typeof(RoomController));
        roomGO.transform.SetParent(parent);

        RoomController controller = roomGO.GetComponent<RoomController>();
        controller.gridPos = gridPos;
        controller.roomOrigin = new Vector2(originX, originY);
        controller.roomSize = new Vector2(RoomWidth, RoomHeight);
        controller.player = player;
        controller.enemySprite = enemySprite;
        controller.eliteSprite = eliteSprite;
        controller.recipe = recipe;

        foreach ((Vector2 pos, bool onVerticalWall) door in doors)
        {
            GameObject blocker = SpawnDoorBlocker(door.pos, door.onVerticalWall, doorBarrierSprite, roomGO.transform);
            controller.doorBlockers.Add(blocker);
        }

        controllers.Add(controller);
        return hasElite;
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

    static Sprite CreateHeartSprite(string path, Color leftColor, Color rightColor)
    {
        int w = HeartMask[0].Length;
        int h = HeartMask.Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < h; y++)
        {
            string row = HeartMask[h - 1 - y]; // texture row 0 is the bottom; mask row 0 is the visual top
            for (int x = 0; x < w; x++)
            {
                if (row[x] != 'X') { tex.SetPixel(x, y, clear); continue; }
                tex.SetPixel(x, y, x < w / 2 ? leftColor : rightColor);
            }
        }
        tex.Apply();
        return SaveTextureAsSprite(tex, path);
    }

    static Sprite SaveTextureAsSprite(Texture2D tex, string path, Vector2? pivot = null)
    {
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        // Delete any previously-imported asset first: changing pixel dimensions on an existing
        // Sprite import can otherwise leave a stale cached sprite rect from the old texture size.
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TilePixelSize;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;

        if (pivot.HasValue)
        {
            importer.spritePivot = pivot.Value;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot.Value;
            importer.SetTextureSettings(settings);
        }

        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Tile CreateTileAsset(string path, Sprite sprite, Tile.ColliderType colliderType)
    {
        Tile existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (existing != null)
        {
            existing.sprite = sprite;
            existing.colliderType = colliderType;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.colliderType = colliderType;
        AssetDatabase.CreateAsset(tile, path);
        return tile;
    }
}
