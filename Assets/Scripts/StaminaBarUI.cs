using UnityEngine;
using UnityEngine.UI;

// A small always-visible bar under the hearts - drains while sprinting (PlayerController), refills
// on its own once the player lets off Shift for a moment (see Stamina.regenDelay). Also carries a
// numeric readout: the bar alone was hard to read at a glance (small, easy for a short sprint tap
// to fully regenerate before it's noticed), so the exact numbers make any change unambiguous.
//
// Polls every frame instead of subscribing to Stamina.OnStaminaChanged (same reasoning as
// CharacterSheetUI) - a Play Mode domain reload (e.g. a script recompile while already playing) silently
// drops C# event subscriptions without Start() ever re-running to resubscribe, which left this
// bar frozen. Two floats read every frame is free enough that there's no reason to risk that.
public class StaminaBarUI : MonoBehaviour
{
    public Stamina target;
    public Image fill;
    public Text label;

    // Temporary diagnostic - reported as still not updating even after switching to polling,
    // which the reflection/eval investigation couldn't reproduce (Unity's Player loop stalls
    // without OS focus when driven remotely, so that testing path is unreliable here - see
    // feedback_live_editor_playmode_check in memory). This logs from inside a REAL play session
    // instead, once every couple seconds, so the next report can say exactly what it printed.
    float lastLogTime = -999f;

    void Update()
    {
        if (target == null) return;
        float fillValue = target.maxStamina > 0f ? target.currentStamina / target.maxStamina : 0f;
        if (fill != null) fill.fillAmount = fillValue;
        if (label != null) label.text = Mathf.CeilToInt(target.currentStamina) + "/" + Mathf.CeilToInt(target.maxStamina);

        if (Time.time - lastLogTime > 2f)
        {
            lastLogTime = Time.time;
            Debug.Log("[StaminaBarUI] current=" + target.currentStamina + " max=" + target.maxStamina
                + " fillAmount=" + fillValue + " fillActive=" + (fill != null && fill.gameObject.activeInHierarchy)
                + " selfActive=" + gameObject.activeInHierarchy);
        }
    }
}
