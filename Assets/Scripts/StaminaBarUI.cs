using UnityEngine;
using UnityEngine.UI;

// A small always-visible bar under the hearts - drains while sprinting (PlayerController), refills
// on its own once the player lets off Shift for a moment (see Stamina.regenDelay). Also carries a
// numeric readout: the bar alone was hard to read at a glance (small, easy for a short sprint tap
// to fully regenerate before it's noticed), so the exact numbers make any change unambiguous.
public class StaminaBarUI : MonoBehaviour
{
    public Stamina target;
    public Image fill;
    public Text label;

    void Start()
    {
        if (target != null)
        {
            target.OnStaminaChanged += Refresh;
            Refresh(target.currentStamina, target.maxStamina);
        }
    }

    void OnDestroy()
    {
        if (target != null) target.OnStaminaChanged -= Refresh;
    }

    void Refresh(float current, float max)
    {
        if (fill != null) fill.fillAmount = max > 0f ? current / max : 0f;
        if (label != null) label.text = Mathf.CeilToInt(current) + "/" + Mathf.CeilToInt(max);
    }
}
