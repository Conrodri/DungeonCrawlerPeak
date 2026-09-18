using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

// Full-screen "Touches" page reachable from the small Settings panel (see
// MainMenuController.BuildSettingsPanel, shared with PauseMenuUI) - one row per GameAction (see
// KeyBindings), click a key button then press any key to rebind it. Same full-screen overlay
// pattern as CharacterSheetUI/SpellBookUI, just not on UIWindowStack since this only ever opens
// from the main/pause menu, never mid-gameplay.
public class RebindKeysUI : MonoBehaviour
{
    GameObject panel;
    readonly Dictionary<GameAction, Text> keyTexts = new Dictionary<GameAction, Text>();
    GameAction? listening;

    static readonly (GameAction action, string label)[] Column1 =
    {
        (GameAction.MoveUp, "Haut"),
        (GameAction.MoveDown, "Bas"),
        (GameAction.MoveLeft, "Gauche"),
        (GameAction.MoveRight, "Droite"),
        (GameAction.Sprint, "Sprint"),
        (GameAction.Roll, "Roulade"),
        (GameAction.SwitchHand, "Changer de main"),
        (GameAction.Interact, "Interagir"),
        (GameAction.Inventory, "Inventaire"),
        (GameAction.CharacterSheet, "Caracteristiques"),
    };

    static readonly (GameAction action, string label)[] Column2 =
    {
        (GameAction.AimUp, "Viser/Lancer Haut"),
        (GameAction.AimDown, "Viser/Lancer Bas"),
        (GameAction.AimLeft, "Viser/Lancer Gauche"),
        (GameAction.AimRight, "Viser/Lancer Droite"),
        (GameAction.SpellBook, "Grimoire"),
    };

    static readonly (GameAction action, string label)[] Column3 =
    {
        (GameAction.Hotbar1, "Objet 1"),
        (GameAction.Hotbar2, "Objet 2"),
        (GameAction.Hotbar3, "Objet 3"),
        (GameAction.Hotbar4, "Objet 4"),
        (GameAction.Hotbar5, "Objet 5"),
        (GameAction.Spell1, "Sort 1"),
        (GameAction.Spell2, "Sort 2"),
        (GameAction.Spell3, "Sort 3"),
        (GameAction.Spell4, "Sort 4"),
        (GameAction.Spell5, "Sort 5"),
    };

    public static RebindKeysUI Build(Transform parent, Font font)
    {
        GameObject go = new GameObject("RebindKeysUI", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        RebindKeysUI ui = go.AddComponent<RebindKeysUI>();
        ui.BuildUI(font);
        return ui;
    }

    void BuildUI(Font font)
    {
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 0.97f);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text title = titleGO.GetComponent<Text>();
        title.text = "Touches";
        title.font = font;
        title.fontSize = 40;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -60f);
        titleRect.sizeDelta = new Vector2(600f, 60f);

        BuildColumn(font, Column1, -380f);
        BuildColumn(font, Column2, 0f);
        BuildColumn(font, Column3, 380f);

        MainMenuController.CreateButton(panel.transform, "Reinitialiser", font, -640f, ResetAll);
        MainMenuController.CreateButton(panel.transform, "Retour", font, -720f, Close);

        panel.SetActive(false);
    }

    void BuildColumn(Font font, (GameAction action, string label)[] rows, float columnX)
    {
        float y = -150f;
        foreach (var (action, label) in rows)
        {
            BuildRow(font, action, label, columnX, y);
            y -= 46f;
        }
    }

    void BuildRow(Font font, GameAction action, string label, float columnX, float y)
    {
        GameObject labelGO = new GameObject(action + "Label", typeof(Text));
        labelGO.transform.SetParent(panel.transform, false);
        Text labelText = labelGO.GetComponent<Text>();
        labelText.text = label;
        labelText.font = font;
        labelText.fontSize = 22;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(columnX - 90f, y);
        labelRect.sizeDelta = new Vector2(220f, 36f);

        GameObject buttonGO = new GameObject(action + "Button", typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(panel.transform, false);
        Image img = buttonGO.GetComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.24f, 0.9f);
        RectTransform buttonRect = img.rectTransform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(columnX + 100f, y);
        buttonRect.sizeDelta = new Vector2(120f, 36f);

        GameObject buttonTextGO = new GameObject("Text", typeof(Text));
        buttonTextGO.transform.SetParent(buttonGO.transform, false);
        Text buttonText = buttonTextGO.GetComponent<Text>();
        buttonText.font = font;
        buttonText.fontSize = 20;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;
        RectTransform buttonTextRect = buttonText.rectTransform;
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        Button button = buttonGO.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => StartListening(action));

        keyTexts[action] = buttonText;
    }

    void StartListening(GameAction action)
    {
        listening = action;
        keyTexts[action].text = "...";
    }

    void Update()
    {
        if (listening == null) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        // Escape cancels the rebind instead of ever becoming a bound key itself - it stays the
        // one fixed, universal close-everything key (see UIWindowStack).
        if (kb.escapeKey.wasPressedThisFrame)
        {
            RefreshRow(listening.Value);
            listening = null;
            return;
        }

        foreach (KeyControl control in kb.allKeys)
        {
            if (!control.wasPressedThisFrame) continue;
            KeyBindings.Set(listening.Value, control.keyCode);
            RefreshRow(listening.Value);
            listening = null;
            return;
        }
    }

    void RefreshRow(GameAction action)
    {
        keyTexts[action].text = KeyBindings.Label(action);
    }

    void RefreshAll()
    {
        foreach (GameAction action in keyTexts.Keys) RefreshRow(action);
    }

    void ResetAll()
    {
        KeyBindings.ResetToDefaults();
        RefreshAll();
    }

    public void Open()
    {
        panel.SetActive(true);
        RefreshAll();
    }

    void Close()
    {
        listening = null;
        panel.SetActive(false);
    }
}
