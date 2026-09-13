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

    // Temporary diagnostic - see StaminaBarUI.lastLogTime.
    float lastLogTime = -999f;

    void Update()
    {
        if (target == null) return;
        float fillValue = target.experienceToNextLevel > 0 ? (float)target.experience / target.experienceToNextLevel : 0f;
        if (fill != null) fill.fillAmount = fillValue;
        if (label != null) label.text = "Niveau " + target.level;

        if (Time.time - lastLogTime > 2f)
        {
            lastLogTime = Time.time;
            Debug.Log("[ExperienceBarUI] xp=" + target.experience + "/" + target.experienceToNextLevel + " level=" + target.level
                + " fillAmount=" + fillValue + " fillActive=" + (fill != null && fill.gameObject.activeInHierarchy)
                + " selfActive=" + gameObject.activeInHierarchy);
        }
    }
}
