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

    [MenuItem("Dungeon/Build Starter Room")]
    public static void Build()
    {
        Sprite floorSprite = CreateSolidSprite("Assets/Art/Tiles/Floor.png", new Color(0.24f, 0.22f, 0.20f));
        Sprite wallSprite = CreateSolidSprite("Assets/Art/Tiles/Wall.png", new Color(0.08f, 0.08f, 0.09f));
        Sprite playerSprite = CreateCircleSprite("Assets/Art/Player.png", new Color(0.85f, 0.75f, 0.15f));

        Tile floorTile = CreateTileAsset("Assets/Art/Tiles/FloorTile.asset", floorSprite);
        Tile wallTile = CreateTileAsset("Assets/Art/Tiles/WallTile.asset", wallSprite);

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);

        GameObject root = new GameObject("DungeonRoot");

        GameObject gridGO = new GameObject("Grid", typeof(Grid));
        gridGO.transform.SetParent(root.transform);

        GameObject floorGO = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer));
        floorGO.transform.SetParent(gridGO.transform);
        floorGO.GetComponent<TilemapRenderer>().sortingOrder = 0;

        GameObject wallsGO = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(Rigidbody2D), typeof(CompositeCollider2D));
        wallsGO.transform.SetParent(gridGO.transform);
        wallsGO.GetComponent<TilemapRenderer>().sortingOrder = 1;
        Rigidbody2D wallsBody = wallsGO.GetComponent<Rigidbody2D>();
        wallsBody.bodyType = RigidbodyType2D.Static;
        wallsGO.GetComponent<TilemapCollider2D>().usedByComposite = true;

        Tilemap floorMap = floorGO.GetComponent<Tilemap>();
        Tilemap wallsMap = wallsGO.GetComponent<Tilemap>();

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

        GameObject player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(PlayerController));
        player.transform.SetParent(root.transform);
        player.transform.position = Vector3.zero;

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        playerRenderer.sprite = playerSprite;
        playerRenderer.sortingOrder = 2;

        Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;

        player.GetComponent<CircleCollider2D>().radius = 0.4f;

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
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("DungeonBootstrap: starter room built (" + RoomWidth + "x" + RoomHeight + ").");
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

    static Sprite SaveTextureAsSprite(Texture2D tex, string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = TilePixelSize;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Tile CreateTileAsset(string path, Sprite sprite)
    {
        Tile existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (existing != null)
        {
            existing.sprite = sprite;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        AssetDatabase.CreateAsset(tile, path);
        return tile;
    }
}
