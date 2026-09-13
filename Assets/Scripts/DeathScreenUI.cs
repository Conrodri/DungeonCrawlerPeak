using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Full-screen, non-auto-hiding game-over overlay - unlike VictoryBannerUI (a momentary mid-run
// celebration that hides itself after a few seconds), death is a hard stop. The run's save was
// already deleted by the time this shows (see PlayerController.HandleDeath - saves never survive
// death, anti-save-scumming), so the only way forward is back to the main menu. Follows this
// project's existing keyboard-prompt convention (TutorialNpc/DialogueManager) instead of a mouse
// Button - nothing else in a live floor is mouse-driven.
public class DeathScreenUI : MonoBehaviour
{
    public GameObject root;
    public MainMenuController mainMenu;

    bool shown;

    public void Show()
    {
        if (root != null) root.SetActive(true);
        shown = true;
    }

    void Update()
    {
        if (!shown || Keyboard.current == null) return;
        if (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
        {
            shown = false;
            if (root != null) root.SetActive(false);
            if (mainMenu != null) mainMenu.ReturnToMenu();
        }
    }
}
