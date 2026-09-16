using UnityEngine;
using UnityEngine.UI;

// Left-side column, below the gold counter: an icon + live value per stat, all 8. Refreshes every
// frame - cheap for eight short strings, and keeps this correct even when a dialogue outcome
// changes a stat mid-run without needing a change-notification event on PlayerStats.
public class StatsUI : MonoBehaviour
{
    public PlayerStats stats;
    public Sprite constitutionIcon;
    public Sprite forceIcon;
    public Sprite agiliteIcon;
    public Sprite intelligenceIcon;
    public Sprite vitesseIcon;
    public Sprite porteeIcon;
    public Sprite charismeIcon;
    public Sprite enduranceIcon;

    public float yOffset = -135f;
    public float rowSpacing = 56f;
    public float iconSize = 40f;
    public int fontSize = 32;
    [Range(0f, 1f)] public float alpha = 0.3f;

    // Label/StatType kept as two parallel arrays instead of hand-written per-stat lines in
    // Refresh() (was: valueTexts[2].text = Labels[2] + ": " + stats.dexterite, where Labels[2]
    // actually read "Agilite" and Portee wasn't listed at all - a real display bug, found
    // 2026-09-16, that this shape can't reproduce since the label and the value now come from the
    // same index into the same PlayerStats.GetStat(StatType) lookup every other stat consumer uses).
    static readonly string[] Labels = { "Constitution", "Force", "Dexterite", "Intelligence", "Vitesse", "Portee", "Charisme", "Endurance" };
    static readonly StatType[] Types = { StatType.Constitution, StatType.Force, StatType.Dexterite, StatType.Intelligence, StatType.Vitesse, StatType.Portee, StatType.Charisme, StatType.Endurance };

    Text[] valueTexts;

    void Start()
    {
        BuildUI();
        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    void BuildUI()
    {
        Sprite[] icons = { constitutionIcon, forceIcon, agiliteIcon, intelligenceIcon, vitesseIcon, porteeIcon, charismeIcon, enduranceIcon };
        valueTexts = new Text[Labels.Length];
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
        Color tint = new Color(1f, 1f, 1f, alpha);

        for (int i = 0; i < Labels.Length; i++)
        {
            float y = yOffset - i * rowSpacing;

            GameObject iconGO = new GameObject(Labels[i] + "Icon", typeof(Image));
            iconGO.transform.SetParent(transform, false);
            Image iconImg = iconGO.GetComponent<Image>();
            iconImg.sprite = icons[i];
            iconImg.color = tint;
            iconImg.preserveAspect = true;
            RectTransform iconRt = iconImg.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(20f, y);
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);

            GameObject textGO = new GameObject(Labels[i] + "Text", typeof(Text));
            textGO.transform.SetParent(transform, false);
            Text text = textGO.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = tint;
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = textRt.anchorMax = new Vector2(0f, 1f);
            textRt.pivot = new Vector2(0f, 1f);
            textRt.anchoredPosition = new Vector2(20f + iconSize + 12f, y);
            textRt.sizeDelta = new Vector2(260f, iconSize);

            valueTexts[i] = text;
        }
    }

    void Refresh()
    {
        if (stats == null || valueTexts == null) return;
        for (int i = 0; i < Labels.Length; i++)
            valueTexts[i].text = Labels[i] + ": " + stats.GetStat(Types[i]);
    }
}
