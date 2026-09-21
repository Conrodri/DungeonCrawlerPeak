using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Debug/training-only tools (2026-09-21 request) - two toggle panels in the Salle d'entrainement:
// pick any monster or any item from a list and spawn/receive it instantly, to test combat and
// loadouts without waiting on a real floor's RNG or room recipes. Both wired up only from
// DungeonGenerator.BuildTrainingRoom - neither ever appears on a real floor/tutorial.
public class MobSpawnerUI : MonoBehaviour, UIWindowStack.IWindow
{
    public Transform player;
    public RoomController.EnemyPresetEntry[] presets;
    public Transform spawnParent;

    GameObject panel;
    bool isOpen;

    void Start() => BuildUI();

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 16);

        GameObject toggleGO = TrainingSpawnerLayout.BuildToggleButton(canvasGO.transform, font, "Spawner Mob",
            new Vector2(-20f, -20f), new Color(0.18f, 0.32f, 0.18f, 0.92f));
        toggleGO.AddComponent<SimpleClickButton>().onClick = Toggle;

        EnemyType[] types = (EnemyType[])System.Enum.GetValues(typeof(EnemyType));

        panel = TrainingSpawnerLayout.BuildPanel(canvasGO.transform, new Vector2(-20f, -62f),
            new Vector2(180f, 20f + types.Length * 32f), new Color(0.07f, 0.1f, 0.07f, 0.94f));

        for (int i = 0; i < types.Length; i++)
        {
            EnemyType type = types[i]; // captured per-iteration for the closure below
            GameObject btnGO = TrainingSpawnerLayout.BuildRowButton(panel.transform, font, type.ToString(), i,
                new Vector2(160f, 28f), new Color(0.22f, 0.38f, 0.22f, 0.95f));
            btnGO.AddComponent<SimpleClickButton>().onClick = () => SpawnMob(type);
        }

        panel.SetActive(false);
    }

    void SpawnMob(EnemyType type)
    {
        if (player == null || presets == null) return;
        Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
        Vector2 pos = (Vector2)player.position + offset;
        DungeonGenerator.SpawnTrainingMob(type, pos, player, presets, spawnParent);
    }

    void Toggle()
    {
        isOpen = !isOpen;
        panel.SetActive(isOpen);
        if (isOpen) UIWindowStack.Push(this);
        else UIWindowStack.Remove(this);
    }

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        isOpen = false;
        panel.SetActive(false);
        return true;
    }
}

// Same shape as MobSpawnerUI above, reading every registered item (ItemDatabase.All, already
// populated by ItemCatalog.Awake by the time BuildTrainingRoom wires this up) instead of a fixed
// EnemyType list - a grid of icons (InventoryUI's own manual-grid-math convention, see its
// columns/slotSize) rather than a single column, since there are far more items than mob types.
public class ItemSpawnerUI : MonoBehaviour, UIWindowStack.IWindow
{
    public PlayerInventory inventory;
    public int columns = 8;
    public float slotSize = 40f;
    public float spacing = 4f;

    GameObject panel;
    bool isOpen;

    void Start() => BuildUI();

