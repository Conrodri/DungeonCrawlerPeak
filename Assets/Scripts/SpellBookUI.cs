using UnityEngine;
using UnityEngine.UI;

// Grimoire des sorts, toggled with K - same shape as CharacterSheetUI (read-only recap panel,
// pushed on UIWindowStack). Only one spell exists today (Orbe de Foudre, see PlayerController) but
// this is where every future spell gets listed, each with its own key/cout/degats/cooldown.
public class SpellBookUI : MonoBehaviour, UIWindowStack.IWindow
{
    public PlayerController controller;
    public Mana mana;
    public PlayerStats stats;

    GameObject panel;
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
        bodyRect.anchoredPosition = new Vector2(0f, -160f);
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
            int damage = stats != null
                ? Mathf.RoundToInt(controller.lightningOrbDamage * stats.MagicDamageMultiplier)
                : controller.lightningOrbDamage;

            text +=
                "Orbe de Foudre [" + KeyBindings.Label(GameAction.Spell1) + "]\n" +
                "Cout : " + controller.lightningOrbManaCost + " mana\n" +
                "Degats : " + damage + " (rebondit sur les ennemis a moins de " + controller.lightningOrbChainRadius.ToString("0.#") + "m de l'impact)\n" +
                "Portee : " + controller.lightningOrbRange.ToString("0.#") + "\n" +
                "Recharge : " + controller.lightningOrbCooldown.ToString("0.#") + "s\n";
        }
        else
        {
            text += "Aucun sort connu.\n";
        }

        bodyText.text = text;
    }
}
