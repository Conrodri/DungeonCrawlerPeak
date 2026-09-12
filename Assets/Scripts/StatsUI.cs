using UnityEngine;
using UnityEngine.UI;

// Left-side column, below the gold counter: an icon glyph + live value per stat. Refreshes every
// frame - cheap for six short strings, and keeps this correct even when a dialogue outcome changes
// a stat mid-run without needing a change-notification event on PlayerStats.
public class StatsUI : MonoBehaviour
{
    public PlayerStats stats;

    public float yOffset = -135f;
    public float rowSpacing = 56f;
    public int iconFontSize = 36;
    public int fontSize = 32;
    [Range(0f, 1f)] public float alpha = 0.5f;

    // Native Unicode glyphs instead of hand-drawn sprites - no texture/import step needed.
    static readonly string[] Icons = { "❤", "⚔", "†", "ⓘ", "⚡", "★" }; // heart, crossed swords, dagger, info circle, lightning bolt, star
    static readonly string[] Labels = { "Constitution", "Force", "Agilite", "Intelligence", "Vitesse", "Charisme" };

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
        valueTexts = new Text[Labels.Length];
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
        Color textColor = new Color(1f, 1f, 1f, alpha);

        for (int i = 0; i < Labels.Length; i++)
        {
            float y = yOffset - i * rowSpacing;

            GameObject iconGO = new GameObject(Labels[i] + "Icon", typeof(Text));
            iconGO.transform.SetParent(transform, false);
            Text iconText = iconGO.GetComponent<Text>();
            iconText.font = font;
            iconText.fontSize = iconFontSize;
            iconText.alignment = TextAnchor.MiddleCenter;
            iconText.color = textColor;
            iconText.text = Icons[i];
            RectTransform iconRt = iconText.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(20f, y);
            iconRt.sizeDelta = new Vector2(iconFontSize + 4f, rowSpacing);

            GameObject textGO = new GameObject(Labels[i] + "Text", typeof(Text));
            textGO.transform.SetParent(transform, false);
            Text text = textGO.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = textColor;
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = textRt.anchorMax = new Vector2(0f, 1f);
            textRt.pivot = new Vector2(0f, 1f);
            textRt.anchoredPosition = new Vector2(20f + iconFontSize + 16f, y);
            textRt.sizeDelta = new Vector2(260f, rowSpacing);

            valueTexts[i] = text;
        }
    }

    void Refresh()
    {
        if (stats == null || valueTexts == null) return;
        valueTexts[0].text = Labels[0] + ": " + stats.constitution;
        valueTexts[1].text = Labels[1] + ": " + stats.force;
        valueTexts[2].text = Labels[2] + ": " + stats.dexterite;
        valueTexts[3].text = Labels[3] + ": " + stats.intelligence;
        valueTexts[4].text = Labels[4] + ": " + stats.vitesse;
        valueTexts[5].text = Labels[5] + ": " + stats.charisme;
    }
}
