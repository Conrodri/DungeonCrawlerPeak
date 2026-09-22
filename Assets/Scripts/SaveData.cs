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

    // Snapshot of DungeonGenerator.UsedBossBiomesBeforeCurrentFloor at save time - the run-wide
    // history of boss families already handed out, as it stood BEFORE the currently loaded floor
    // picked its own 3 (not including them) - see DungeonGenerator.Build's priorUsedBossBiomes
    // parameter for why the distinction matters (2026-09-15 "no boss family repeats within or
    // across floors" feature): restoring exactly this pre-floor snapshot is what lets "Continuer"
    // reproduce this floor's identical 3 boss families instead of rolling a fresh set.
    public List<Biome> usedBossBiomesBeforeFloor = new List<Biome>();

    // World position at the moment of saving - without this, "Continuer" always dropped the
    // player back at the Start room (DungeonGenerator.Build's fixed spawn point) regardless of
    // where they actually saved (2026-09-15 report). Defaults to (0,0), which sits inside/near the
    // Start room anyway (RoomWidth/RoomHeight center is the real spawn, not exactly the origin) -
    // a save from before this field existed just resumes at the old default behavior instead of
    // erroring, same JsonUtility-keeps-the-initializer tolerance already relied on for limbHealth*
    // below.
    public Vector2 playerPosition;

    public InventorySlot[] slots;
    public string[] hotbarSlots;
    public string cursedItemId;

    public int level;
    public int experience;
    // No experienceToNextLevel here anymore - PlayerStats.experienceToNextLevel is a computed
    // property derived from `level` now, so it's never captured/restored on its own (see
    // PlayerStats.cs - old saves with a stale value from before this change simply recompute
    // fresh off the `level` field above, exactly like a brand new run would).
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
    // A real weapon item worn in the Weapon slot (see EquipmentSlotType.Weapon) - null/empty while
    // fighting bare-handed or while a cursed weapon is forced on (see weaponLocked/currentWeapon
    // below, which cover that case instead).
    public string equippedWeapon;
    public string[] equippedRingsLeft;
    public string[] equippedRingsRight;

    // Durability parallel to the equipped-item fields above (see PlayerEquipment) - 0 for a save
    // predating this feature reads as "fully worn out" on whatever's equipped, same tolerated
    // migration quirk already accepted for limbHealth* below (a prototype still under active
    // development, not a shipped save format). Weapons don't carry durability (2026-09-22 removal)
    // so there's no weapon-side field here anymore - see PlayerController/DungeonGenerator.
    public int durabilityHead;
    public int durabilityShoulders;
    public int durabilityGloves;
    public int durabilityBoots;
    public int durabilityNeck;
    public int durabilityBelt;
    public int durabilityKnees;
    public int[] durabilityRingsLeft;
    public int[] durabilityRingsRight;

    public int maxHealth;
    public int currentHealth;
    public float maxStamina;
    public float currentStamina;

    public PlayerController.WeaponType currentWeapon;
    public bool weaponLocked;
    public BodyPart weaponHand = BodyPart.ArmRight;

    // Per-part HP (see PlayerLimbs/LimbState - Head 35/Torso 70/Arm 20 each/Leg 30 each) - flat
    // named fields, not an array/dictionary, same JsonUtility-friendly convention as the rest of
    // this class (JsonUtility can't serialize a Dictionary, and a bare array loses the
    // BodyPart->slot mapping across a version change). The = BaseMaxFor(...) initializer isn't
    // just a sensible default for a brand new SaveData - verified via eval that
    // JsonUtility.FromJson keeps a field's initializer when the source JSON doesn't contain that
    // key, so a save file from before this field existed resumes with every limb full rather than
    // silently broken. A save from the OLD flat-3-per-limb system (this same session, before this
    // HP rework) is a separate, narrower case NOT specially migrated - its small saved values
    // would clamp down to nearly-broken limbs under the new much larger maxes. Acceptable for a
    // single-save prototype still under active development; revisit if that ever stops being true.
    public int limbHealthHead = PlayerLimbs.BaseMaxFor(BodyPart.Head);
    public int limbHealthTorso = PlayerLimbs.BaseMaxFor(BodyPart.Torso);
    public int limbHealthArmLeft = PlayerLimbs.BaseMaxFor(BodyPart.ArmLeft);
    public int limbHealthArmRight = PlayerLimbs.BaseMaxFor(BodyPart.ArmRight);
    public int limbHealthLegLeft = PlayerLimbs.BaseMaxFor(BodyPart.LegLeft);
    public int limbHealthLegRight = PlayerLimbs.BaseMaxFor(BodyPart.LegRight);
}
