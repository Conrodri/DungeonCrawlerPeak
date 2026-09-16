using UnityEngine;
using UnityEngine.UI;

// A small always-visible bar under the stamina bar - drains when casting a spell (Orbe de Foudre
// for now, see PlayerController.TryCastLightningOrb), refills on its own once the player stops
// spending it for a moment (see Mana.regenDelay). Same numeric-readout-plus-bar shape as
// StaminaBarUI, and polls every frame for the same reason (a Play Mode domain reload silently
// drops C# event subscriptions without Start() ever re-running to resubscribe).
public class ManaBarUI : MonoBehaviour
{
    public Mana target;
    public Image fill;
    public Text label;

    void Update()
    {
        if (target == null) return;
        float fillValue = target.maxMana > 0f ? target.currentMana / target.maxMana : 0f;
        if (fill != null) fill.fillAmount = fillValue;
        if (label != null) label.text = Mathf.CeilToInt(target.currentMana) + "/" + Mathf.CeilToInt(target.maxMana);
    }
}
