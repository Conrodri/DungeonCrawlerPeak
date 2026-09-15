using UnityEngine;

// 6-tier rarity scale shared by every item (see ItemDefinition.Rarity) - 1 is the most common, 6
// the rarest. Purely a display/weighting concept, no gameplay stat of its own.
public static class ItemRarity
{
    public const int Min = 1;
    public const int Max = 6;

    public static string Name(int rarity) => rarity switch
    {
        1 => "Commun",
        2 => "Inhabituel",
        3 => "Rare",
        4 => "Epique",
        5 => "Legendaire",
        6 => "Mythique",
        _ => "Commun",
    };

    public static Color Color(int rarity) => rarity switch
    {
        1 => new Color(0.75f, 0.75f, 0.75f),
        2 => new Color(0.3f, 0.8f, 0.3f),
        3 => new Color(0.25f, 0.5f, 0.95f),
        4 => new Color(0.6f, 0.3f, 0.9f),
        5 => new Color(0.95f, 0.6f, 0.15f),
        6 => new Color(0.9f, 0.15f, 0.15f),
        _ => UnityEngine.Color.white,
    };

    public static string HexColor(int rarity)
    {
        Color c = Color(rarity);
        return ColorUtility.ToHtmlStringRGB(c);
    }

    // Common drops more, rare drops less - shared by LootTable/CorpseLoot/Chest instead of each
    // hand-tuning its own weights per item.
    public static int Weight(int rarity) => Mathf.Max(1, (Max + 1) - Mathf.Clamp(rarity, Min, Max));
}
