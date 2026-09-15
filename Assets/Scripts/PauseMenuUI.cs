using UnityEngine;
using UnityEngine.InputSystem;

// Escape-triggered in-game pause menu: unlike MainMenuController (only reachable before/after a
// run), this exists on a live floor so the player can adjust volume or bail out to the main menu
// mid-run without needing to die first (see DeathScreenUI, which covers that other case).
// Time.timeScale freezes enemies/timers/physics while open - MonoBehaviour.Update() itself is
// never scaled, so Escape keeps responding even at timeScale 0.
public class PauseMenuUI : MonoBehaviour
{
    public GameObject root;
    public GameObject settingsPanel;
    public MainMenuController mainMenu;

    bool isPaused;

    void OnDestroy()
    {
        // Never leave the game frozen behind - e.g. quitting to the menu while paused, or this
        // floor's DungeonRoot being torn down for any other reason.
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

        if (settingsPanel != null && settingsPanel.activeSelf)
        {
            settingsPanel.SetActive(false);
            return;
        }

        if (isPaused)
        {
            SetPaused(false);
            return;
        }

        // Any other window open (dialogue, inspect panel, chest, corpse, inventory, repair,
        // attribute allocation, shop...) - close the most recently opened one instead of opening
        // the pause menu underneath it (explicit request).
        if (UIWindowStack.CloseTop()) return;

        SetPaused(true);
    }

    void SetPaused(bool paused)
    {
        isPaused = paused;
        if (root != null) root.SetActive(paused);
        Time.timeScale = paused ? 0f : 1f;
    }

    public void Resume() => SetPaused(false);

    public void ToggleSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    public void QuitToMenu()
    {
        Time.timeScale = 1f;
        if (mainMenu != null) mainMenu.ReturnToMenu();
    }
}
