using System.Collections.Generic;

// Central registry of every currently open "window" (NPC dialogue, ground-item inspect panel, a
// Chest, a Corpse, the inventory, the repair panel, the attribute allocation panel) - explicit
// request: Escape closes whichever one was opened LAST first, one at a time, and never opens the
// pause menu (see PauseMenuUI) while any of them is still open. Each window pushes itself when it
// opens and removes itself when it closes by ANY means (its own close button/key, an outcome that
// auto-closes it, walking out of range...) so the stack never goes stale.
public static class UIWindowStack
{
    public interface IWindow
    {
        // Called when this window is the top of the stack and Escape was just pressed. Returns
        // true if the window actually closed (and should be popped) - false means it intercepted
        // the key but stayed open (e.g. NpcInteractable dialogue mid dice-roll), which still
        // counts as "handled" so the pause menu doesn't open underneath it.
        bool TryCloseFromStack();
    }

    static readonly List<IWindow> stack = new List<IWindow>();

    public static bool HasOpenWindow => stack.Count > 0;

    public static void Push(IWindow window)
    {
        stack.Remove(window); // never double-register if something reopens without closing first
        stack.Add(window);
    }

    public static void Remove(IWindow window)
    {
        stack.Remove(window);
    }

    // Returns true if an open window intercepted the Escape press (whether or not it actually
    // closed) - the caller (PauseMenuUI) should NOT open the pause menu in that case. False only
    // when the stack was already empty.
    public static bool CloseTop()
    {
        if (stack.Count == 0) return false;
        IWindow top = stack[stack.Count - 1];
        if (top.TryCloseFromStack()) stack.Remove(top);
        return true;
    }
}
