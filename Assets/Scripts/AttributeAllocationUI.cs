using UnityEngine;
using UnityEngine.UI;

// Opened from the Tavernier NPC (see DialogueOutcome.opensAttributeAllocation) - lets the player
// spend PlayerStats.unspentAttributePoints one at a time on any of the 8 stats. A dedicated panel
// rather than a DialogueOption per stat: the remaining-points count and each stat's current value
// need to update live as points are spent, which a static option list (built once at floor
// generation) can't reflect.
public class AttributeAllocationUI : MonoBehaviour, UIWindowStack.IWindow
{
    public static AttributeAllocationUI Instance { get; private set; }

    public GameObject root;
    public PlayerStats stats;
    public Text pointsLabel;
    public StatType[] statTypes;
    public Text[] valueLabels;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show()
    {
        if (root != null) root.SetActive(true);
        UIWindowStack.Push(this);
        Refresh();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        UIWindowStack.Remove(this);
    }

    public bool TryCloseFromStack()
    {
        if (root == null || !root.activeSelf) return false;
        Hide();
        return true;
    }

    public void Allocate(StatType type)
    {
        if (stats == null || stats.unspentAttributePoints <= 0) return;
        stats.ApplyBonus(type, 1);
        stats.unspentAttributePoints--;
        Refresh();
    }

    void Refresh()
    {
        if (stats == null) return;
        if (pointsLabel != null) pointsLabel.text = "Points disponibles : " + stats.unspentAttributePoints;
        for (int i = 0; i < statTypes.Length; i++)
        {
            if (valueLabels[i] != null) valueLabels[i].text = stats.GetStat(statTypes[i]).ToString();
        }
    }
}
