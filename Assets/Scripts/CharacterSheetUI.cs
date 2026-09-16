using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Read-only recap of all 8 raw stats + level/XP, toggled with C (2026-09-16 request) - distinct
// from StatsUI (a permanent, near-invisible watermark column showing only 7 of 8 stats with no
// explanation of what they DO) and from AttributeAllocationUI (lets you SPEND points, also only
// 7 of 8 - Vitesse is deliberately unspendable, see its own comment). This is the one place that
// shows the complete picture, each stat next to the exact live gameplay effect it drives, reusing
// PlayerStats' own multiplier properties rather than re-deriving the formulas here.
public class CharacterSheetUI : MonoBehaviour, UIWindowStack.IWindow
{
    public PlayerStats stats;

    GameObject panel;
    Text bodyText;
    bool isOpen;

    void Start()
    {
        BuildUI();
    }

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.cKey.wasPressedThisFrame) return;
        if (isOpen) Close();
        else Open();
    }

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 32);

        panel = new GameObject("CharacterSheetPanel", typeof(RectTransform), typeof(Image));
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
        title.text = "Caracteristiques";
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
        bodyRect.sizeDelta = new Vector2(760f, 620f);

        GameObject closeGO = new GameObject("CloseHint", typeof(Text));
        closeGO.transform.SetParent(panel.transform, false);
        Text closeHint = closeGO.GetComponent<Text>();
        closeHint.text = "[C] ou [ECHAP] pour fermer";
        closeHint.font = font;
        closeHint.fontSize = 22;
        closeHint.alignment = TextAnchor.MiddleCenter;
        closeHint.color = new Color(0.7f, 0.7f, 0.75f);
        RectTransform closeRect = closeHint.rectTransform;
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
        if (stats == null || bodyText == null) return;

        string pointsLine = stats.unspentAttributePoints > 0
            ? stats.unspentAttributePoints + " point(s) non depense(s) - voir le Tavernier\n\n"
            : "\n";

        bodyText.text =
            "Niveau " + stats.level + "   (XP " + stats.experience + "/" + stats.experienceToNextLevel + ")\n" +
            pointsLine +
            Row("Force", stats.force, PercentDelta(stats.PhysicalDamageMultiplier) + " degats physiques, " + PercentDelta(stats.AttackStaminaCostMultiplier) + " cout endurance attaque") +
            Row("Dexterite", stats.dexterite, PercentDelta(stats.AttackSpeedMultiplier) + " vitesse d'attaque, " + Mathf.RoundToInt(stats.DodgeChance * 100f) + "% esquive") +
            Row("Intelligence", stats.intelligence, PercentDelta(stats.MagicDamageMultiplier) + " degats magiques") +
            Row("Vitesse", stats.vitesse, "deplacement x" + stats.MoveSpeedMultiplier.ToString("0.00")) +
            Row("Constitution", stats.constitution, "+" + stats.constitution * 6 + " PV max (+1 par membre)") +
            Row("Portee", stats.portee, "portee x" + stats.RangeMultiplier.ToString("0.00")) +
            Row("Charisme", stats.charisme, PercentDelta(stats.ShopPriceMultiplier) + " prix boutique") +
            Row("Endurance", stats.endurance, "+" + Mathf.RoundToInt(stats.endurance * 8f) + " endurance max");
    }

    static string Row(string label, int value, string effect) => label + " : " + value + "  (" + effect + ")\n";

    // multiplier is always centered on 1 (e.g. 1.05 = +5%, 0.98 = -2%) for every caller here -
    // DodgeChance is the one PlayerStats property that ISN'T (a raw 0..1 fraction), so Refresh()
    // formats that one inline instead of routing it through here.
    static string PercentDelta(float multiplier) => (multiplier >= 1f ? "+" : "") + Mathf.RoundToInt((multiplier - 1f) * 100f) + "%";
}
