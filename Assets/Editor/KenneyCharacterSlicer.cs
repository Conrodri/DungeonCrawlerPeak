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

    // Row/col picked by visually inspecting an upscaled crop of the sheet (it's a modular
    // body+hair+clothes builder sheet, not one clean grid of ready characters, so coordinates
    // aren't guessable from Instructions.txt alone). Re-run this (then Dungeon/Rebuild Icon Pack
    // Data) if a chosen tile ever changes. pixelsPerUnit matches DungeonGenerator.TilePixelSize
    // (16) so each renders at exactly 1 world unit, the same convention every procedurally-
    // generated sprite already uses.
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

        // Ready-made single-tile characters beyond the player's own (row 5 col 0) - picked by the
        // same visual-inspection method, distinct enough silhouettes to tell NPC roles apart even
        // before DungeonGenerator applies a per-role color tint (SpawnExampleNpc/SpawnTavernNpc/
        // SpawnMerchantNpc/SpawnTutorialNpc - see "meme si c'est les memes, change la couleur" 2026-09-14).
        (string name, int row, int col)[] tiles =
        {
            ("PlayerHero", 5, 0),        // blonde adventurer, orange tunic
            ("NpcElder", 5, 1),          // white-haired elder, teal robe - Le Tavernier
            ("NpcStranger", 6, 0),       // rugged shirtless wanderer - Etranger encapuchonne
            ("NpcMerchant", 6, 1),       // bearded, teal pauldrons - Marchand
            ("NpcGuide", 7, 0),          // tan vest - Le Guide (tutoriel)
        };

        var metas = new SpriteMetaData[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            int srcY = SheetHeight - Pitch * (tiles[i].row + 1);
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

        Debug.Log("Sliced " + tiles.Length + " character tiles from " + SheetPath + " - run Dungeon/Rebuild Icon Pack Data next.");
    }
}
