using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Drives the whole NPC conversation flow: proximity prompt, option selection (number keys, no
// mouse/EventSystem in this project), the dice roll for checked options, and applying outcomes.
// One instance per scene, created by DungeonBootstrap alongside the rest of the HUD.
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isOpen;

    public PlayerStats playerStats;
    public PlayerInventory playerInventory;
    public PlayerController playerController;
    public Health playerHealth;
    public Stamina playerStamina;
    public DiceRollUI diceRoll;

    public GameObject promptGO;
    public GameObject panel;
    public Text nameText;
    public Text bodyText;
    public Text optionsText;

    NpcInteractable nearbyNpc;
    NpcInteractable activeNpc;
    bool isOpen;
    bool waitingForResolution;
    bool closing;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (isOpen)
        {
            // Safety net: if the active NPC was destroyed by something other than our own
            // outcome-resolution flow (which always schedules CloseAfterDelay itself), don't leave
            // the panel stuck open forever.
            if (activeNpc == null && !closing) { Close(); return; }
            if (!waitingForResolution && !closing)
            {
                // Lets the player back out without being forced to pick an option (e.g. a purchase
                // they can't afford, or just changing their mind) - not allowed mid dice-roll, since
                // the roll coroutine still expects to resolve against an open dialogue.
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
                HandleOptionInput();
            }
            return;
        }

        if (promptGO != null) promptGO.SetActive(nearbyNpc != null);

        if (nearbyNpc != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Open(nearbyNpc);
        }
    }

    public void SetNearbyNpc(NpcInteractable npc)
    {
        nearbyNpc = npc;
    }

    public void ClearNearbyNpc(NpcInteractable npc)
    {
        if (nearbyNpc == npc) nearbyNpc = null;
    }

    // Called from NpcInteractable.OnDestroy - covers both "despawned elsewhere" and the dialogue's
    // own npcDisappearsForever outcome (which destroys the NPC itself, right before CloseAfterDelay
    // is scheduled) - only clears references here, so the outcome message stays on screen for its
    // full delay instead of the panel snapping shut the instant Destroy's deferred callback runs.
    public void NotifyNpcRemoved(NpcInteractable npc)
    {
        if (nearbyNpc == npc) nearbyNpc = null;
        if (activeNpc == npc) activeNpc = null;
    }

    void Open(NpcInteractable npc)
    {
        activeNpc = npc;
        isOpen = true;
        if (promptGO != null) promptGO.SetActive(false);
        panel.SetActive(true);
        nameText.text = npc.npcName;
        bodyText.text = npc.greeting;
        RefreshOptionsText();
    }

    void RefreshOptionsText()
    {
        string s = "";
        for (int i = 0; i < activeNpc.options.Count; i++) s += (i + 1) + ". " + FormatOption(activeNpc.options[i]) + "\n";
        optionsText.text = s;
    }

    // Shows exactly what a check needs (stat + proficiency vs DC) and how risky failing it is, so
    // the player can judge whether to take the option before committing to it - not just after.
    string FormatOption(DialogueOption option)
    {
        string line = option.text;
        if (option.checkStat != StatType.None)
        {
            int statValue = playerStats.GetStat(option.checkStat);
            int proficiency = playerStats.ProficiencyBonus;
            line += " [" + option.checkStat + " " + statValue + " +" + proficiency + " vs DC " + option.dc + ", " + RiskLabel(option.risk) + "]";
        }
        return option.disabled ? "<color=#777777>" + line + " (indisponible)</color>" : line;
    }

    string RiskLabel(RiskTier risk)
    {
        switch (risk)
        {
            case RiskTier.Safe: return "Safe";
            case RiskTier.Important: return "Importante";
            case RiskTier.Risky: return "Risquee";
            default: return "";
        }
    }

    void HandleOptionInput()
    {
        if (Keyboard.current == null || activeNpc == null) return;
        for (int i = 0; i < activeNpc.options.Count && i < 5; i++)
        {
            if (!NumberKeyPressed(i)) continue;
            if (!activeNpc.options[i].disabled) ChooseOption(activeNpc.options[i]);
            return;
        }
    }

    bool NumberKeyPressed(int index)
    {
        switch (index)
        {
            case 0: return Keyboard.current.digit1Key.wasPressedThisFrame;
            case 1: return Keyboard.current.digit2Key.wasPressedThisFrame;
            case 2: return Keyboard.current.digit3Key.wasPressedThisFrame;
            case 3: return Keyboard.current.digit4Key.wasPressedThisFrame;
            case 4: return Keyboard.current.digit5Key.wasPressedThisFrame;
            default: return false;
        }
    }

    void ChooseOption(DialogueOption option)
    {
        optionsText.text = "";

        if (option.isPurchase)
        {
            ResolvePurchase(option);
            return;
        }

        if (option.checkStat == StatType.None)
        {
            Resolve(option.onSuccess, true);
            return;
        }

        waitingForResolution = true;
        int statValue = playerStats.GetStat(option.checkStat);
        int proficiency = playerStats.ProficiencyBonus;
        StartCoroutine(diceRoll.Roll(statValue, proficiency, option.dc, (roll, success) =>
        {
            waitingForResolution = false;
            if (success)
            {
                Resolve(option.onSuccess, true);
                return;
            }

            float malusChance = option.risk == RiskTier.Risky ? 0.66f : option.risk == RiskTier.Important ? 0.33f : 0f;
            bool apply = Random.value < malusChance;
            if (option.risk == RiskTier.Important)
            {
                // Failing an Important check doesn't end the conversation - the option just
                // becomes permanently unusable, and the player can still pick another one.
                option.disabled = true;
                ResolveKeepOpen(option.onFailure, apply);
            }
            else
            {
                Resolve(option.onFailure, apply);
            }
        }));
    }

    // A purchase never rolls dice - just checks/spends a cost item and hands over the result, or
    // refuses with a message if the player is short. Gold is discounted by Charisme (a real shop
    // price); any other cost item (crafting materials) is paid at face value.
    void ResolvePurchase(DialogueOption option)
    {
        closing = true;
        bool isGold = option.costItemId == ItemIds.Gold;
        int cost = isGold ? Mathf.RoundToInt(option.costAmount * playerStats.ShopPriceMultiplier) : option.costAmount;

        if (playerInventory.GetCount(option.costItemId) >= cost && playerInventory.RemoveAmount(option.costItemId, cost))
        {
            playerInventory.Add(option.purchaseItemId, 1);
            bodyText.text = isGold ? "Vous achetez l'objet pour " + cost + " or." : "Vous fabriquez l'objet.";
        }
        else
        {
            bodyText.text = isGold ? "Vous n'avez pas assez d'or (" + cost + " requis)." : "Materiaux insuffisants (" + cost + " requis).";
        }
        StartCoroutine(CloseAfterDelay(1.5f));
    }

    // Same outcome handling as Resolve, but re-lists the options instead of closing the panel -
    // used for a failed Important-tier check, where only that one option should become unusable.
    void ResolveKeepOpen(DialogueOutcome outcome, bool apply)
    {
        if (outcome == null) { RefreshOptionsText(); return; }

        if (apply)
        {
            bodyText.text = outcome.messagePool != null && outcome.messagePool.Length > 0
                ? outcome.messagePool[Random.Range(0, outcome.messagePool.Length)]
                : outcome.message;
            ApplyOutcome(outcome);
        }
        else
        {
            bodyText.text = "Rien ne se passe.";
        }

        // ApplyOutcome may have destroyed the NPC (npcDisappearsForever) - if so, activeNpc is now
        // null and the Update() safety net closes the panel on its own; only refresh if it's still here.
        if (activeNpc != null) RefreshOptionsText();
    }

    void Resolve(DialogueOutcome outcome, bool apply)
    {
        if (outcome == null) { Close(); return; }

        closing = true;
        if (apply)
        {
            bodyText.text = outcome.messagePool != null && outcome.messagePool.Length > 0
                ? outcome.messagePool[Random.Range(0, outcome.messagePool.Length)]
                : outcome.message;
            ApplyOutcome(outcome);
        }
        else
        {
            bodyText.text = "Rien ne se passe.";
        }

        StartCoroutine(CloseAfterDelay(1.5f));
    }

    IEnumerator CloseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Close();
    }

    void ApplyOutcome(DialogueOutcome outcome)
    {
        if (outcome.itemRewardPool != null && outcome.itemRewardPool.Length > 0 && playerInventory != null)
        {
            string picked = outcome.itemRewardPool[Random.Range(0, outcome.itemRewardPool.Length)];
            playerInventory.Add(picked, 1);
        }

        if (outcome.statPenaltyTypes != null)
        {
            for (int i = 0; i < outcome.statPenaltyTypes.Length; i++)
            {
                int amount = outcome.statPenaltyAmounts != null && i < outcome.statPenaltyAmounts.Length ? outcome.statPenaltyAmounts[i] : 0;
                playerStats.ApplyPenalty(outcome.statPenaltyTypes[i], amount);
            }
        }

        if (outcome.curse) playerStats.ApplyCurse();

        if (outcome.removesCursedItem)
        {
            playerInventory.RemoveCurse();
            if (playerController != null) playerController.UnlockWeapon();
        }

        if (outcome.savesGame)
        {
            if (playerHealth != null) playerHealth.Heal(playerHealth.maxHealth);
            SaveManager.Save(DungeonGenerator.CurrentSeed, DungeonGenerator.CurrentFloor, playerInventory, playerStats, playerHealth, playerStamina, playerController,
                DungeonGenerator.ClearedRoomsThisFloor, DungeonGenerator.BossDefeatedThisFloor);
        }

        // Destroying it fires NpcInteractable.OnDestroy -> NotifyNpcRemoved, which only clears the
        // reference (see NotifyNpcRemoved) - the already-scheduled CloseAfterDelay still closes the
        // panel on its own timer, so the outcome message above stays readable.
        if (outcome.npcDisappearsForever && activeNpc != null) Destroy(activeNpc.gameObject);
    }

    void Close()
    {
        isOpen = false;
        waitingForResolution = false;
        closing = false;
        activeNpc = null;
        panel.SetActive(false);
    }
}
