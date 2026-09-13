using UnityEngine;
using UnityEngine.UI;

// A small always-visible bar under the hearts - drains while sprinting (PlayerController), refills
// on its own once the player lets off Shift for a moment (see Stamina.regenDelay). Also carries a
// numeric readout: the bar alone was hard to read at a glance (small, easy for a short sprint tap
// to fully regenerate before it's noticed), so the exact numbers make any change unambiguous.
//
// Polls every frame instead of subscribing to Stamina.OnStaminaChanged (same reasoning as
// StatsUI) - a Play Mode domain reload (e.g. a script recompile while already playing) silently
// drops C# event subscriptions without Start() ever re-running to resubscribe, which left this
// bar frozen. Two floats read every frame is free enough that there's no reason to risk that.
public class StaminaBarUI : MonoBehaviour
{
    public Stamina target;
    public Image fill;
    public Text label;

    void Update()
    {
        if (target == null) return;
        if (fill != null) fill.fillAmount = target.maxStamina > 0f ? target.currentStamina / target.maxStamina : 0f;
        if (label != null) label.text = Mathf.CeilToInt(target.currentStamina) + "/" + Mathf.CeilToInt(target.maxStamina);
    }
}
