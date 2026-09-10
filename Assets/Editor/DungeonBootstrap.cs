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
    enum RoomType { Start, Empty, Monster, Shop, Treasure, Secret, Gamble }

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

    [MenuItem("Dungeon/Generate Floor")]
    public static void Build()
    {
        Sprite floorSprite = CreateSolidSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", new Color(0.10f, 0.09f, 0.11f), new Color(0.34f, 0.31f, 0.36f), new Color(0.55f, 0.52f, 0.58f));
        Sprite playerSprite = CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));
        Sprite enemySprite = CreateCircleSprite("Assets/Art/Enemy.png", new Color(0.75f, 0.15f, 0.15f));
        Sprite eliteSprite = CreateCircleSprite("Assets/Art/EnemyElite.png", new Color(0.95f, 0.55f, 0.05f));

        Sprite shopMarker = CreateSolidSprite("Assets/Art/Markers/Shop.png", new Color(0.2f, 0.7f, 0.75f));
        Sprite treasureMarker = CreateSolidSprite("Assets/Art/Markers/Treasure.png", new Color(0.85f, 0.7f, 0.2f));
        Sprite secretMarker = CreateSolidSprite("Assets/Art/Markers/Secret.png", new Color(0.55f, 0.35f, 0.75f));
        Sprite gambleMarker = CreateSolidSprite("Assets/Art/Markers/Gamble.png", new Color(0.85f, 0.35f, 0.15f));

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

        GameObject wallsGO = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(Rigidbody2D), typeof(CompositeCollider2D));
        wallsGO.transform.SetParent(gridGO.transform);
        // Individual mode (rather than batched Chunk) lets each wall tile sort against the
        // player sprite by Y position, so tall wall tops correctly draw in front of / behind the player.
        wallsGO.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
        wallsGO.GetComponent<TilemapRenderer>().sortingOrder = 0;
        Rigidbody2D wallsBody = wallsGO.GetComponent<Rigidbody2D>();
        wallsBody.bodyType = RigidbodyType2D.Static;
        wallsGO.GetComponent<TilemapCollider2D>().usedByComposite = true;

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
        // Each room's doors, so a Monster room can later block/unblock its own thresholds.
        var doorsByRoom = new Dictionary<Vector2Int, List<(Vector2 pos, bool onVerticalWall)>>();
        foreach (KeyValuePair<Vector2Int, RoomType> kv in layout)
        {
            Vector2Int cell = kv.Key;
            if (layout.ContainsKey(cell + Vector2Int.right))
            {
                // Each side's door is positioned independently along its own wall - a room
                // entered near one corner can open into its neighbor near the opposite one.
                int leftDoorY = RandomDoorOffset(RoomHeight) + cell.y * StepY;
                int rightDoorY = RandomDoorOffset(RoomHeight) + cell.y * StepY;
                CarveHorizontalDoor(cell.x * StepX, (cell.x + 1) * StepX, leftDoorY, rightDoorY, floorMap, wallsMap, floorTile);

                Vector2 leftPos = new Vector2(cell.x * StepX + RoomWidth - 0.5f, leftDoorY + DoorWidth / 2f);
                Vector2 rightPos = new Vector2((cell.x + 1) * StepX + 0.5f, rightDoorY + DoorWidth / 2f);
                AddDoorInfo(doorsByRoom, cell, leftPos, true);
                AddDoorInfo(doorsByRoom, cell + Vector2Int.right, rightPos, true);
                CreateDoorLink(leftPos, Vector2.left, rightPos, Vector2.right, root.transform);
            }
            if (layout.ContainsKey(cell + Vector2Int.up))
            {
                int bottomDoorX = RandomDoorOffset(RoomWidth) + cell.x * StepX;
                int topDoorX = RandomDoorOffset(RoomWidth) + cell.x * StepX;
                CarveVerticalDoor(cell.y * StepY, (cell.y + 1) * StepY, bottomDoorX, topDoorX, floorMap, wallsMap, floorTile);

                Vector2 bottomPos = new Vector2(bottomDoorX + DoorWidth / 2f, cell.y * StepY + RoomHeight - 0.5f);
                Vector2 topPos = new Vector2(topDoorX + DoorWidth / 2f, (cell.y + 1) * StepY + 0.5f);
                AddDoorInfo(doorsByRoom, cell, bottomPos, false);
                AddDoorInfo(doorsByRoom, cell + Vector2Int.up, topPos, false);
                CreateDoorLink(bottomPos, Vector2.down, topPos, Vector2.up, root.transform);
            }
        }

        // --- Player ---
        Vector2Int startCell = Vector2Int.zero;
        Vector2 startWorld = new Vector2(startCell.x * StepX + RoomWidth / 2f, startCell.y * StepY + RoomHeight / 2f);

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(PlayerInventory), typeof(PlayerController));
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
                    shopMarker, treasureMarker, secretMarker, gambleMarker,
                    swordPickupSprite, staffPickupSprite, goldSprite, shurikenSprite, caillouSprite, batonSprite, bombSprite);
            }
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
        hotbar.fontSize = 18;

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
        minimap.allRoomGridPositions = new List<Vector2Int>(layout.Keys);
        minimap.cellSize = 22f;
        minimap.spacing = 5f;
        minimap.maxPanelSize = 320f;

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

        PlaceSpecialRoom(rooms, RoomType.Treasure);
        PlaceSpecialRoom(rooms, RoomType.Shop);
        PlaceSpecialRoom(rooms, RoomType.Secret);
        PlaceSpecialRoom(rooms, RoomType.Gamble);

        return rooms;
    }

    static void PlaceSpecialRoom(Dictionary<Vector2Int, RoomType> rooms, RoomType type)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var candidates = new List<Vector2Int>();

        foreach (Vector2Int cell in rooms.Keys)
        {
            foreach (Vector2Int d in dirs)
            {
                Vector2Int next = cell + d;
                if (rooms.ContainsKey(next)) continue;

                int neighborCount = 0;
                foreach (Vector2Int d2 in dirs) if (rooms.ContainsKey(next + d2)) neighborCount++;
                if (neighborCount == 1) candidates.Add(next);
            }
        }

        if (candidates.Count == 0) return;
        Vector2Int chosen = candidates[Random.Range(0, candidates.Count)];
        rooms[chosen] = type;
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
    // the player just inside the other room, past its own threshold.
    static void CreateDoorLink(Vector2 posA, Vector2 inwardA, Vector2 posB, Vector2 inwardB, Transform parent)
    {
        const float landingDepth = 1.5f;
        SpawnDoorTrigger(posA, inwardA, posB + inwardB * landingDepth, parent);
        SpawnDoorTrigger(posB, inwardB, posA + inwardA * landingDepth, parent);
    }

    static void SpawnDoorTrigger(Vector2 pos, Vector2 inward, Vector2 destination, Transform parent)
    {
        GameObject go = new GameObject("DoorTrigger", typeof(BoxCollider2D), typeof(DoorTrigger));
        go.transform.SetParent(parent);
        // Sits a little toward the void side of the threshold, so the player has to step fully
        // through the opening (not just graze its edge) before teleporting.
        go.transform.position = pos - inward * 0.4f;

        BoxCollider2D collider = go.GetComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(DoorWidth, DoorWidth);

        go.GetComponent<DoorTrigger>().destination = destination;
    }

    static void PopulateRoom(RoomType type, int originX, int originY, Transform parent, Transform player,
        Sprite shopMarker, Sprite treasureMarker, Sprite secretMarker, Sprite gambleMarker,
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
