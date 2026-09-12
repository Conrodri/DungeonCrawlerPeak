using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    static InventoryUI instance;
    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    public PlayerInventory inventory;
    public int columns = 5;
    public float slotSize = 56f;
    public float spacing = 64f;
    public int fontSize = 24;

    GameObject panel;
    Image[] slotImages;
    Text[] slotTexts;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        BuildPanel();
        if (inventory != null)
        {
            inventory.OnInventoryChanged += Refresh;
            Refresh();
        }
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnInventoryChanged -= Refresh;
        if (instance == this) instance = null;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.iKey.wasPressedThisFrame) panel.SetActive(!panel.activeSelf);
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
            rt.anchoredPosition = new Vector2(col * spacing - totalWidth / 2f, totalHeight / 2f - row * spacing);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            InventorySlotUI slotUI = slotGO.AddComponent<InventorySlotUI>();
            slotUI.inventory = inventory;
            slotUI.isHotbarSlot = false;
            slotUI.index = i;

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

    void Refresh()
    {
        if (inventory == null) return;
        for (int i = 0; i < slotImages.Length; i++)
        {
            InventorySlot slot = inventory.GetSlot(i);
            ItemDefinition definition = !slot.IsEmpty ? ItemDatabase.Get(slot.itemId) : null;
            slotImages[i].sprite = definition != null ? definition.Icon : null;
            slotImages[i].color = definition != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
            slotTexts[i].text = definition != null ? slot.count.ToString() : "";
        }
    }
}
