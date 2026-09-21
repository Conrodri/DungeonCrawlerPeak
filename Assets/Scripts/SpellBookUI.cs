using UnityEngine;
using UnityEngine.UI;

// Grimoire des sorts, toggled with K - same shape as CharacterSheetUI (a recap panel, pushed on
// UIWindowStack), plus a row of clickable spell chips (2026-09-21 request: "sorts... ajoutable a
// la barre depuis l'interface des sorts") - clicking a spell here adds it to the first empty
// PlayerController.spellSlots slot, or removes it if it's already on the bar. Every spell the
// player knows is listed (see KnownSpellIds) - there's no "unlock" system today, so this is
// currently all 3 spells that exist (SpellIds), always.
public class SpellBookUI : MonoBehaviour, UIWindowStack.IWindow
{
    static readonly string[] KnownSpellIds = { SpellIds.LightningOrb, SpellIds.Fireball, SpellIds.FireLine };
    static readonly string[] KnownSpellNames = { "Orbe de Foudre", "Boule de Feu", "Ligne de Feu" };

    static readonly Color ChipOnBarColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    static readonly Color ChipOffBarColor = new Color(1f, 1f, 1f, 0.15f);

    public PlayerController controller;
    public Mana mana;
    public PlayerStats stats;

    GameObject panel;
    Image[] chipBackgrounds;
    Text bodyText;
    Text closeHintText;
    bool isOpen;

    void Start()
    {
        BuildUI();
    }

