using UnityEngine;

// The quest-giver's own live state (2026-09-21 request: "un maximum d'1 PNJ de quete par etage").
// Builds/refreshes its own NpcInteractable.greeting/options as progress comes in instead of adding
// any new DialogueManager plumbing: a kill quest starts with an EMPTY option list (the greeting
// text alone shows the objective/progress, see BuildStatusText) and gets exactly one "Recuperer la
// recompense" option added once its target is met - DialogueManager already copes fine with a
// short/empty option list (RefreshOptionsText/HandleOptionInput just iterate whatever's there), so
// nothing there needed to change. TurnInItem needs none of this live tracking at all - it's set up
// entirely as a normal isPurchase option in DungeonGenerator.SpawnQuestNpc and never touches this
// component beyond existing.
[RequireComponent(typeof(NpcInteractable))]
public class QuestNpc : MonoBehaviour
{
    public QuestType type;
    public int difficulty; // 1-4, only used upstream (DungeonGenerator) to pick the reward pool
    public int targetCount = 1;
    public int currentCount;
    public string[] rewardPool;
    public int xpReward;
    // Set by DungeonGenerator.SpawnQuestNpc - the flavor line stating the objective, shown as the
    // NPC's greeting until completed (see BuildStatusText).
    public string objectiveText;
    public string completionMessage = "Merci pour votre aide.";

    NpcInteractable npc;
    bool completed;

    void Awake()
    {
        npc = GetComponent<NpcInteractable>();
    }

    void OnEnable()
    {
        if (type == QuestType.KillMonsters) EnemyController.OnAnyEnemyDied += HandleEnemyKilled;
        else if (type != QuestType.TurnInItem) BossRoomController.OnAnyBossDefeated += HandleBossDefeated;
    }

    // Covers destruction too (Unity always calls OnDisable before OnDestroy) - without this, these
    // static events would keep a dead reference to every quest-giver from every floor ever visited.
    void OnDisable()
    {
        EnemyController.OnAnyEnemyDied -= HandleEnemyKilled;
        BossRoomController.OnAnyBossDefeated -= HandleBossDefeated;
    }

    void HandleEnemyKilled()
    {
        if (completed) return;
        currentCount = Mathf.Min(targetCount, currentCount + 1);
        if (currentCount >= targetCount) Complete();
        else RefreshText();
    }

    void HandleBossDefeated(DungeonGenerator.BossTier tier)
    {
        if (completed) return;
        bool matches = (type == QuestType.KillZoneBoss && tier == DungeonGenerator.BossTier.Zone)
            || (type == QuestType.KillVilleBoss && tier == DungeonGenerator.BossTier.Ville)
            || (type == QuestType.KillRegionBoss && tier == DungeonGenerator.BossTier.Region);
        if (!matches) return;
        currentCount = targetCount;
        Complete();
    }

    void Complete()
    {
        completed = true;
        RefreshText();
        if (AchievementManager.Instance != null) AchievementManager.Instance.NotifyQuestCompleted();
        npc.options.Add(new DialogueOption
        {
            text = "Recuperer la recompense",
            checkStat = StatType.None,
            onSuccess = new DialogueOutcome
            {
                message = completionMessage,
                xpReward = xpReward,
                itemRewardPool = rewardPool,
                npcDisappearsForever = true, // one-shot - no farming the same contract twice
            },
        });
    }

    public void RefreshText()
    {
        npc.greeting = BuildStatusText();
    }

    string BuildStatusText()
    {
        if (completed) return "Contrat rempli ! " + completionMessage;
        return type == QuestType.KillMonsters
            ? objectiveText + " (" + currentCount + "/" + targetCount + ")"
            : objectiveText;
    }
}
