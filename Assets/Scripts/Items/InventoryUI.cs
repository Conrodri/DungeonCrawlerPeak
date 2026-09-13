using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    static InventoryUI instance;
    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    // Lets InventorySlotUI close the panel right after a click-to-use throw/potion, so the player
    // actually sees the projectile fly instead of it firing behind a still-open menu.
    public static void CloseIfOpen()
    {
        if (instance != null && instance.panel != null) instance.panel.SetActive(false);
    }

    public PlayerInventory inventory;
    public PlayerEquipment equipment;
    // Lets a plain click on a grid slot use/throw the item directly - see InventorySlotUI.
    public PlayerController player;
    public int columns = 5;
    public float slotSize = 56f;
    public float spacing = 64f;
    public int fontSize = 24;

    GameObject panel;
    Image[] slotImages;
    Text[] slotTexts;

    // Equipment panel bookkeeping - single slots keyed by type, rings keyed by (type, hand index).
    readonly Dictionary<EquipmentSlotType, Image> equipmentImages = new Dictionary<EquipmentSlotType, Image>();
    readonly Dictionary<(EquipmentSlotType, int), Image> ringImages = new Dictionary<(EquipmentSlotType, int), Image>();

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        BuildPanel();
        BuildEquipmentPanel();
        if (inventory != null) inventory.OnInventoryChanged += Refresh;
        if (equipment != null) equipment.OnEquipmentChanged += Refresh;
        Refresh();
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnInventoryChanged -= Refresh;
        if (equipment != null) equipment.OnEquipmentChanged -= Refresh;
        if (instance == this) instance = null;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) panel.SetActive(!panel.activeSelf);
    }

    void BuildPanel()
    {
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        int slotCount = inventory != null ? inventory.SlotCount : 0;
        int rows = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)columns));
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);

        slotImages = new Image[slotCount];
        slotTexts = new Text[slotCount];

        float totalWidth = (columns - 1) * spacing;
        float totalHeight = (rows - 1) * spacing;

        for (int i = 0; i < slotCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            GameObject slotGO = new GameObject("Slot" + i, typeof(Image));
            slotGO.transform.SetParent(panel.transform, false);

            Image img = slotGO.GetComponent<Image>();
            slotImages[i] = img;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Shifted left of center to leave room for the equipment panel on the right.
            rt.anchoredPosition = new Vector2(col * spacing - totalWidth / 2f - 220f, totalHeight / 2f - row * spacing);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            InventorySlotUI slotUI = slotGO.AddComponent<InventorySlotUI>();
            slotUI.inventory = inventory;
            slotUI.kind = InventorySlotKind.Inventory;
            slotUI.index = i;
            slotUI.player = player;

            GameObject textGO = new GameObject("Count", typeof(Text));
            textGO.transform.SetParent(slotGO.transform, false);
            Text text = textGO.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.LowerRight;
            text.color = Color.white;
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            slotTexts[i] = text;
        }
    }

    // Labeled slots to the right of the inventory grid: 7 single-item slots (Head/Shoulders/
    // Gloves/Boots/Neck/Belt/Knees), then two 5-slot ring columns (left hand / right hand) - see
    // EquipmentSlotType/PlayerEquipment.
    void BuildEquipmentPanel()
    {
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize - 6);
        float equipSlotSize = slotSize * 0.85f;
        float baseX = 260f;
        float startY = 260f;
        float rowSpacing = 76f;

        (EquipmentSlotType type, string label)[] singleSlots =
        {
            (EquipmentSlotType.Head, "Casque"),
            (EquipmentSlotType.Shoulders, "Epaulieres"),
            (EquipmentSlotType.Gloves, "Gants"),
            (EquipmentSlotType.Boots, "Bottes"),
            (EquipmentSlotType.Neck, "Cou"),
            (EquipmentSlotType.Belt, "Ceinture"),
            (EquipmentSlotType.Knees, "Genouilleres"),
        };

        for (int i = 0; i < singleSlots.Length; i++)
        {
            float y = startY - i * rowSpacing;
            Image img = BuildEquipmentSlot(singleSlots[i].label, font, new Vector2(baseX, y), equipSlotSize, singleSlots[i].type, 0);
            equipmentImages[singleSlots[i].type] = img;
        }

        float ringsTop = startY - singleSlots.Length * rowSpacing - 40f;
        BuildRingColumn("Anneaux\n(main gauche)", font, new Vector2(baseX - 45f, ringsTop), equipSlotSize, EquipmentSlotType.RingLeft);
        BuildRingColumn("Anneaux\n(main droite)", font, new Vector2(baseX + 45f, ringsTop), equipSlotSize, EquipmentSlotType.RingRight);
    }

    void BuildRingColumn(string label, Font font, Vector2 topCenter, float equipSlotSize, EquipmentSlotType ringSlotType)
    {
        GameObject labelGO = new GameObject(ringSlotType + "Label", typeof(Text));
        labelGO.transform.SetParent(panel.transform, false);
        Text labelText = labelGO.GetComponent<Text>();
        labelText.text = label;
        labelText.font = font;
        labelText.fontSize = fontSize - 10;
        labelText.alignment = TextAnchor.LowerCenter;
        labelText.color = Color.white;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = topCenter + new Vector2(0f, 8f);
        labelRect.sizeDelta = new Vector2(90f, 44f);

        for (int i = 0; i < PlayerEquipment.RingSlotsPerHand; i++)
        {
            Vector2 pos = topCenter + new Vector2(0f, -(i + 1) * (equipSlotSize + 8f));
            Image img = BuildEquipmentSlot(null, font, pos, equipSlotSize, ringSlotType, i);
            ringImages[(ringSlotType, i)] = img;
        }
    }

    Image BuildEquipmentSlot(string label, Font font, Vector2 anchoredPos, float size, EquipmentSlotType slotType, int ringIndex)
    {
        if (!string.IsNullOrEmpty(label))
        {
            GameObject labelGO = new GameObject(label + "Label", typeof(Text));
            labelGO.transform.SetParent(panel.transform, false);
            Text labelText = labelGO.GetComponent<Text>();
            labelText.text = label;
            labelText.font = font;
            labelText.fontSize = fontSize - 8;
            labelText.alignment = TextAnchor.MiddleRight;
            labelText.color = Color.white;
            RectTransform labelRect = labelText.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            labelRect.anchoredPosition = anchoredPos + new Vector2(-size / 2f - 10f, 0f);
            labelRect.sizeDelta = new Vector2(140f, size);
        }

        GameObject slotGO = new GameObject("Equip_" + slotType + "_" + ringIndex, typeof(Image));
        slotGO.transform.SetParent(panel.transform, false);
        Image img = slotGO.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(size, size);

        InventorySlotUI slotUI = slotGO.AddComponent<InventorySlotUI>();
        slotUI.inventory = inventory;
        slotUI.equipment = equipment;
        slotUI.kind = InventorySlotKind.Equipment;
        slotUI.equipmentSlotType = slotType;
        slotUI.ringIndex = ringIndex;

        return img;
    }

    void Refresh()
    {
        if (inventory != null)
        {
            for (int i = 0; i < slotImages.Length; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                ItemDefinition definition = !slot.IsEmpty ? ItemDatabase.Get(slot.itemId) : null;
                slotImages[i].sprite = definition != null ? definition.Icon : null;
                slotImages[i].color = definition != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
                slotTexts[i].text = definition != null ? slot.count.ToString() : "";
            }
        }

        if (equipment == null) return;
        foreach (KeyValuePair<EquipmentSlotType, Image> kv in equipmentImages)
        {
            RefreshEquipmentImage(kv.Value, equipment.Get(kv.Key));
        }
        foreach (KeyValuePair<(EquipmentSlotType, int), Image> kv in ringImages)
        {
            RefreshEquipmentImage(kv.Value, equipment.Get(kv.Key.Item1, kv.Key.Item2));
        }
    }

    static void RefreshEquipmentImage(Image img, string itemId)
    {
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        img.sprite = definition != null ? definition.Icon : null;
        img.color = definition != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
    }
}
