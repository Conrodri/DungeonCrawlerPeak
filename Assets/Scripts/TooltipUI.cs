using UnityEngine;
using UnityEngine.UI;

// A single shared hover tooltip, shown near the cursor - any UI element calls
// TooltipUI.Instance.Show/Hide instead of building its own popup. Builds its own minimal UI on
// Start() (finds the scene's "Canvas" by name), same self-contained convention as TutorialNpc.
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    GameObject panel;
    Text nameLabel;
    Text bodyLabel;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        BuildUI();
    }

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 22);

        panel = new GameObject("Tooltip", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.95f);
        bg.raycastTarget = false;
        RectTransform panelRect = bg.rectTransform;
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(320f, 90f);

        GameObject nameGO = new GameObject("Name", typeof(Text));
        nameGO.transform.SetParent(panel.transform, false);
        nameLabel = nameGO.GetComponent<Text>();
        nameLabel.font = font;
        nameLabel.fontSize = 22;
        nameLabel.fontStyle = FontStyle.Bold;
        nameLabel.color = Color.white;
        nameLabel.raycastTarget = false;
        RectTransform nameRect = nameLabel.rectTransform;
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.anchoredPosition = new Vector2(10f, -8f);
        nameRect.sizeDelta = new Vector2(300f, 28f);

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(panel.transform, false);
        bodyLabel = bodyGO.GetComponent<Text>();
        bodyLabel.font = font;
        bodyLabel.fontSize = 18;
        bodyLabel.color = new Color(0.85f, 0.85f, 0.9f);
        bodyLabel.raycastTarget = false;
        RectTransform bodyRect = bodyLabel.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0f, 1f);
        bodyRect.pivot = new Vector2(0f, 1f);
        bodyRect.anchoredPosition = new Vector2(10f, -38f);
        bodyRect.sizeDelta = new Vector2(300f, 50f);

        panel.SetActive(false);
    }

    public void Show(string title, string body, Vector2 screenPosition)
    {
        if (panel == null) return;
        panel.SetActive(true);
        nameLabel.text = title;
        bodyLabel.text = body;
        // Offset up-right of the cursor so the tooltip never sits directly under it.
        panel.GetComponent<RectTransform>().position = screenPosition + new Vector2(16f, 16f);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
