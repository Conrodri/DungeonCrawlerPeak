using UnityEngine;
using UnityEngine.UI;

// Right-side column, below the minimap: a live level per skill (see PlayerSkills). Same
// always-visible, refresh-every-frame approach as StatsUI, just without icons - nothing was drawn
// for these yet and a plain label reads fine at this size.
public class SkillsUI : MonoBehaviour
{
    public PlayerSkills skills;

    // Minimap sits top-right, anchored at (-20,-20) and growing up to maxPanelSize (320) tall - this
    // starts well below its worst case instead of guessing a size that might overlap it.
    public float yOffset = -370f;
    public float rowSpacing = 40f;
    public int fontSize = 26;
    [Range(0f, 1f)] public float alpha = 0.85f;

    static readonly SkillType[] Types = { SkillType.Sprint, SkillType.Melee, SkillType.Ranged, SkillType.Roll };
    static readonly string[] Labels = { "Sprint", "Melee", "Tir", "Roulade" };

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
        Color tint = new Color(1f, 1f, 1f, alpha);

        for (int i = 0; i < Labels.Length; i++)
        {
            float y = yOffset - i * rowSpacing;

            GameObject textGO = new GameObject(Labels[i] + "SkillText", typeof(Text));
            textGO.transform.SetParent(transform, false);
            Text text = textGO.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleRight;
            text.color = tint;
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(1f, 1f);
            textRt.anchoredPosition = new Vector2(-20f, y);
            textRt.sizeDelta = new Vector2(260f, 36f);

            valueTexts[i] = text;
        }
    }

    void Refresh()
    {
        if (skills == null || valueTexts == null) return;
        for (int i = 0; i < Types.Length; i++)
        {
            int level = skills.GetLevel(Types[i]);
            valueTexts[i].text = Labels[i] + " Nv." + level + (level >= PlayerSkills.MaxLevel ? " (max)" : "");
        }
    }
}
