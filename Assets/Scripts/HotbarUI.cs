using UnityEngine;
using UnityEngine.UI;

public class HotbarUI : MonoBehaviour
{
    public const int SlotCount = 5;

    public PlayerInventory inventory;
    // Slot 5 is reserved for a future item.
    public string[] slotItemIds = { ItemIds.Shuriken, ItemIds.Caillou, ItemIds.Baton, ItemIds.Bomb, null };
    public float slotSize = 40f;
    public float spacing = 48f;
    public int fontSize = 14;

    Text[] slotTexts;

    void Start()
    {
        BuildSlots();
        if (inventory != null)
        {
            inventory.OnInventoryChanged += Refresh;
            Refresh();
        }
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnInventoryChanged -= Refresh;
    }

    void BuildSlots()
    {
        slotTexts = new Text[SlotCount];
        Font font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);

        for (int i = 0; i < SlotCount; i++)
        {
            string itemId = slotItemIds[i];
            ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;

            GameObject slotGO = new GameObject("Slot" + (i + 1), typeof(Image));
            slotGO.transform.SetParent(transform, false);

            Image img = slotGO.GetComponent<Image>();
            img.sprite = definition != null ? definition.Icon : null;
            img.color = definition != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);

            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Centered as a row at the bottom-middle of the screen.
            float xOffset = (i - (SlotCount - 1) / 2f) * spacing;
            rt.anchoredPosition = new Vector2(xOffset, 20f + slotSize / 2f);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

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
        for (int i = 0; i < SlotCount; i++)
        {
            string itemId = slotItemIds[i];
            slotTexts[i].text = !string.IsNullOrEmpty(itemId) && inventory != null
                ? inventory.GetCount(itemId).ToString()
                : "";
        }
    }
}
