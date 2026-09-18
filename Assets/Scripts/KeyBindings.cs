using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Every action a player can rebind from the new Settings > Touches page (see RebindKeysUI).
// Escape/Tab/dialogue's 1-9 choice keys stay fixed on purpose - Escape is the universal
// close-everything convention (see UIWindowStack) and rebinding it would break that, Tab is just
// Inventory's secondary shortcut, and dialogue's number keys are contextual (only read while a
// conversation is open, never at the same time as the hotbar).
public enum GameAction
{
    MoveUp, MoveDown, MoveLeft, MoveRight,
    Sprint, Roll, SwitchHand,
    AimUp, AimDown, AimLeft, AimRight,
    Interact, Inventory, CharacterSheet, SpellBook,
    Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5,
    Spell1, Spell2, Spell3, Spell4, Spell5,
}

// Central rebindable keymap, persisted in PlayerPrefs (a device preference, unlike SaveManager's
// per-run save) - every gameplay script that used to poll Keyboard.current.xKey directly now goes
// through IsPressed/WasPressedThisFrame here instead, so a rebind takes effect everywhere at once.
public static class KeyBindings
{
    static readonly Dictionary<GameAction, Key> Defaults = new Dictionary<GameAction, Key>
    {
        { GameAction.MoveUp, Key.W },
        { GameAction.MoveDown, Key.S },
        { GameAction.MoveLeft, Key.A },
        { GameAction.MoveRight, Key.D },
        { GameAction.Sprint, Key.LeftShift },
        { GameAction.Roll, Key.Space },
        { GameAction.SwitchHand, Key.H },
        { GameAction.AimUp, Key.UpArrow },
        { GameAction.AimDown, Key.DownArrow },
        { GameAction.AimLeft, Key.LeftArrow },
        { GameAction.AimRight, Key.RightArrow },
        { GameAction.Interact, Key.E },
        { GameAction.Inventory, Key.I },
        { GameAction.CharacterSheet, Key.C },
        { GameAction.SpellBook, Key.K },
        { GameAction.Hotbar1, Key.Digit1 },
        { GameAction.Hotbar2, Key.Digit2 },
        { GameAction.Hotbar3, Key.Digit3 },
        { GameAction.Hotbar4, Key.Digit4 },
        { GameAction.Hotbar5, Key.Digit5 },
        { GameAction.Spell1, Key.Z },
        { GameAction.Spell2, Key.X },
        { GameAction.Spell3, Key.C },
        { GameAction.Spell4, Key.V },
        { GameAction.Spell5, Key.B },
    };

    static Dictionary<GameAction, Key> current;

    static void EnsureLoaded()
    {
        if (current != null) return;
        current = new Dictionary<GameAction, Key>(Defaults);
        foreach (GameAction action in Defaults.Keys)
        {
            string saved = PlayerPrefs.GetString(PrefKey(action), "");
            if (!string.IsNullOrEmpty(saved) && System.Enum.TryParse(saved, out Key key)) current[action] = key;
        }
    }

    static string PrefKey(GameAction action) => "KeyBind_" + action;

    public static IEnumerable<GameAction> AllActions => Defaults.Keys;

    public static Key Get(GameAction action)
    {
        EnsureLoaded();
        return current[action];
    }

    public static void Set(GameAction action, Key key)
    {
        EnsureLoaded();
        current[action] = key;
        PlayerPrefs.SetString(PrefKey(action), key.ToString());
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        current = new Dictionary<GameAction, Key>(Defaults);
        foreach (GameAction action in Defaults.Keys) PlayerPrefs.DeleteKey(PrefKey(action));
        PlayerPrefs.Save();
    }

    // Keyboard layout-aware where possible ("Z" on AZERTY for what's bound to Key.W) so the label
    // always matches what the player actually has to press, not the US-QWERTY key name.
    public static string Label(GameAction action)
    {
        Key key = Get(action);
        var kb = Keyboard.current;
        string display = kb != null ? kb[key].displayName : null;
        return string.IsNullOrEmpty(display) ? key.ToString() : display.ToUpperInvariant();
    }

    public static bool IsPressed(GameAction action)
    {
        var kb = Keyboard.current;
        return kb != null && kb[Get(action)].isPressed;
    }

    public static bool WasPressedThisFrame(GameAction action)
    {
        var kb = Keyboard.current;
        return kb != null && kb[Get(action)].wasPressedThisFrame;
    }
}
