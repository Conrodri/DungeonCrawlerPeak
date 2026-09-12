using System;
using System.Collections.Generic;
using UnityEngine;

// Baked once in the Editor (Dungeon/Rebuild Icon Pack Data, Assets/Editor/DungeonBootstrap.cs)
// from the "Modern GDR - Free icons pack" atlas, then loaded at runtime via Resources.Load -
// AssetDatabase (used to read the atlas by name) is Editor-only, this lookup table isn't.
public class IconPackData : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string name;
        public Sprite sprite;
    }

    public List<Entry> entries = new List<Entry>();
}
