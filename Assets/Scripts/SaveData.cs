using System;
using System.Collections.Generic;
using UnityEngine;

// Flat, JsonUtility-friendly snapshot of everything a resumed run needs: the seed (so
// DungeonGenerator.Build(seed) recreates the exact same floor) plus the player's full state.
[Serializable]
public class SaveData
{
    public int seed;
    public int floor = 1;

    // Which Monster rooms (by grid cell - see RoomController.memberCells) were already fully
    // cleared, and whether this floor's boss was already defeated - without this, "Continuer"
    // called DungeonGenerator.Build(seed) fresh, which recreates the exact same LAYOUT (same seed)
    // but has no memory of progress within it, so every monster and the boss respawned on every
    // reload. Empty/false for a floor nothing has been cleared on yet.
    public List<Vector2Int> clearedRooms = new List<Vector2Int>();
    public bool bossDefeated;

    public InventorySlot[] slots;
    public string[] hotbarSlots;
    public string cursedItemId;

    public int level;
    public int experience;
    public int experienceToNextLevel;
    public int unspentAttributePoints;
    public int force;
    public int dexterite;
    public int intelligence;
    public int vitesse;
    public int constitution;
    public int portee;
    public int charisme;
    public int endurance;

    // Equipped item ids (see PlayerEquipment/EquipmentSlotType) - without these, "Continuer" would
    // rebuild a fresh, empty PlayerEquipment and silently strip everything the player had worn.
    public string equippedHead;
    public string equippedShoulders;
    public string equippedGloves;
    public string equippedBoots;
    public string equippedNeck;
    public string equippedBelt;
    public string equippedKnees;
    public string[] equippedRingsLeft;
    public string[] equippedRingsRight;

    public int maxHealth;
    public int currentHealth;
    public float maxStamina;
    public float currentStamina;

    public PlayerController.WeaponType currentWeapon;
    public bool weaponLocked;
    public BodyPart weaponHand = BodyPart.ArmRight;

    // Per-part HP (see PlayerLimbs/LimbState) - flat named fields, not an array/dictionary, same
    // JsonUtility-friendly convention as the rest of this class (JsonUtility can't serialize a
    // Dictionary, and a bare array loses the BodyPart->slot mapping across a version change). The
    // = MaxLimbHealth initializer isn't just a sensible default for a brand new SaveData - verified
    // via eval that JsonUtility.FromJson keeps a field's initializer when the source JSON doesn't
    // contain that key, so an old save file from before this system existed resumes with every limb
    // full rather than silently broken.
    public int limbHealthHead = PlayerLimbs.MaxLimbHealth;
    public int limbHealthTorso = PlayerLimbs.MaxLimbHealth;
    public int limbHealthArmLeft = PlayerLimbs.MaxLimbHealth;
    public int limbHealthArmRight = PlayerLimbs.MaxLimbHealth;
    public int limbHealthLegLeft = PlayerLimbs.MaxLimbHealth;
    public int limbHealthLegRight = PlayerLimbs.MaxLimbHealth;
}
