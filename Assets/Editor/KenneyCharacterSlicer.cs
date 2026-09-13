using UnityEditor;
using UnityEngine;

// Slices individual character tiles out of the Roguelike Characters Pack spritesheet into named
// sub-sprites, Multiple mode, so DungeonBootstrap.RebuildIconPackData can pick them up alongside
// the icon pack atlas via the same runtime lookup (DungeonGenerator.LoadIconPackSprite) - no
// separate runtime code path needed for a second sprite source.
public static class KenneyCharacterSlicer
{
    const string SheetPath = "Assets/Kenney Game Assets/2D assets/Roguelike Characters Pack/Spritesheet/roguelikeChar_transparent.png";
    const int Tile = 16;
    const int Margin = 1;
    const int Pitch = Tile + Margin;
    const int SheetHeight = 203; // Assets/.../roguelikeChar_transparent.png's actual pixel height

    // Row 5, col 0 - a blonde adventurer in an orange tunic, picked by visually inspecting an
    // upscaled crop of the sheet (it's a modular body+hair+clothes builder sheet, not one clean
    // grid of ready characters, so coordinates aren't guessable from Instructions.txt alone).
    // Re-run this (then Dungeon/Rebuild Icon Pack Data) if the chosen tile ever changes.
    // pixelsPerUnit matches DungeonGenerator.TilePixelSize (16) so it renders at exactly 1 world
    // unit, the same convention every procedurally-generated sprite already uses.
    [MenuItem("Dungeon/Kenney/Slice Player Sprite")]
    public static void SlicePlayerSprite()
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Tile;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;

        int row = 5, col = 0;
        int srcY = SheetHeight - Pitch * (row + 1);
        importer.spritesheet = new[]
        {
            new SpriteMetaData
            {
                name = "PlayerHero",
                rect = new Rect(col * Pitch, srcY, Tile, Tile),
                pivot = new Vector2(0.5f, 0.5f),
                alignment = (int)SpriteAlignment.Custom,
            },
        };
        importer.SaveAndReimport();

        Debug.Log("Sliced PlayerHero from " + SheetPath + " at row " + row + ", col " + col + " - run Dungeon/Rebuild Icon Pack Data next.");
    }
}
