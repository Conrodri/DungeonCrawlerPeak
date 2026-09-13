using UnityEngine;
using UnityEngine.UI;

// Top-center mm:ss countdown, turns red once time is short - readable at a glance while exploring
// without needing to open a menu.
public class FloorTimerUI : MonoBehaviour
{
    public FloorTimer target;
    public Text label;
    public Color warningColor = new Color(0.9f, 0.2f, 0.15f);
    public float warningThreshold = 60f;

    Color baseColor;
    bool baseColorCaptured;

    void Update()
    {
        if (target == null || label == null) return;

        if (!baseColorCaptured)
        {
            baseColor = label.color;
            baseColorCaptured = true;
        }

        float remaining = target.Remaining;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        label.text = minutes.ToString("00") + ":" + seconds.ToString("00");
        label.color = remaining <= warningThreshold ? warningColor : baseColor;
    }
}
