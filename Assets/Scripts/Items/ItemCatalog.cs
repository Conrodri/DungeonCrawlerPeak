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
        public string description;
        public int weight;
        public bool isCursed;
        // A Nullable<WeaponType> field would silently fail to survive Unity's serialization
        // (an edit-time-set-but-lost-on-reload trap already hit once this project) - a plain bool
        // flag next to a non-nullable default value doesn't have that problem.
        public bool hasCursedWeapon;
        public PlayerController.WeaponType cursedWeaponType;
        public bool isTrap;
        public int healAmount;
        public bool isEquipment;
        public EquipmentSlotType equipmentSlot;
        public StatType ringBonusStat;
        public int armorValue;
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
                Icon = entry.icon,
                Description = entry.description,
                Weight = entry.weight,
                IsCursed = entry.isCursed,
                HasCursedWeapon = entry.hasCursedWeapon,
                CursedWeaponType = entry.cursedWeaponType,
                IsTrap = entry.isTrap,
                HealAmount = entry.healAmount,
                IsEquipment = entry.isEquipment,
                EquipmentSlot = entry.equipmentSlot,
                RingBonusStat = entry.ringBonusStat,
                ArmorValue = entry.armorValue
            });
        }
    }
}
