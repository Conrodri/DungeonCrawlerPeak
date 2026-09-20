using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Drives the whole NPC conversation flow: proximity prompt, option selection (number keys, or a
// clickable icon grid for a shop NPC - see BuildShopGrid/NpcInteractable.useShopUI), the dice roll
// for checked options, and applying outcomes. One instance per scene, created by DungeonBootstrap
// alongside the rest of the HUD.
public class DialogueManager : MonoBehaviour, UIWindowStack.IWindow
{
    public static DialogueManager Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isOpen;

    public PlayerStats playerStats;
    public PlayerInventory playerInventory;
    public PlayerEquipment playerEquipment;
    public PlayerController playerController;
    public Health playerHealth;
    public Stamina playerStamina;
    public DiceRollUI diceRoll;

    public GameObject promptGO;
    public GameObject panel;
    public Text nameText;
    public Text bodyText;
    public Text optionsText;
    // Visual, clickable alternative to optionsText - built/shown instead of the numbered text list
    // when the active NPC is a shop (see NpcInteractable.useShopUI/BuildShopGrid).
    public GameObject shopGridRoot;

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
            // Escape is handled centrally now (see TryCloseFromStack/UIWindowStack, driven from
            // PauseMenuUI) - not allowed mid dice-roll or while closing, same guard as before.
            if (!waitingForResolution && !closing) HandleOptionInput();
            return;
        }

        if (promptGO != null) promptGO.SetActive(nearbyNpc != null);

        if (nearbyNpc != null && KeyBindings.WasPressedThisFrame(GameAction.Interact))
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
        UIWindowStack.Push(this);
        if (promptGO != null) promptGO.SetActive(false);
        panel.SetActive(true);
        nameText.text = npc.npcName;
        bodyText.text = npc.greeting;

        if (npc.useShopUI)
        {
            optionsText.text = "";
            if (shopGridRoot != null) shopGridRoot.SetActive(true);
            BuildShopGrid();
        }
        else
        {
            if (shopGridRoot != null) shopGridRoot.SetActive(false);
            RefreshOptionsText();
        }
    }

    // Called on Escape when this is the top of UIWindowStack (see PauseMenuUI) - same "can't back
    // out mid dice-roll" guard the old inline Update() check used.
    public bool TryCloseFromStack()
    {
        if (!isOpen || waitingForResolution || closing) return false;
        Close();
        return true;
    }

    // Pays and grants the item, then refreshes the grid in place (affordability tint, in case the
    // purchase itself changed what else is affordable) instead of closing the panel - see
    // ChooseOption. Silently ignores anything that isn't a plain item purchase (the Marchand's
    // options are always exactly that - see SpawnMerchantNpc/BuyOption).
    void ResolveShopPurchase(DialogueOption option)
    {
        if (!option.isPurchase || string.IsNullOrEmpty(option.purchaseItemId)) return;

        if (!TryPayCost(option, out int cost, out bool isGold))
        {
            bodyText.text = isGold ? "Vous n'avez pas assez d'or (" + cost + " requis)." : "Materiaux insuffisants (" + cost + " requis).";
            return;
        }

        playerInventory.Add(option.purchaseItemId, 1);
        ItemDefinition definition = ItemDatabase.Get(option.purchaseItemId);
        bodyText.text = "Achete : " + (definition != null ? definition.DisplayName : option.purchaseItemId) + " (" + cost + " or).";
        BuildShopGrid();
    }

    const int ShopGridColumns = 8;
    const float ShopGridCell = 92f;
    const float ShopGridIconSize = 72f;

    // A grid of clickable item icons instead of a numbered text list (explicit request) - one
    // slot per isPurchase option with a real purchaseItemId (every option the Marchand offers, see
    // SpawnMerchantNpc). Rebuilt from scratch on open and after every purchase (cheap - at most a
    // couple dozen slots, only touched when a shop panel is actually open) rather than tracking
    // per-slot diffs. No scrolling yet - 8 columns comfortably fits the Marchand's current ~15
    // items in 2 rows; revisit with a real ScrollRect if the catalog grows past that.
    void BuildShopGrid()
    {
        if (shopGridRoot == null || activeNpc == null) return;
        foreach (Transform child in shopGridRoot.transform) Destroy(child.gameObject);

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 20);
        List<DialogueOption> options = activeNpc.options;
        int slotIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            DialogueOption option = options[i];
            if (!option.isPurchase || string.IsNullOrEmpty(option.purchaseItemId)) continue;

            int col = slotIndex % ShopGridColumns;
            int row = slotIndex / ShopGridColumns;
            slotIndex++;
            Vector2 pos = new Vector2(col * ShopGridCell, -row * ShopGridCell);

            GameObject slotGO = new GameObject("ShopSlot" + i, typeof(Image), typeof(Button), typeof(ShopSlotUI));
            slotGO.transform.SetParent(shopGridRoot.transform, false);

            Image icon = slotGO.GetComponent<Image>();
            ItemDefinition definition = ItemDatabase.Get(option.purchaseItemId);
            icon.sprite = definition != null ? definition.Icon : null;
            RectTransform rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(ShopGridIconSize, ShopGridIconSize);

            bool affordable = TryPeekAfford(option);
            icon.color = affordable ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.5f);

            GameObject priceGO = new GameObject("Price", typeof(Text));
            priceGO.transform.SetParent(slotGO.transform, false);
            Text priceText = priceGO.GetComponent<Text>();
            priceText.font = font;
            priceText.fontSize = 18;
            priceText.fontStyle = FontStyle.Bold;
            priceText.alignment = TextAnchor.LowerRight;
            priceText.color = affordable ? new Color(0.95f, 0.85f, 0.3f) : new Color(0.75f, 0.35f, 0.3f);
            bool isGold = option.costItemId == ItemIds.Gold;
            int cost = isGold ? Mathf.RoundToInt(option.costAmount * playerStats.ShopPriceMultiplier) : option.costAmount;
            priceText.text = cost + (isGold ? "o" : "x");
            RectTransform priceRect = priceText.rectTransform;
            priceRect.anchorMin = Vector2.zero;
            priceRect.anchorMax = Vector2.one;
            priceRect.offsetMin = Vector2.zero;
            priceRect.offsetMax = Vector2.zero;

            Button button = slotGO.GetComponent<Button>();
            button.targetGraphic = icon;
            button.onClick.AddListener(() => ChooseOption(option));

            ShopSlotUI shopSlot = slotGO.GetComponent<ShopSlotUI>();
            shopSlot.option = option;
        }
    }

    // Read-only affordability check for the grid's tint/price color - doesn't spend anything,
    // unlike TryPayCost (which actually removes the cost and is only ever called once the player
    // has committed by clicking/pressing the option).
    bool TryPeekAfford(DialogueOption option)
    {
        bool isGold = option.costItemId == ItemIds.Gold;
        int cost = isGold ? Mathf.RoundToInt(option.costAmount * playerStats.ShopPriceMultiplier) : option.costAmount;
        return playerInventory.GetCount(option.costItemId) >= cost;
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

    // Capped at 9 (digit1-9) - the Table de Craft now lists up to 8 options at once (2026-09-16,
    // 4 flower-based Potion de Soin recipes added alongside Baton/Caillou/Bombe/Reparer), the most
    // any NPC in this project offers at a fixed (non-scrolling) list.
    void HandleOptionInput()
    {
        if (Keyboard.current == null || activeNpc == null) return;
        for (int i = 0; i < activeNpc.options.Count && i < 9; i++)
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
            case 5: return Keyboard.current.digit6Key.wasPressedThisFrame;
            case 6: return Keyboard.current.digit7Key.wasPressedThisFrame;
            case 7: return Keyboard.current.digit8Key.wasPressedThisFrame;
            case 8: return Keyboard.current.digit9Key.wasPressedThisFrame;
            default: return false;
        }
    }

    // Cost/discount math shared by the normal one-shot purchase flow below and ResolveShopPurchase
    // (the Marchand's visual grid, which pays the exact same way but never closes the panel).
    bool TryPayCost(DialogueOption option, out int cost, out bool isGold)
    {
        isGold = option.costItemId == ItemIds.Gold;
        cost = isGold ? Mathf.RoundToInt(option.costAmount * playerStats.ShopPriceMultiplier) : option.costAmount;
        return playerInventory.GetCount(option.costItemId) >= cost && playerInventory.RemoveAmount(option.costItemId, cost);
    }

    void ChooseOption(DialogueOption option)
    {
        // The Marchand's visual grid (see NpcInteractable.useShopUI/BuildShopGrid) reuses this same
        // entry point for both a click and a number key - browsing a shop shouldn't close the panel
        // after every single purchase the way a normal one-off dialogue choice does.
        if (activeNpc != null && activeNpc.useShopUI) { ResolveShopPurchase(option); return; }

        optionsText.text = "";

        if (option.isPurchase)
        {
            if (!TryPayCost(option, out int cost, out bool isGold))
            {
                closing = true;
                bodyText.text = isGold ? "Vous n'avez pas assez d'or (" + cost + " requis)." : "Materiaux insuffisants (" + cost + " requis).";
                StartCoroutine(CloseAfterDelay(1.5f));
                return;
            }

            if (!string.IsNullOrEmpty(option.purchaseItemId))
            {
                closing = true;
                playerInventory.Add(option.purchaseItemId, 1);
                bodyText.text = isGold ? "Vous achetez l'objet pour " + cost + " or." : "Vous fabriquez l'objet.";
                StartCoroutine(CloseAfterDelay(1.5f));
                return;
            }

            // No item id - an on-the-spot effect instead of anything entering the inventory. A
            // plain None check (see EatOption, a Restaurant dish) resolves onSuccess immediately;
            // a real checkStat (see ArcadeOption, an arcade machine) rolls the dice exactly like
            // a free option below, just after gold already changed hands - paying doesn't
            // guarantee winning.
            if (option.checkStat == StatType.None)
            {
                Resolve(option.onSuccess, true);
                return;
            }
        }
        else if (option.checkStat == StatType.None)
        {
            Resolve(option.onSuccess, true);
            return;
        }

        RollAndResolve(option);
    }

    void RollAndResolve(DialogueOption option)
    {
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

        if (outcome.xpReward > 0 && playerStats != null) playerStats.AddExperience(outcome.xpReward);

        if (outcome.healAmount > 0 && playerHealth != null) playerHealth.Heal(outcome.healAmount);

        if (outcome.removesCursedItem)
        {
            playerInventory.RemoveCurse();
            if (playerController != null) playerController.UnlockWeapon();
        }

        if (outcome.savesGame)
        {
            if (playerHealth != null) playerHealth.Heal(playerHealth.maxHealth);
            // A rest is also the one way to fix a broken limb outright (see PlayerLimbs.RepairAll)
            // - a normal Heal() above only tops up limbs that aren't already at 0.
            PlayerLimbs playerLimbs = playerHealth != null ? playerHealth.GetComponent<PlayerLimbs>() : null;
            if (playerLimbs != null) playerLimbs.RepairAll();
            SaveManager.Save(DungeonGenerator.CurrentSeed, DungeonGenerator.CurrentFloor, playerInventory, playerStats, playerHealth, playerStamina, playerController,
                playerEquipment, DungeonGenerator.ClearedRoomsThisFloor, DungeonGenerator.BossDefeatedThisFloor, playerLimbs, DungeonGenerator.UsedBossBiomesBeforeCurrentFloor);
        }

        // Not closed here directly - Resolve()/ResolveKeepOpen() already schedule the dialogue
        // panel's own close shortly after any outcome (see CloseAfterDelay), so it clears itself
        // out of the way on its own without fighting that existing timing.
        if (outcome.opensAttributeAllocation && AttributeAllocationUI.Instance != null)
        {
            AttributeAllocationUI.Instance.Show();
        }

        if (outcome.opensRepairPanel && RepairUI.Instance != null)
        {
            RepairUI.Instance.Show();
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
        if (shopGridRoot != null) shopGridRoot.SetActive(false);
        UIWindowStack.Remove(this);
    }
}
