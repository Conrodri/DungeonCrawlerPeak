using UnityEngine;
using UnityEngine.UI;

// Bound at runtime by BossRoomController.Start() - never subscribe to Health's events from
// DungeonBootstrap (Editor-time), that subscription would be lost on the Play Mode scene reload.
//
// The fill itself now polls every frame instead of relying on Health.OnHealthChanged, same
// reasoning as StaminaBarUI: a Play Mode domain reload silently drops C# event subscriptions
// without Start() ever re-running to resubscribe, which left this bar frozen mid-fight. OnDeath
// stays event-based since it's a one-shot terminal state raised at most once per boss.
public class BossHealthBarUI : MonoBehaviour
{
    public GameObject root;
    public Image fill;

    Health target;
    // Temporary diagnostic - see StaminaBarUI.lastLogTime.
    float lastLogTime = -999f;

    public void Bind(Health health)
    {
        target = health;
        if (target == null) return;

        target.OnDeath += HandleDeath;
        if (root != null) root.SetActive(true);
        Debug.Log("[BossHealthBarUI] Bind called, target=" + health.name + " root active=" + (root != null && root.activeInHierarchy));
    }

    void Update()
    {
        if (target == null || fill == null) return;
        float fillValue = target.maxHealth > 0 ? (float)target.currentHealth / target.maxHealth : 0f;
        fill.fillAmount = fillValue;

        if (Time.time - lastLogTime > 2f)
        {
            lastLogTime = Time.time;
            Debug.Log("[BossHealthBarUI] hp=" + target.currentHealth + "/" + target.maxHealth
                + " fillAmount=" + fillValue + " fillActive=" + fill.gameObject.activeInHierarchy
                + " rootActive=" + (root != null && root.activeInHierarchy));
        }
    }

    void HandleDeath()
    {
        if (root != null) root.SetActive(false);
    }
}
