using UnityEngine;
using UnityEngine.UI;

// Bound at runtime by BossRoomController.Start() - never subscribe to Health's events from
// DungeonBootstrap (Editor-time), that subscription would be lost on the Play Mode scene reload.
public class BossHealthBarUI : MonoBehaviour
{
    public GameObject root;
    public Image fill;

    Health target;

    public void Bind(Health health)
    {
        target = health;
        if (target == null) return;

        target.OnHealthChanged += Refresh;
        target.OnDeath += HandleDeath;
        if (root != null) root.SetActive(true);
        Refresh(target.currentHealth, target.maxHealth);
    }

    void Refresh(int current, int max)
    {
        if (fill != null) fill.fillAmount = max > 0 ? (float)current / max : 0f;
    }

    void HandleDeath()
    {
        if (root != null) root.SetActive(false);
    }
}
