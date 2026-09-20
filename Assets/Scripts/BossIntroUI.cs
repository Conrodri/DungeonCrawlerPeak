using UnityEngine;
using UnityEngine.UI;

// Boss-fight intro title card (2026-09-21 request: camera pans to the boss, the fight pauses, an
// AI voice narrates the boss's name/tier/lore, then control returns to the player - see
// BossRoomController.PlayIntroThenEngage). Two lines (name+tier, then the lore blurb) rather than
// RoomAnnouncementUI/VictoryBannerUI's single line - different enough shape to earn its own tiny
// component, same reasoning as AchievementToastUI.
public class BossIntroUI : MonoBehaviour
{
    public GameObject root;
    public Text nameText;
    public Text descriptionText;

    public void Show(string bossName, string tierLabel, string description)
    {
        if (nameText != null) nameText.text = bossName + " — Boss de " + tierLabel;
        if (descriptionText != null) descriptionText.text = description;
        if (root != null) root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
