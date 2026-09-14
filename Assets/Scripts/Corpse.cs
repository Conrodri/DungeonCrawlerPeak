using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A dead mob/boss's remains - explicit request (2026-09-14): examining a corpse (E) opens its
// loot instead of items silently auto-dropping on death (see CorpseLoot.cs for what ends up
// inside, and EnemyController/BossRoomController for where this gets spawned). Same self-built-UI,
// proximity-prompt pattern as TutorialNpc/RestBed - a single action with no branching needs no
// shared dialogue panel.
[RequireComponent(typeof(CircleCollider2D))]
public class Corpse : MonoBehaviour
{
    const string ExaminePrompt = "Appuyez sur E pour examiner le corps";

    public string title = "Cadavre";
    public List<(string itemId, int amount)> items = new List<(string, int)>();
    public PlayerInventory playerInventory;

    GameObject promptGO;
    GameObject panel;
    Text panelText;
    bool playerNearby;
    bool isOpen;
    bool looted;
    string lootMessage;

    void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Start()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);

        promptGO = new GameObject("CorpsePrompt", typeof(Text));
        promptGO.transform.SetParent(canvasGO.transform, false);
        Text prompt = promptGO.GetComponent<Text>();
        prompt.font = font;
        prompt.fontSize = 28;
        prompt.alignment = TextAnchor.MiddleCenter;
        prompt.color = Color.white;
        prompt.text = ExaminePrompt;
        RectTransform promptRect = prompt.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 120f);
        promptRect.sizeDelta = new Vector2(500f, 40f);
        promptGO.SetActive(false);

        panel = new GameObject("CorpsePanel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.95f);
        RectTransform panelRect = bg.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(600f, 340f);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text titleLabel = titleGO.GetComponent<Text>();
        titleLabel.font = font;
        titleLabel.fontSize = 28;
        titleLabel.fontStyle = FontStyle.Bold;
        titleLabel.color = Color.white;
        titleLabel.text = title;
        RectTransform titleRect = titleLabel.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(560f, 40f);

        GameObject bodyGO = new GameObject("Body", typeof(Text));
        bodyGO.transform.SetParent(panel.transform, false);
        panelText = bodyGO.GetComponent<Text>();
        panelText.font = font;
        panelText.fontSize = 22;
        panelText.color = Color.white;
        panelText.alignment = TextAnchor.UpperLeft;
        RectTransform bodyRect = panelText.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -70f);
        bodyRect.sizeDelta = new Vector2(560f, 220f);

        panel.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current == null || promptGO == null) return;

        if (isOpen)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame) Close();
            return;
        }

        promptGO.SetActive(playerNearby);
        if (playerNearby && Keyboard.current.eKey.wasPressedThisFrame) Open();
    }

    void Open()
    {
        isOpen = true;
        promptGO.SetActive(false);
        panel.SetActive(true);

        // Only actually hands out items the first time - reopening (nothing stops the player from
        // pressing E again) just re-shows the same summary instead of doubling the loot.
        if (!looted)
        {
            looted = true;
            lootMessage = BuildLootMessageAndGrant();
        }
        panelText.text = lootMessage + "\n\n[E] Fermer";
    }

    string BuildLootMessageAndGrant()
    {
        if (items.Count == 0) return "Il n'y a rien a recuperer.";

        var sb = new System.Text.StringBuilder();
        foreach ((string itemId, int amount) in items)
        {
            ItemDefinition definition = ItemDatabase.Get(itemId);
            sb.AppendLine((definition != null ? definition.DisplayName : itemId) + " x" + amount);
            if (playerInventory != null) playerInventory.Add(itemId, amount);
        }
        sb.Append("\nRecupere.");
        return sb.ToString();
    }

    void Close()
    {
        isOpen = false;
        panel.SetActive(false);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = false;
    }

    // Runtime-safe factory (unlike DungeonBootstrap's editor-only spawning) - used by
    // EnemyController/BossRoomController on death, which happens during actual play.
    public static GameObject SpawnAt(Vector2 position, string title, List<(string itemId, int amount)> items, Sprite baseSprite, PlayerInventory playerInventory)
    {
        GameObject go = new GameObject(title + " (corps)", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Corpse));
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, 0f, 90f); // fallen over
        go.transform.localScale = Vector3.one * 0.85f;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = baseSprite;
        renderer.color = new Color(0.4f, 0.4f, 0.4f, 0.9f); // desaturated - reads as dead, not just a duller version of the same creature
        renderer.sortingOrder = -1; // under anything still alive/standing

        CircleCollider2D col = go.GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 1f;

        Corpse corpse = go.GetComponent<Corpse>();
        corpse.title = title;
        corpse.items = items;
        corpse.playerInventory = playerInventory;
        return go;
    }
}
