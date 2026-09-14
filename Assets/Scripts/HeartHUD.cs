using UnityEngine;
using UnityEngine.UI;

// A fill bar + numeric readout - replaced individual heart icons on 2026-09-14: per-limb HP (see
// PlayerLimbs) pushed the player's real total into the tens/hundreds (205 base + 6/Constitution
// point), far past what a handful of discrete heart slots could represent. Same polling pattern as
// StaminaBarUI (see its own comment) rather than subscribing to Health.OnHealthChanged - a Play
// Mode domain reload silently drops C# event subscriptions without Start() ever re-running to
// resubscribe, which left event-driven bars frozen before.
public class HeartHUD : MonoBehaviour
{
    public Health target;
    public Image fill;
    public Text label;

    void Update()
    {
        if (target == null) return;
        float fillValue = target.maxHealth > 0 ? (float)target.currentHealth / target.maxHealth : 0f;
        if (fill != null) fill.fillAmount = fillValue;
        if (label != null) label.text = target.currentHealth + "/" + target.maxHealth;
    }
}
