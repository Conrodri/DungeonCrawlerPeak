using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class DungeonBootstrap
{
    const int RoomWidth = 14;
    const int RoomHeight = 9;
    const int TilePixelSize = 16;
    const int WallExtraHeight = 8;

    [MenuItem("Dungeon/Build Starter Room")]
    public static void Build()
    {
        Sprite floorSprite = CreateSolidSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateWallSprite("Assets/Art/Tiles/Wall.png", new Color(0.10f, 0.09f, 0.11f), new Color(0.34f, 0.31f, 0.36f), new Color(0.55f, 0.52f, 0.58f));
        Sprite playerSprite = CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));

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

        int halfW = RoomWidth / 2;
        int halfH = RoomHeight / 2;
        for (int x = 0; x < RoomWidth; x++)
        {
            for (int y = 0; y < RoomHeight; y++)
            {
                Vector3Int pos = new Vector3Int(x - halfW, y - halfH, 0);
                bool isWall = x == 0 || y == 0 || x == RoomWidth - 1 || y == RoomHeight - 1;
                wallsMap.SetTile(pos, isWall ? wallTile : null);
                floorMap.SetTile(pos, isWall ? null : floorTile);
            }
        }

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(PlayerController));
        player.transform.SetParent(root.transform);
        player.transform.position = Vector3.zero;
        player.tag = "Player";

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        playerRenderer.sprite = playerSprite;
        // Same sorting order as the walls: with the camera's Y-axis custom sort below, draw
        // order between the player and any wall tile is resolved by world Y position instead.
        playerRenderer.sortingOrder = 0;

        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;

        player.GetComponent<CircleCollider2D>().radius = 0.4f;
        Health playerHealth = player.GetComponent<Health>();
        playerHealth.maxHealth = 5;
        // Awake() (which normally sets this) only runs once Play mode starts, so set it
        // explicitly here too - otherwise the Inspector shows 0/5 while still in Edit mode.
        playerHealth.currentHealth = playerHealth.maxHealth;

        Sprite enemySprite = CreateCircleSprite("Assets/Art/Enemy.png", new Color(0.75f, 0.15f, 0.15f));
        Vector2[] enemySpawns = new Vector2[]
        {
            new Vector2(-4f, 2f),
            new Vector2(4f, 2f),
            new Vector2(0f, -2f),
        };
        foreach (Vector2 spawn in enemySpawns)
        {
            GameObject enemy = new GameObject("Enemy", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Health), typeof(EnemyController));
            enemy.transform.SetParent(root.transform);
            enemy.transform.position = spawn;

            SpriteRenderer enemyRenderer = enemy.GetComponent<SpriteRenderer>();
            enemyRenderer.sprite = enemySprite;
            enemyRenderer.sortingOrder = 0;

            Rigidbody2D enemyBody = enemy.GetComponent<Rigidbody2D>();
            enemyBody.gravityScale = 0f;
            enemyBody.constraints = RigidbodyConstraints2D.FreezeRotation;

            enemy.GetComponent<CircleCollider2D>().radius = 0.4f;
            Health enemyHealth = enemy.GetComponent<Health>();
            enemyHealth.maxHealth = 2;
            enemyHealth.currentHealth = enemyHealth.maxHealth;
            enemy.GetComponent<EnemyController>().SetTarget(player.transform);
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            Vector3 camPos = cam.transform.position;
            cam.transform.position = new Vector3(0f, 0f, camPos.z);

            CameraFollow2D follow = cam.GetComponent<CameraFollow2D>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow2D>();
            follow.target = player.transform;

            // Sort same-order sprites by world Y (further up the screen = further away) so the
            // player correctly passes behind tall wall tops and in front of near ones, Isaac-style.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("DungeonBootstrap: starter room built (" + RoomWidth + "x" + RoomHeight + ") with " + enemySpawns.Length + " enemies.");
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