    void Update()
    {
        if (!KeyBindings.WasPressedThisFrame(GameAction.SpellBook)) return;
        if (isOpen) Close();
        else Open();
    }

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 32);

        panel = new GameObject("SpellBookPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 0.9f);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text title = titleGO.GetComponent<Text>();
        title.text = "Grimoire";
        title.font = font;
        title.fontSize = 44;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -70f);
        titleRect.sizeDelta = new Vector2(800f, 70f);

        // --- Spell chips: click to add/remove from the bar (PlayerController.spellSlots) ---
        Font chipFont = Font.CreateDynamicFontFromOSFont("Arial", 18);
        chipBackgrounds = new Image[KnownSpellIds.Length];
        float chipWidth = 220f;
        float chipSpacing = 16f;
        float chipsTotalWidth = KnownSpellIds.Length * chipWidth + (KnownSpellIds.Length - 1) * chipSpacing;
        for (int i = 0; i < KnownSpellIds.Length; i++)
        {
            string spellId = KnownSpellIds[i];
            GameObject chipGO = new GameObject("SpellChip" + i, typeof(Image));
            chipGO.transform.SetParent(panel.transform, false);
            Image chipBg = chipGO.GetComponent<Image>();
            chipBackgrounds[i] = chipBg;
            RectTransform chipRect = chipBg.rectTransform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(0.5f, 1f);
            chipRect.pivot = new Vector2(0.5f, 1f);
            float x = -chipsTotalWidth / 2f + i * (chipWidth + chipSpacing) + chipWidth / 2f;
            chipRect.anchoredPosition = new Vector2(x, -150f);
            chipRect.sizeDelta = new Vector2(chipWidth, 44f);

            GameObject chipLabelGO = new GameObject("Label", typeof(Text));
            chipLabelGO.transform.SetParent(chipGO.transform, false);
            Text chipLabel = chipLabelGO.GetComponent<Text>();
            chipLabel.text = KnownSpellNames[i];
            chipLabel.font = chipFont;
            chipLabel.fontSize = 18;
            chipLabel.alignment = TextAnchor.MiddleCenter;
            chipLabel.color = Color.white;
            chipLabel.raycastTarget = false;
            RectTransform chipLabelRect = chipLabel.rectTransform;
            chipLabelRect.anchorMin = Vector2.zero;
            chipLabelRect.anchorMax = Vector2.one;
            chipLabelRect.offsetMin = Vector2.zero;
            chipLabelRect.offsetMax = Vector2.zero;

            chipGO.AddComponent<SimpleClickButton>().onClick = () => ToggleSpellOnBar(spellId);
        }

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(panel.transform, false);
        bodyText = bodyGO.GetComponent<Text>();
        bodyText.font = font;
        bodyText.fontSize = 26;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.color = Color.white;
        bodyText.lineSpacing = 1.25f;
        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -220f);
        bodyRect.sizeDelta = new Vector2(760f, 720f);

        GameObject closeGO = new GameObject("CloseHint", typeof(Text));
        closeGO.transform.SetParent(panel.transform, false);
        closeHintText = closeGO.GetComponent<Text>();
        closeHintText.font = font;
        closeHintText.fontSize = 22;
        closeHintText.alignment = TextAnchor.MiddleCenter;
        closeHintText.color = new Color(0.7f, 0.7f, 0.75f);
        RectTransform closeRect = closeHintText.rectTransform;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
        closeRect.pivot = new Vector2(0.5f, 0f);
        closeRect.anchoredPosition = new Vector2(0f, 40f);
        closeRect.sizeDelta = new Vector2(500f, 40f);

        panel.SetActive(false);
    }

    void Open()
    {
        isOpen = true;
        panel.SetActive(true);
        UIWindowStack.Push(this);
        if (closeHintText != null) closeHintText.text = "[" + KeyBindings.Label(GameAction.SpellBook) + "] ou [ECHAP] pour fermer";
        Refresh();
    }

    void Close()
    {
        isOpen = false;
        panel.SetActive(false);
        UIWindowStack.Remove(this);
    }

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        Close();
        return true;
    }

    void Refresh()
    {
        if (bodyText == null) return;

        string manaLine = mana != null
            ? "Mana : " + Mathf.CeilToInt(mana.currentMana) + " / " + Mathf.CeilToInt(mana.maxMana) + "\n\n"
            : "\n";

        string text = manaLine;

        if (controller != null)
        {
            float magicMultiplier = stats != null ? stats.MagicDamageMultiplier : 1f;
            int lightningDamage = Mathf.RoundToInt(controller.lightningOrbDamage * magicMultiplier);
            int fireballDamage = Mathf.RoundToInt(controller.fireballDamage * magicMultiplier);
            int burnDamage = Mathf.RoundToInt(controller.burnDamagePerTick * magicMultiplier);

            text +=
                "Orbe de Foudre [" + SlotKeyLabel(SpellIds.LightningOrb) + "]\n" +
                "Cout : " + controller.lightningOrbManaCost + " mana\n" +
                "Degats : " + lightningDamage + " (rebondit sur les ennemis a moins de " + controller.lightningOrbChainRadius.ToString("0.#") + "m de l'impact)\n" +
                "Portee : " + controller.lightningOrbRange.ToString("0.#") + "\n" +
                "Recharge : " + controller.lightningOrbCooldown.ToString("0.#") + "s\n\n" +

                "Boule de Feu [" + SlotKeyLabel(SpellIds.Fireball) + "]\n" +
                "Cout : " + controller.fireballManaCost + " mana\n" +
                "Degats : " + fireballDamage + " (grossit " + controller.fireballChargeDuration.ToString("0.#") + "s avant de partir)\n" +
                "Portee : " + controller.fireballRange.ToString("0.#") + "\n" +
                "Recharge : " + controller.fireballCooldown.ToString("0.#") + "s\n\n" +

                "Ligne de Feu [" + SlotKeyLabel(SpellIds.FireLine) + "]\n" +
                "Cout : " + controller.fireLineManaCost + " mana\n" +
                "Laisse une trainee de feu pendant " + controller.fireLineLifetime.ToString("0.#") + "s\n" +
                "Brulure : " + burnDamage + " degats/" + controller.burnTickInterval.ToString("0.#") + "s pendant " + controller.burnDuration.ToString("0.#") + "s (se renouvelle tant que la cible reste dans le feu)\n" +
                "Portee : " + controller.fireLineRange.ToString("0.#") + "\n" +
                "Recharge : " + controller.fireLineCooldown.ToString("0.#") + "s\n";
        }
        else
        {
            text += "Aucun sort connu.\n";
        }

        bodyText.text = text;

        if (chipBackgrounds != null)
        {
            for (int i = 0; i < KnownSpellIds.Length; i++)
            {
                bool onBar = controller != null && System.Array.IndexOf(controller.spellSlots, KnownSpellIds[i]) >= 0;
                chipBackgrounds[i].color = onBar ? ChipOnBarColor : ChipOffBarColor;
            }
        }
    }

    // A spell's key label now depends on WHICH bar slot it's been clicked into (any spell can sit
    // in any of the 5 slots, or none) - "non assigne" when it isn't on the bar at all.
    static readonly GameAction[] SlotActions = { GameAction.Spell1, GameAction.Spell2, GameAction.Spell3, GameAction.Spell4, GameAction.Spell5 };

    string SlotKeyLabel(string spellId)
    {
        if (controller == null) return "non assigne";
        for (int i = 0; i < controller.spellSlots.Length; i++)
        {
            if (controller.spellSlots[i] == spellId) return KeyBindings.Label(SlotActions[i]);
        }
        return "non assigne";
    }

    // Click a chip: already on the bar -> remove it; otherwise drop it into the first empty slot
    // (or do nothing if all 5 are full - no eviction, the player un-slots something first).
    void ToggleSpellOnBar(string spellId)
    {
        if (controller == null) return;

        for (int i = 0; i < controller.spellSlots.Length; i++)
        {
            if (controller.spellSlots[i] == spellId)
            {
                controller.spellSlots[i] = null;
                Refresh();
                return;
            }
        }

        for (int i = 0; i < controller.spellSlots.Length; i++)
        {
            if (string.IsNullOrEmpty(controller.spellSlots[i]))
            {
                controller.spellSlots[i] = spellId;
                Refresh();
                return;
            }
        }
    }
}
