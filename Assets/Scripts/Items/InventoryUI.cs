using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour, UIWindowStack.IWindow
{
    static InventoryUI instance;
    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    // Lets InventorySlotUI close the panel right after a click-to-use throw/potion, so the player
    // actually sees the projectile fly instead of it firing behind a still-open menu.
    public static void CloseIfOpen()
    {
        if (instance != null) instance.ClosePanel();
    }

    public PlayerInventory inventory;
    public PlayerEquipment equipment;
    // Lets a plain click on a grid slot use/throw the item directly - see InventorySlotUI.
    public PlayerController player;
    // Drives the body silhouette (see BuildLimbSilhouette) - null-safe throughout, same as
    // inventory/equipment above, so a scene without one just skips that panel.
    public PlayerLimbs limbs;
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
    readonly Dictionary<BodyPart, Image> limbImages = new Dictionary<BodyPart, Image>();
    Text weaponHandText;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        BuildPanel();
        BuildEquipmentPanel();
        BuildLimbSilhouette();
        if (inventory != null) inventory.OnInventoryChanged += Refresh;
        if (equipment != null) equipment.OnEquipmentChanged += Refresh;
        if (limbs != null) limbs.OnLimbsChanged += Refresh;
        Refresh();
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnInventoryChanged -= Refresh;
        if (equipment != null) equipment.OnEquipmentChanged -= Refresh;
        if (limbs != null) limbs.OnLimbsChanged -= Refresh;
        if (instance == this) instance = null;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || (!kb.iKey.wasPressedThisFrame && !kb.tabKey.wasPressedThisFrame)) return;

        if (panel.activeSelf) ClosePanel();
        else OpenPanel();
    }

    void OpenPanel()
    {
        panel.SetActive(true);
        UIWindowStack.Push(this);
        // Weapon hand (H, see PlayerController.SwitchWeaponHand) has no change event to refresh
        // from like inventory/equipment/limbs do - catch it fresh on every open instead.
        Refresh();
    }

    void ClosePanel()
    {
        panel.SetActive(false);
        UIWindowStack.Remove(this);
        // Without this, hovering an item and then closing the panel (I/Tab, Escape, or a
        // click-to-use) leaves the description stuck on screen - OnPointerExit never fires because
        // the slot's GameObject just got deactivated instead of the cursor actually leaving it.
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }

    public bool TryCloseFromStack()
    {
        if (panel == null || !panel.activeSelf) return false;
        ClosePanel();
        return true;
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

    // A paperdoll silhouette to the right of the inventory grid, instead of a flat top-to-bottom
    // list (explicit request - "comme dans un rpg classique... type diablo 3/4 ou path of exile"):
    // helmet on top, pauldrons/necklace flanking the head, weapon/belt at hip height on the
    // center spine, gloves/knees below that, boots at the very bottom - each slot sits roughly
    // where the gear would actually be worn. Ring columns flank the whole silhouette at hand
    // height. See EquipmentSlotType/PlayerEquipment.
    void BuildEquipmentPanel()
    {
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize - 6);
        float equipSlotSize = slotSize * 0.85f;
        float cx = 280f;
        float cy = 210f;
        float colSpacing = 100f;
        float rowSpacing = 84f;

        (EquipmentSlotType type, string label, Vector2 pos)[] silhouetteSlots =
        {
            (EquipmentSlotType.Head, "Casque", new Vector2(cx, cy + rowSpacing * 2f)),
            (EquipmentSlotType.Shoulders, "Epaulieres", new Vector2(cx - colSpacing, cy + rowSpacing)),
            (EquipmentSlotType.Neck, "Cou", new Vector2(cx + colSpacing, cy + rowSpacing)),
            (EquipmentSlotType.Weapon, "Arme", new Vector2(cx - colSpacing, cy)),
            (EquipmentSlotType.Belt, "Ceinture", new Vector2(cx, cy)),
            (EquipmentSlotType.Gloves, "Gants", new Vector2(cx - colSpacing, cy - rowSpacing)),
            (EquipmentSlotType.Knees, "Genouilleres", new Vector2(cx + colSpacing, cy - rowSpacing)),
            (EquipmentSlotType.Boots, "Bottes", new Vector2(cx, cy - rowSpacing * 2f)),
        };

        foreach (var slot in silhouetteSlots)
        {
            Image img = BuildEquipmentSlot(slot.label, font, slot.pos, equipSlotSize, slot.type, 0);
            equipmentImages[slot.type] = img;
        }

        float ringsTop = cy + 50f;
        BuildRingColumn("Anneaux\n(main gauche)", font, new Vector2(cx - colSpacing * 2f, ringsTop), equipSlotSize, EquipmentSlotType.RingLeft);
        BuildRingColumn("Anneaux\n(main droite)", font, new Vector2(cx + colSpacing * 2f, ringsTop), equipSlotSize, EquipmentSlotType.RingRight);
    }

    // A small colored body diagram (green/orange/red per BodyPart, see LimbState/Refresh) further
    // right of the equipment panel - hover a part for its exact HP (LimbSlotUI). Purely visual
    // rectangles, not a real body sprite - matches this project's placeholder-first convention
    // (procedural sprites/colors everywhere else, see DungeonGenerator's CreateSolidSprite).
    void BuildLimbSilhouette()
    {
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize - 6);
        // Pushed further right than before (was 560) - the equipment panel's silhouette layout
        // (see BuildEquipmentPanel) is wider than the old flat list it replaced, its ring columns
        // now reach out to about x=550.
        float cx = 700f;
        float cy = 180f;

        GameObject titleGO = new GameObject("LimbsTitle", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text title = titleGO.GetComponent<Text>();
        title.text = "Membres";
        title.font = font;
        title.fontSize = fontSize - 4;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(cx, cy + 130f);
        titleRect.sizeDelta = new Vector2(160f, 30f);

        BuildLimbPart(font, BodyPart.Head, "Tete", new Vector2(cx, cy + 92f), new Vector2(40f, 40f));
        BuildLimbPart(font, BodyPart.Torso, "Torse", new Vector2(cx, cy + 28f), new Vector2(58f, 74f));
        BuildLimbPart(font, BodyPart.ArmLeft, "Bras gauche", new Vector2(cx - 44f, cy + 30f), new Vector2(26f, 66f));
        BuildLimbPart(font, BodyPart.ArmRight, "Bras droit", new Vector2(cx + 44f, cy + 30f), new Vector2(26f, 66f));
        BuildLimbPart(font, BodyPart.LegLeft, "Jambe gauche", new Vector2(cx - 17f, cy - 56f), new Vector2(26f, 68f));
        BuildLimbPart(font, BodyPart.LegRight, "Jambe droite", new Vector2(cx + 17f, cy - 56f), new Vector2(26f, 68f));

        GameObject handGO = new GameObject("WeaponHandText", typeof(Text));
        handGO.transform.SetParent(panel.transform, false);
        weaponHandText = handGO.GetComponent<Text>();
        weaponHandText.font = font;
        weaponHandText.fontSize = fontSize - 8;
        weaponHandText.alignment = TextAnchor.MiddleCenter;
        weaponHandText.color = new Color(0.85f, 0.85f, 0.9f);
        RectTransform handRect = weaponHandText.rectTransform;
        handRect.anchorMin = handRect.anchorMax = new Vector2(0.5f, 0.5f);
        handRect.pivot = new Vector2(0.5f, 0.5f);
        handRect.anchoredPosition = new Vector2(cx, cy - 110f);
        handRect.sizeDelta = new Vector2(220f, 40f);
    }

    void BuildLimbPart(Font font, BodyPart part, string label, Vector2 anchoredPos, Vector2 size)
    {
        GameObject slotGO = new GameObject("Limb_" + part, typeof(Image));
        slotGO.transform.SetParent(panel.transform, false);
        Image img = slotGO.GetComponent<Image>();
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        LimbSlotUI slotUI = slotGO.AddComponent<LimbSlotUI>();
        slotUI.limbs = limbs;
        slotUI.part = part;
        slotUI.label = label;

        limbImages[part] = img;
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
            // Centered directly under the icon rather than off to one side - a fixed side (the old
            // layout always put it to the left) reads fine in a flat top-to-bottom list, but the
            // silhouette has slots on both the left and right column, where a left-anchored label
            // would either collide with a neighboring slot or float away from its own icon.
            GameObject labelGO = new GameObject(label + "Label", typeof(Text));
            labelGO.transform.SetParent(panel.transform, false);
            Text labelText = labelGO.GetComponent<Text>();
            labelText.text = label;
            labelText.font = font;
            labelText.fontSize = fontSize - 12;
            labelText.alignment = TextAnchor.UpperCenter;
            labelText.color = Color.white;
            RectTransform labelRect = labelText.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = anchoredPos + new Vector2(0f, -size / 2f - 4f);
            labelRect.sizeDelta = new Vector2(size + 50f, 22f);
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

        if (equipment != null)
        {
            foreach (KeyValuePair<EquipmentSlotType, Image> kv in equipmentImages)
            {
                RefreshEquipmentImage(kv.Value, equipment.Get(kv.Key));
            }
            foreach (KeyValuePair<(EquipmentSlotType, int), Image> kv in ringImages)
            {
                RefreshEquipmentImage(kv.Value, equipment.Get(kv.Key.Item1, kv.Key.Item2));
            }
        }

        if (limbs != null)
        {
            foreach (KeyValuePair<BodyPart, Image> kv in limbImages)
            {
                kv.Value.color = ColorForLimbState(limbs.GetState(kv.Key));
            }
        }

        if (weaponHandText != null && player != null)
        {
            string handLabel = player.weaponHand == BodyPart.ArmRight ? "droite" : "gauche";
            weaponHandText.text = "Main d'arme : " + handLabel + " (H pour changer)";
        }
    }

    static Color ColorForLimbState(LimbState state) => state switch
    {
        LimbState.Healthy => new Color(0.25f, 0.75f, 0.3f),
        LimbState.Damaged => new Color(0.9f, 0.6f, 0.1f),
        _ => new Color(0.85f, 0.15f, 0.15f), // Broken
    };

    static void RefreshEquipmentImage(Image img, string itemId)
    {
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        img.sprite = definition != null ? definition.Icon : null;
        img.color = definition != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
    }
}
