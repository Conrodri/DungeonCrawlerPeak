using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Second bottom bar, sits just above HotbarUI - 5 slots, one per known spell (2026-09-18 request:
// "meme systeme... un sort se lance en le selectionnant puis en cliquant dans une direction", then
// "il nous faut evidemment une touche attribuee" - Z/X/C/V/B by default, see KeyBindings.Spell1-5).
// Clicking a slot (or its key) arms that spell (see PlayerController.ArmSpell/UseSpellSlot) and
// the next arrow-key press casts it, exactly like armedThrowItemId already does for a hotbar
// throwable. Reads which spell sits in each slot from PlayerController.spellSlots - the single
// source of truth, since the keyboard shortcuts need the exact same mapping.
public class SpellBarUI : MonoBehaviour
{
    public const int SlotCount = 5;

    static readonly GameAction[] SlotActions =
    {
        GameAction.Spell1, GameAction.Spell2, GameAction.Spell3, GameAction.Spell4, GameAction.Spell5,
    };

    public PlayerController controller;
    public float slotSize = 40f;
    public float spacing = 48f;
    public float rowYOffset = 70f; // bottom edge, sits directly above HotbarUI's row

    Image[] backgrounds;
    Image[] icons;
    Text[] keyLabels;

    static readonly Color UnarmedColor = new Color(1f, 1f, 1f, 0.15f);
    static readonly Color ArmedColor = new Color(1f, 0.85f, 0.2f, 0.9f);

    void Start()
    {
        BuildSlots();
        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    void BuildSlots()
    {
        backgrounds = new Image[SlotCount];
        icons = new Image[SlotCount];
        keyLabels = new Text[SlotCount];
        Font font = Font.CreateDynamicFontFromOSFont("Arial", 14);

        for (int i = 0; i < SlotCount; i++)
        {
            GameObject slotGO = new GameObject("SpellSlot" + (i + 1), typeof(Image));
            slotGO.transform.SetParent(transform, false);

            Image bg = slotGO.GetComponent<Image>();
            backgrounds[i] = bg;

            RectTransform rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float xOffset = (i - (SlotCount - 1) / 2f) * spacing;
            rt.anchoredPosition = new Vector2(xOffset, rowYOffset + slotSize / 2f);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            GameObject iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(slotGO.transform, false);
            Image icon = iconGO.GetComponent<Image>();
            icon.raycastTarget = false;
            icons[i] = icon;
            RectTransform iconRt = icon.rectTransform;
            iconRt.anchorMin = new Vector2(0.1f, 0.1f);
            iconRt.anchorMax = new Vector2(0.9f, 0.9f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;

            GameObject keyGO = new GameObject("KeyLabel", typeof(Text));
            keyGO.transform.SetParent(slotGO.transform, false);
            Text keyText = keyGO.GetComponent<Text>();
            keyText.font = font;
            keyText.fontSize = 14;
            keyText.fontStyle = FontStyle.Bold;
            keyText.alignment = TextAnchor.UpperLeft;
            keyText.color = new Color(1f, 1f, 1f, 0.85f);
            keyText.raycastTarget = false;
            keyLabels[i] = keyText;
            RectTransform keyRt = keyText.rectTransform;
            keyRt.anchorMin = new Vector2(0f, 1f);
            keyRt.anchorMax = new Vector2(0f, 1f);
            keyRt.pivot = new Vector2(0f, 1f);
            keyRt.anchoredPosition = new Vector2(2f, -2f);
            keyRt.sizeDelta = new Vector2(24f, 16f);

            SpellSlotUI click = slotGO.AddComponent<SpellSlotUI>();
            click.controller = controller;
            click.SlotIndex = i;
        }
    }

    void Refresh()
    {
        if (backgrounds == null || controller == null) return;

        for (int i = 0; i < SlotCount; i++)
        {
            string spellId = controller.spellSlots[i];
            bool hasSpell = !string.IsNullOrEmpty(spellId);
            bool armed = hasSpell && controller.ArmedSpellId == spellId;

            backgrounds[i].color = armed ? ArmedColor : UnarmedColor;
            icons[i].sprite = hasSpell ? controller.IconForSpell(spellId) : null;
            icons[i].color = hasSpell ? Color.white : new Color(1f, 1f, 1f, 0f);
            keyLabels[i].text = hasSpell ? KeyBindings.Label(SlotActions[i]) : "";
        }
    }
}

// Plain click-to-arm handler for a single SpellBarUI slot - no drag & drop, unlike
// InventorySlotUI, since spells aren't reassignable items today. Reads controller.spellSlots by
// index rather than caching the spellId, so it always arms whatever that slot currently holds -
// the same thing its Z/X/C/V/B shortcut (see PlayerController.UseSpellSlot) would.
public class SpellSlotUI : MonoBehaviour, IPointerClickHandler
{
    public PlayerController controller;
    public int SlotIndex;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null) return;
        string spellId = controller.spellSlots[SlotIndex];
        if (string.IsNullOrEmpty(spellId)) return;
        controller.ArmSpell(spellId);
    }
}
