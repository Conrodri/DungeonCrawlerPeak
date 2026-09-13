using UnityEngine;

// Purely visual for now (floor/wall palette + display name) - no unique decor/enemies per biome
// yet, that's a bigger follow-up. Colors keep the project's existing flat-color procedural sprite
// look (see DungeonGenerator.CreateSolidSprite/CreateWallSprite) rather than needing new art.
public struct BiomeTheme
{
    public string displayName;
    public Color floorColor;
    public Color wallFaceColor;
    public Color wallTopColor;
    public Color wallEdgeColor;

    public static BiomeTheme Get(Biome biome)
    {
        switch (biome)
        {
            case Biome.Jungle:
                return new BiomeTheme
                {
                    displayName = "Jungle Tropicale",
                    floorColor = new Color(0.16f, 0.22f, 0.12f),
                    wallFaceColor = new Color(0.08f, 0.14f, 0.07f),
                    wallTopColor = new Color(0.22f, 0.34f, 0.16f),
                    wallEdgeColor = new Color(0.4f, 0.55f, 0.25f),
                };
            case Biome.Forest:
                return new BiomeTheme
                {
                    displayName = "Foret",
                    floorColor = new Color(0.20f, 0.16f, 0.10f),
                    wallFaceColor = new Color(0.12f, 0.09f, 0.06f),
                    wallTopColor = new Color(0.28f, 0.22f, 0.14f),
                    wallEdgeColor = new Color(0.45f, 0.38f, 0.22f),
                };
            case Biome.City:
                return new BiomeTheme
                {
                    displayName = "Ville en Ruines",
                    floorColor = new Color(0.22f, 0.22f, 0.24f),
                    wallFaceColor = new Color(0.12f, 0.12f, 0.14f),
                    wallTopColor = new Color(0.32f, 0.32f, 0.35f),
                    wallEdgeColor = new Color(0.55f, 0.55f, 0.6f),
                };
            case Biome.Beach:
                return new BiomeTheme
                {
                    displayName = "Plage",
                    floorColor = new Color(0.55f, 0.48f, 0.30f),
                    wallFaceColor = new Color(0.35f, 0.30f, 0.18f),
                    wallTopColor = new Color(0.62f, 0.55f, 0.35f),
                    wallEdgeColor = new Color(0.8f, 0.72f, 0.5f),
                };
            case Biome.SkyCastle:
                return new BiomeTheme
                {
                    displayName = "Chateau Celeste",
                    floorColor = new Color(0.55f, 0.55f, 0.62f),
                    wallFaceColor = new Color(0.5f, 0.48f, 0.55f),
                    wallTopColor = new Color(0.7f, 0.68f, 0.78f),
                    wallEdgeColor = new Color(0.9f, 0.88f, 0.95f),
                };
            default: // Cave
                return new BiomeTheme
                {
                    displayName = "Grotte",
                    floorColor = new Color(0.14f, 0.14f, 0.17f),
                    wallFaceColor = new Color(0.06f, 0.06f, 0.08f),
                    wallTopColor = new Color(0.20f, 0.20f, 0.25f),
                    wallEdgeColor = new Color(0.35f, 0.35f, 0.42f),
                };
        }
    }
}
