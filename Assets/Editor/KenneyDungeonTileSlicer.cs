using UnityEditor;
using UnityEngine;

// Slices a floor tile and a wall-face tile out of the Roguelike Dungeon Pack spritesheet, same
// named-sub-sprite approach as KenneyCharacterSlicer - picked up by DungeonBootstrap.RebuildIconPackData
// and sampled pixel-by-pixel at runtime (DungeonGenerator.CreateTexturedFloorSprite/CreateWallSprite)
// to give the floor/walls real texture instead of a single flat color, while keeping each biome's
// palette (DungeonGenerator.TintFor multiplies this texture by the biome color instead of replacing it).
public static class KenneyDungeonTileSlicer
{
    const string SheetPath = "Assets/Kenney Game Assets/2D assets/Roguelike Dungeon Pack/Spritesheet/roguelikeDungeon_transparent.png";
    const int Tile = 16;
    const int Margin = 1;
    const int Pitch = Tile + Margin;
    const int SheetHeight = 305; // Assets/.../roguelikeDungeon_transparent.png's actual pixel height

    // col/row picked by visually inspecting an upscaled+gridded crop (same method as
    // KenneyCharacterSlicer) - col 8 row 2 is a clean, crack-free grey stone tile (floor); col 8
    // row 0 is the same stone block's face variant, used tiled as the wall's vertical face band.
    // isReadable=true (unlike the character slicer) because these are pixel-sampled at runtime,
    // not just assigned whole to a SpriteRenderer.
    [MenuItem("Dungeon/Kenney/Slice Dungeon Tiles")]
    public static void SliceDungeonTiles()
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Tile;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = true;

        (string name, int col, int row)[] tiles =
        {
            ("DungeonFloor", 8, 2),
            ("DungeonWallFace", 8, 0),
        };

        var metas = new SpriteMetaData[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            // +1: this sheet (unlike the character one) has an extra fully-transparent 1px border
            // row at the very bottom, on top of the documented 1px inter-tile margin - confirmed by
            // scanning alpha across a known-solid tile (row 2's cell was transparent at dy=0/17,
            // opaque only at dy=1..16) rather than trusting Instructions.txt's plain "margin: 1".
            int srcY = SheetHeight - Pitch * (tiles[i].row + 1) + 1;
            metas[i] = new SpriteMetaData
            {
                name = tiles[i].name,
                rect = new Rect(tiles[i].col * Pitch, srcY, Tile, Tile),
                pivot = new Vector2(0.5f, 0.5f),
                alignment = (int)SpriteAlignment.Custom,
            };
        }
        importer.spritesheet = metas;
        importer.SaveAndReimport();

        Debug.Log("Sliced " + tiles.Length + " dungeon tiles from " + SheetPath + " - run Dungeon/Rebuild Icon Pack Data next.");
    }
}
