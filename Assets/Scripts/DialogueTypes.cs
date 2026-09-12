using System;

public enum StatType { None, Force, Dexterite, Intelligence, Vitesse, Constitution, Portee, Charisme }

// 0%, 33%, 66% chance of the failure outcome actually applying when the roll fails.
public enum RiskTier { Safe, Important, Risky }

[Serializable]
public class DialogueOutcome
{
    public string message;
    public string[] messagePool; // if non-empty, one entry is shown at random instead of `message`
    public string[] itemRewardPool; // one entry picked at random and given to the player - empty = no item
    public StatType[] statPenaltyTypes; // parallel to statPenaltyAmounts - a penalty can hit several stats at once
    public int[] statPenaltyAmounts;
    public bool curse; // an extra -1 to every stat, on top of any statPenaltyTypes above
    public bool npcDisappearsForever; // destroys the NPC - it never comes back in this floor
    public bool removesCursedItem; // lifts PlayerInventory's cursed-item lock and any forced weapon
}

[Serializable]
public class DialogueOption
{
    public string text;
    public StatType checkStat = StatType.None; // None = no roll, always resolves as success
    public int dc;
    public RiskTier risk = RiskTier.Safe;
    public DialogueOutcome onSuccess;
    public DialogueOutcome onFailure;
    // Set at runtime when an Important-tier check fails - the option stays listed but can't be
    // chosen again, instead of the whole conversation ending. Persists on this instance, so it
    // stays disabled if the player leaves and re-opens the same NPC's dialogue later.
    [System.NonSerialized] public bool disabled;
}
