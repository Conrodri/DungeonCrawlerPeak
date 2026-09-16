using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A read-only explanation popup - distinct from NpcInteractable/DialogueManager (built for
// branching choices + D20 checks), this only ever shows one static paragraph and closes. Builds
// its own minimal UI on Start() (finds the scene's "Canvas" by name) so a caller only needs to
// set npcName/bodyText, without wiring a shared dialogue panel just for one tutorial line.
[RequireComponent(typeof(CircleCollider2D))]
public class TutorialNpc : MonoBehaviour, UIWindowStack.IWindow
{
    public string npcName = "Le Guide";
    [TextArea] public string bodyText;

    GameObject promptGO;
    GameObject panel;
    Text bodyLabel;
    bool playerNearby;
    bool isOpen;

    void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Start()
    {
        BuildUI();
    }

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);
        promptGO = InteractPromptUI.Build(canvasGO.transform, "TutorialPrompt", "Appuyez sur E pour parler").gameObject;

        panel = new GameObject("TutorialPanel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.95f);
        RectTransform panelRect = bg.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(760f, 380f);

        GameObject nameGO = new GameObject("Name", typeof(Text));
        nameGO.transform.SetParent(panel.transform, false);
        Text nameLabel = nameGO.GetComponent<Text>();
        nameLabel.font = font;
        nameLabel.fontSize = 30;
        nameLabel.fontStyle = FontStyle.Bold;
        nameLabel.color = Color.white;
        nameLabel.text = npcName;
        RectTransform nameRect = nameLabel.rectTransform;
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0f, -20f);
        nameRect.sizeDelta = new Vector2(700f, 40f);

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(panel.transform, false);
        bodyLabel = bodyGO.GetComponent<Text>();
        bodyLabel.font = font;
        bodyLabel.fontSize = 24;
        bodyLabel.color = Color.white;
        bodyLabel.alignment = TextAnchor.UpperLeft;
        RectTransform bodyRect = bodyLabel.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -70f);
        bodyRect.sizeDelta = new Vector2(700f, 280f);

        panel.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (isOpen)
        {
            // Escape also closes this (see TryCloseFromStack/UIWindowStack).
            if (Keyboard.current.eKey.wasPressedThisFrame) Close();
            return;
        }

        if (promptGO != null) promptGO.SetActive(playerNearby);
        if (playerNearby && Keyboard.current.eKey.wasPressedThisFrame) Open();
    }

    void Open()
    {
        isOpen = true;
        UIWindowStack.Push(this);
        if (promptGO != null) promptGO.SetActive(false);
        if (panel != null) panel.SetActive(true);
        if (bodyLabel != null) bodyLabel.text = bodyText + "\n\n[E] Fermer";
    }

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        Close();
        return true;
    }

    void Close()
    {
        isOpen = false;
        if (panel != null) panel.SetActive(false);
        UIWindowStack.Remove(this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = false;
    }
}
