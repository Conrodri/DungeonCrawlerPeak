using UnityEngine;
using UnityEngine.UI;

// Small bar + "Niveau X" label. Polls every frame instead of subscribing to PlayerStats.
// OnExperienceChanged - see StaminaBarUI for why (a Play Mode domain reload silently drops event
// subscriptions without ever re-running Start()).
public class ExperienceBarUI : MonoBehaviour
{
    public PlayerStats target;
    public Image fill;
    public Text label;

    void Update()
    {
        if (target == null) return;
        if (fill != null) fill.fillAmount = target.experienceToNextLevel > 0 ? (float)target.experience / target.experienceToNextLevel : 0f;
        if (label != null) label.text = "Niveau " + target.level;
    }
}
