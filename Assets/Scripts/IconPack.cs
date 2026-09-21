using System.Collections.Generic;
using UnityEngine;

// Runtime lookup into the baked icon atlas (see IconPackData) - a small shared alternative to
// DungeonGenerator's own private LoadIconPackSprite (kept as-is for its many existing call sites)
// for callers that just need one icon by name without threading a sprite reference through
// several constructor signatures first (2026-09-21: PlayerController/EnemyController's broken-limb
// debuff icons, which have no reason to route through RoomController/FloorAssets just for this).
public static class IconPack
{
    static Dictionary<string, Sprite> cache;

    public static Sprite Get(string name)
    {
        if (cache == null)
        {
            cache = new Dictionary<string, Sprite>();
            IconPackData data = Resources.Load<IconPackData>("IconPackData");
            if (data != null) foreach (IconPackData.Entry entry in data.entries) cache[entry.name] = entry.sprite;
        }
        return cache.TryGetValue(name, out Sprite sprite) ? sprite : null;
    }
}
