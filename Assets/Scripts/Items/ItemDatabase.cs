using System.Collections.Generic;

public static class ItemDatabase
{
    static readonly Dictionary<string, ItemDefinition> byId = new Dictionary<string, ItemDefinition>();

    public static void Register(ItemDefinition definition)
    {
        byId[definition.Id] = definition;
    }

    public static void Clear()
    {
        byId.Clear();
    }

    public static ItemDefinition Get(string itemId)
    {
        byId.TryGetValue(itemId, out ItemDefinition definition);
        return definition;
    }

    public static bool TryGet(string itemId, out ItemDefinition definition)
    {
        return byId.TryGetValue(itemId, out definition);
    }

    public static IEnumerable<ItemDefinition> All => byId.Values;
}
