using UnityEngine;
using UnityEngine.UI;

// Small bar + "Niveau X" label, same bound-event pattern as StaminaBarUI/HeartHUD. There was no
// XP/level visual at all before this - PlayerStats.level only fed dialogue proficiency checks
// silently.
public class ExperienceBarUI : MonoBehaviour
{
    public PlayerStats target;
    public Image fill;
    public Text label;

    void Start()
    {
        if (target != null)
        {
            target.OnExperienceChanged += Refresh;
            Refresh(target.experience, target.experienceToNextLevel, target.level);
        }
    }

    void OnDestroy()
    {
        if (target != null) target.OnExperienceChanged -= Refresh;
    }

    void Refresh(int current, int max, int level)
    {
        if (fill != null) fill.fillAmount = max > 0 ? (float)current / max : 0f;
        if (label != null) label.text = "Niveau " + level;
    }
}
