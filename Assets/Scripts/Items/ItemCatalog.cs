using System;
using System.Collections.Generic;
using UnityEngine;

public class ItemCatalog : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        public ItemCategory category;
        public int maxStack;
        public Sprite icon;
    }

    public List<Entry> entries = new List<Entry>();

    void Awake()
    {
        ItemDatabase.Clear();
        foreach (Entry entry in entries)
        {
            ItemDatabase.Register(new ItemDefinition
            {
                Id = entry.id,
                DisplayName = entry.displayName,
                Category = entry.category,
                MaxStack = entry.maxStack,
                Icon = entry.icon
            });
        }
    }
}
