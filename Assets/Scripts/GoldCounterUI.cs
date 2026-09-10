using UnityEngine;
using UnityEngine.UI;

public class GoldCounterUI : MonoBehaviour
{
    public PlayerInventory inventory;
    public Sprite goldSprite;
    public float yOffset = -64f;

    Text text;

    void Start()
    {
        BuildUI();
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

    void BuildUI()
    {
        GameObject iconGO = new GameObject("GoldIcon", typeof(Image));
        iconGO.transform.SetParent(transform, false);
        Image icon = iconGO.GetComponent<Image>();
        icon.sprite = goldSprite;
        RectTransform iconRt = icon.rectTransform;
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
        iconRt.pivot = new Vector2(0f, 1f);
        iconRt.anchoredPosition = new Vector2(20f, yOffset);
        iconRt.sizeDelta = new Vector2(24f, 24f);

        GameObject textGO = new GameObject("GoldText", typeof(Text));
        textGO.transform.SetParent(transform, false);
        text = textGO.GetComponent<Text>();
        text.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = textRt.anchorMax = new Vector2(0f, 1f);
        textRt.pivot = new Vector2(0f, 1f);
        textRt.anchoredPosition = new Vector2(50f, yOffset);
        textRt.sizeDelta = new Vector2(80f, 24f);
    }

    void Refresh()
    {
        text.text = inventory != null ? inventory.gold.ToString() : "0";
    }
}
