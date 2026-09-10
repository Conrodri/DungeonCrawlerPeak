using UnityEngine;
using UnityEngine.UI;

public class HeartHUD : MonoBehaviour
{
    public Health target;
    public Sprite fullHeart;
    public Sprite halfHeart;
    public Sprite emptyHeart;
    public int maxHeartSlots = 3;
    public float spacing = 40f;
    public float heartSize = 32f;

    Image[] hearts;

    void Start()
    {
        BuildSlots();
        if (target != null)
        {
            target.OnHealthChanged += Refresh;
            Refresh(target.currentHealth, target.maxHealth);
        }
    }

    void OnDestroy()
    {
        if (target != null) target.OnHealthChanged -= Refresh;
    }

    void BuildSlots()
    {
        hearts = new Image[maxHeartSlots];
        for (int i = 0; i < maxHeartSlots; i++)
        {
            GameObject go = new GameObject("Heart" + i, typeof(Image));
            go.transform.SetParent(transform, false);

            Image img = go.GetComponent<Image>();
            img.sprite = emptyHeart;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f + i * spacing, -20f);
            rt.sizeDelta = new Vector2(heartSize, heartSize);

            hearts[i] = img;
        }
    }

    // Health is tracked in half-heart units: 2 units per full heart slot.
    void Refresh(int current, int max)
    {
        for (int i = 0; i < hearts.Length; i++)
        {
            int slotValue = current - i * 2;
            if (slotValue >= 2) hearts[i].sprite = fullHeart;
            else if (slotValue == 1) hearts[i].sprite = halfHeart;
            else hearts[i].sprite = emptyHeart;
        }
    }
}
