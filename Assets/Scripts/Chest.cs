using UnityEngine;
using UnityEngine.UI;

// A stationary container holding exactly one item, inspected before it's taken - explicit request:
// "un item dans un coffre... pourra etre inspecte avant d'etre recupere. Si jamais la description
// indique : epee maudite du soldat dechu, le joueur pourra simplement eviter de le recuperer".
// Same self-built-UI, proximity-prompt pattern as Corpse/TutorialNpc/RestBed, but unlike Corpse
// (which auto-grants everything on open) this one is a real choice: [E] Prendre or leave it
// closed and walk away, reopenable any time until actually taken.
[RequireComponent(typeof(CircleCollider2D))]
public class Chest : MonoBehaviour, UIWindowStack.IWindow
{
    const string ExaminePrompt = "Appuyez sur E pour ouvrir le coffre";

    public string itemId;
    public int amount = 1;
    public PlayerInventory playerInventory;

    GameObject promptGO;
    GameObject panel;
    Text nameText;
    Text bodyText;
    bool playerNearby;
    bool isOpen;
    bool taken;

    void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Start()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);
        promptGO = InteractPromptUI.Build(canvasGO.transform, "ChestPrompt", ExaminePrompt).gameObject;

        panel = new GameObject("ChestPanel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.95f);
        RectTransform panelRect = bg.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(600f, 300f);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text titleLabel = titleGO.GetComponent<Text>();
        titleLabel.font = font;
        titleLabel.fontSize = 28;
        titleLabel.fontStyle = FontStyle.Bold;
        titleLabel.color = Color.white;
        titleLabel.text = "Coffre";
        RectTransform titleRect = titleLabel.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(560f, 40f);

        GameObject nameGO = new GameObject("ItemName", typeof(Text));
        nameGO.transform.SetParent(panel.transform, false);
        nameText = nameGO.GetComponent<Text>();
        nameText.font = font;
        nameText.fontSize = 26;
        nameText.fontStyle = FontStyle.Bold;
        nameText.color = Color.white;
        nameText.supportRichText = true;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0f, -70f);
        nameRect.sizeDelta = new Vector2(560f, 40f);

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(panel.transform, false);
        bodyText = bodyGO.GetComponent<Text>();
        bodyText.font = font;
        bodyText.fontSize = 22;
        bodyText.color = Color.white;
        bodyText.alignment = TextAnchor.UpperLeft;
        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -120f);
        bodyRect.sizeDelta = new Vector2(560f, 170f);

        panel.SetActive(false);
    }

    void Update()
    {
        if (promptGO == null) return;

        if (isOpen)
        {
            if (!taken && KeyBindings.WasPressedThisFrame(GameAction.Interact)) TakeItem();
            // Escape closes this (see TryCloseFromStack/UIWindowStack), handled centrally now.
            return;
        }

        promptGO.SetActive(playerNearby);
        if (playerNearby && KeyBindings.WasPressedThisFrame(GameAction.Interact)) Open();
    }

    void Open()
    {
        isOpen = true;
        UIWindowStack.Push(this);
        promptGO.SetActive(false);
        panel.SetActive(true);
        Refresh();
    }

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        Close();
        return true;
    }

    void Refresh()
    {
        if (taken || string.IsNullOrEmpty(itemId))
        {
            nameText.text = "";
            bodyText.text = "Le coffre est vide.\n\n[ECHAP] Fermer";
            return;
        }

        ItemDefinition definition = ItemDatabase.Get(itemId);
        string displayName = definition != null ? definition.DisplayName : itemId;
        int rarity = definition != null ? definition.Rarity : ItemRarity.Min;
        nameText.text = ItemRarity.ColoredName(displayName + (amount > 1 ? " x" + amount : ""), rarity);

        string body = definition != null ? definition.Description : "";
        body = ItemRarity.DescriptionWithRarity(body, rarity);
        body += "\n\n[E] Prendre   [ECHAP] Laisser";
        bodyText.text = body;
    }

    void TakeItem()
    {
        if (playerInventory != null && !string.IsNullOrEmpty(itemId)) playerInventory.Add(itemId, amount);
        taken = true;
        Refresh();
    }

    void Close()
    {
        isOpen = false;
        panel.SetActive(false);
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

    // Runtime-safe factory (unlike DungeonBootstrap's editor-only spawning) - used wherever a
    // floor's population logic places a chest (currently just Treasure rooms - see
    // DungeonGenerator.PopulateRoom/ChestLootPool).
    public static GameObject SpawnAt(Vector2 position, string itemId, int amount, Sprite sprite, PlayerInventory playerInventory)
    {
        GameObject go = new GameObject("Chest", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Chest));
        go.transform.position = position;
        go.transform.localScale = new Vector3(1.4f, 1f, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;

        CircleCollider2D col = go.GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 1f;

        Chest chest = go.GetComponent<Chest>();
        chest.itemId = itemId;
        chest.amount = amount;
        chest.playerInventory = playerInventory;
        return go;
    }
}