    void BuildUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 16);

        GameObject toggleGO = TrainingSpawnerLayout.BuildToggleButton(canvasGO.transform, font, "Spawner Item",
            new Vector2(-190f, -20f), new Color(0.32f, 0.28f, 0.12f, 0.92f));
        toggleGO.AddComponent<SimpleClickButton>().onClick = Toggle;

        var entries = new System.Collections.Generic.List<ItemDefinition>(ItemDatabase.All);
        int rows = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)columns));
        float panelWidth = columns * slotSize + (columns - 1) * spacing + 20f;
        float panelHeight = rows * slotSize + (rows - 1) * spacing + 20f;

        panel = TrainingSpawnerLayout.BuildPanel(canvasGO.transform, new Vector2(-190f, -62f),
            new Vector2(panelWidth, panelHeight), new Color(0.1f, 0.09f, 0.06f, 0.94f));

        for (int i = 0; i < entries.Count; i++)
        {
            ItemDefinition entry = entries[i];
            int col = i % columns;
            int row = i / columns;

            GameObject slotGO = new GameObject(entry.Id, typeof(Image));
            slotGO.transform.SetParent(panel.transform, false);
            Image slotBg = slotGO.GetComponent<Image>();
            slotBg.color = new Color(0.3f, 0.28f, 0.2f, 0.9f);
            RectTransform slotRect = slotBg.rectTransform;
            slotRect.anchorMin = slotRect.anchorMax = new Vector2(0f, 1f);
            slotRect.pivot = new Vector2(0f, 1f);
            slotRect.anchoredPosition = new Vector2(10f + col * (slotSize + spacing), -10f - row * (slotSize + spacing));
            slotRect.sizeDelta = new Vector2(slotSize, slotSize);

            GameObject iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(slotGO.transform, false);
            Image icon = iconGO.GetComponent<Image>();
            icon.sprite = entry.Icon;
            icon.raycastTarget = false;
            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.1f, 0.1f);
            iconRect.anchorMax = new Vector2(0.9f, 0.9f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            int grantAmount = Mathf.Max(1, Mathf.Min(10, entry.MaxStack));
            SimpleClickButton click = slotGO.AddComponent<SimpleClickButton>();
            click.onClick = () => inventory.Add(entry.Id, grantAmount);

            TrainingSpawnerLayout.WireTooltip(slotGO, entry.DisplayName, "Cliquez pour recevoir x" + grantAmount);
        }

        panel.SetActive(false);
    }

    void Toggle()
    {
        isOpen = !isOpen;
        panel.SetActive(isOpen);
        if (isOpen) UIWindowStack.Push(this);
        else UIWindowStack.Remove(this);
    }

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        isOpen = false;
        panel.SetActive(false);
        return true;
    }
}

// Small shared layout helpers so MobSpawnerUI/ItemSpawnerUI don't each re-derive the same
// toggle-button/panel/tooltip boilerplate - purely code-driven UI construction, same convention as
// every other panel in this project (SpellBookUI, InventoryUI, ...).
static class TrainingSpawnerLayout
{
    public static GameObject BuildToggleButton(Transform canvasParent, Font font, string label, Vector2 anchoredPosition, Color color)
    {
        GameObject go = new GameObject(label.Replace(" ", "") + "Toggle", typeof(Image));
        go.transform.SetParent(canvasParent, false);
        Image bg = go.GetComponent<Image>();
        bg.color = color;
        RectTransform rect = bg.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(160f, 36f);

        GameObject labelGO = new GameObject("Label", typeof(Text));
        labelGO.transform.SetParent(go.transform, false);
        Text text = labelGO.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return go;
    }

    public static GameObject BuildPanel(Transform canvasParent, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasParent, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = color;
        RectTransform rect = bg.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return panel;
    }

    public static GameObject BuildRowButton(Transform panelParent, Font font, string label, int rowIndex, Vector2 size, Color color)
    {
        GameObject go = new GameObject(label, typeof(Image));
        go.transform.SetParent(panelParent, false);
        Image bg = go.GetComponent<Image>();
        bg.color = color;
        RectTransform rect = bg.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, -10f - rowIndex * (size.y + 4f));
        rect.sizeDelta = size;

        GameObject labelGO = new GameObject("Label", typeof(Text));
        labelGO.transform.SetParent(go.transform, false);
        Text text = labelGO.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return go;
    }

    public static void WireTooltip(GameObject go, string title, string body)
    {
        TrainingTooltipTrigger trigger = go.AddComponent<TrainingTooltipTrigger>();
        trigger.title = title;
        trigger.body = body;
    }
}

// Reuses the shared TooltipUI (see InventorySlotUI's own hover handling for the established
// pattern) rather than each slot building its own label.
class TrainingTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string title;
    public string body;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Show(title, body, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }
}
