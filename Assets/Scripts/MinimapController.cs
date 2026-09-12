using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MinimapController : MonoBehaviour
{
    public RoomCameraController roomCamera;
    public RoomController[] monsterRooms = new RoomController[0];
    public List<Vector2Int> allRoomGridPositions = new List<Vector2Int>();
    // Never pre-revealed as "Adjacent" from a neighboring room - only shows up once actually
    // entered (see HandleRoomEntered), since a secret room sits behind a bombable wall, not a door.
    public List<Vector2Int> secretRoomGridPositions = new List<Vector2Int>();
    public List<Vector2Int> bossRoomGridPositions = new List<Vector2Int>();
    public List<Vector2Int> shopRoomGridPositions = new List<Vector2Int>();
    public List<Vector2Int> eventRoomGridPositions = new List<Vector2Int>();
    public List<Vector2Int> treasureRoomGridPositions = new List<Vector2Int>();
    public List<Vector2Int> safeRoomGridPositions = new List<Vector2Int>();
    public Sprite bossIconSprite;
    public Sprite shopIconSprite;
    public Sprite eventIconSprite;
    public Sprite secretIconSprite;
    public Sprite safeIconSprite;
    // A hollow-centered square (see DungeonBootstrap's CreateRingSprite) shared by every outline
    // below - plain white so each can be tinted to its own color via Image.color.
    public Sprite outlineRingSprite;
    public Color bossOutlineColor = new Color(0.85f, 0.1f, 0.1f, 1f);
    public Color treasureOutlineColor = new Color(0.95f, 0.85f, 0.15f, 1f);
    public Color eventOutlineColor = new Color(0.55f, 0.25f, 0.85f, 1f);
    public Color secretOutlineColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    public Color safeOutlineColor = new Color(0.3f, 0.85f, 0.5f, 1f);
    public float cellSize = 12f;
    public float spacing = 3f;
    public float maxPanelSize = 240f;

    public Color undiscoveredColor = new Color(0f, 0f, 0f, 0f);
    public Color halfDiscoveredColor = new Color(0.35f, 0.33f, 0.4f, 0.9f);
    public Color discoveredColor = new Color(0.85f, 0.85f, 0.92f, 0.95f);
    public Color clearedColor = new Color(0.5f, 0.8f, 0.55f, 0.95f);
    public Color currentRoomColor = new Color(0.95f, 0.8f, 0.25f, 1f);

    [Header("Frame")]
    public Color frameColor = new Color(0.8f, 0.8f, 0.85f, 0.9f);
    public Color backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.8f);
    public float framePadding = 5f;

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    readonly Dictionary<Vector2Int, Image> icons = new Dictionary<Vector2Int, Image>();
    // Only holds an entry for rooms with a type icon and/or an outline (Boss/Shop/Event/Secret/
    // Treasure) - toggled on/off as a unit in Refresh() rather than colored like the plain rooms,
    // and parented to their own icons[gridPos] so they track its position automatically (including
    // through UpdateLayout's re-centering).
    readonly Dictionary<Vector2Int, GameObject> iconOverlays = new Dictionary<Vector2Int, GameObject>();
    readonly Dictionary<Vector2Int, RoomState> states = new Dictionary<Vector2Int, RoomState>();
    Vector2Int currentGridPos;

    void Start()
    {
        BuildFrame();
        BuildIcons();
        if (roomCamera != null) roomCamera.OnRoomEntered += HandleRoomEntered;
        foreach (RoomController room in monsterRooms)
        {
            if (room != null) room.OnRoomCleared += HandleRoomCleared;
        }
        HandleRoomEntered(Vector2Int.zero); // reveal the starting room immediately
    }

    void OnDestroy()
    {
        if (roomCamera != null) roomCamera.OnRoomEntered -= HandleRoomEntered;
        foreach (RoomController room in monsterRooms)
        {
            if (room != null) room.OnRoomCleared -= HandleRoomCleared;
        }
    }

    void BuildFrame()
    {
        GameObject frame = new GameObject("Frame", typeof(Image));
        frame.transform.SetParent(transform, false);
        frame.GetComponent<Image>().color = frameColor;
        RectTransform frameRt = frame.GetComponent<RectTransform>();
        frameRt.anchorMin = Vector2.zero;
        frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = Vector2.zero;
        frameRt.offsetMax = Vector2.zero;

        GameObject background = new GameObject("Background", typeof(Image));
        background.transform.SetParent(transform, false);
        background.GetComponent<Image>().color = backgroundColor;
        RectTransform bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(framePadding, framePadding);
        bgRt.offsetMax = new Vector2(-framePadding, -framePadding);
    }

    void BuildIcons()
    {
        foreach (Vector2Int gridPos in allRoomGridPositions)
        {
            GameObject go = new GameObject("Room_" + gridPos, typeof(Image));
            go.transform.SetParent(transform, false);

            Image img = go.GetComponent<Image>();
            img.color = undiscoveredColor;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(cellSize, cellSize);
            rt.anchoredPosition = new Vector2(gridPos.x * (cellSize + spacing), gridPos.y * (cellSize + spacing));

            icons[gridPos] = img;

            Sprite typeIcon = GetTypeIconSprite(gridPos);
            Color? outlineColor = GetOutlineColor(gridPos);
            if (typeIcon != null || outlineColor != null)
            {
                GameObject overlay = new GameObject("Overlay", typeof(RectTransform));
                overlay.transform.SetParent(go.transform, false);
                RectTransform overlayRt = (RectTransform)overlay.transform;
                overlayRt.anchorMin = overlayRt.anchorMax = new Vector2(0.5f, 0.5f);
                overlayRt.pivot = new Vector2(0.5f, 0.5f);
                overlayRt.anchoredPosition = Vector2.zero;
                overlayRt.sizeDelta = Vector2.zero;

                // Drawn first (behind the type icon below); the ring sprite's hollow center lets
                // the room's own square (the parent, rendered further behind still) show through,
                // so only the border itself reads as a colored outline around the cell.
                if (outlineColor != null && outlineRingSprite != null)
                {
                    GameObject outlineGO = new GameObject("Outline", typeof(Image));
                    outlineGO.transform.SetParent(overlay.transform, false);

                    Image outlineImg = outlineGO.GetComponent<Image>();
                    outlineImg.sprite = outlineRingSprite;
                    outlineImg.color = outlineColor.Value;
                    outlineImg.raycastTarget = false;

                    RectTransform outlineRt = outlineImg.rectTransform;
                    outlineRt.anchorMin = outlineRt.anchorMax = new Vector2(0.5f, 0.5f);
                    outlineRt.pivot = new Vector2(0.5f, 0.5f);
                    outlineRt.anchoredPosition = Vector2.zero;
                    outlineRt.sizeDelta = new Vector2(cellSize, cellSize);
                }

                if (typeIcon != null)
                {
                    GameObject iconGO = new GameObject("TypeIcon", typeof(Image));
                    iconGO.transform.SetParent(overlay.transform, false);

                    Image iconImg = iconGO.GetComponent<Image>();
                    iconImg.sprite = typeIcon;
                    iconImg.preserveAspect = true;

                    RectTransform iconRt = iconImg.rectTransform;
                    iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRt.pivot = new Vector2(0.5f, 0.5f);
                    iconRt.anchoredPosition = Vector2.zero;
                    iconRt.sizeDelta = new Vector2(cellSize * 0.8f, cellSize * 0.8f);
                }

                overlay.SetActive(false); // hidden until Refresh() decides it should show
                iconOverlays[gridPos] = overlay;
            }
        }
    }

    Sprite GetTypeIconSprite(Vector2Int gridPos)
    {
        if (bossRoomGridPositions.Contains(gridPos)) return bossIconSprite;
        if (shopRoomGridPositions.Contains(gridPos)) return shopIconSprite;
        if (eventRoomGridPositions.Contains(gridPos)) return eventIconSprite;
        if (secretRoomGridPositions.Contains(gridPos)) return secretIconSprite;
        if (safeRoomGridPositions.Contains(gridPos)) return safeIconSprite;
        return null;
    }

    Color? GetOutlineColor(Vector2Int gridPos)
    {
        if (bossRoomGridPositions.Contains(gridPos)) return bossOutlineColor;
        if (treasureRoomGridPositions.Contains(gridPos)) return treasureOutlineColor;
        if (eventRoomGridPositions.Contains(gridPos)) return eventOutlineColor;
        if (secretRoomGridPositions.Contains(gridPos)) return secretOutlineColor;
        if (safeRoomGridPositions.Contains(gridPos)) return safeOutlineColor;
        return null;
    }

    void HandleRoomEntered(Vector2Int gridPos)
    {
        if (!icons.ContainsKey(gridPos)) return;

        currentGridPos = gridPos;
        // Cleared is sticky - re-entering a cleared room must not downgrade it back to Discovered.
        if (!states.TryGetValue(gridPos, out RoomState current) || current != RoomState.Cleared)
            states[gridPos] = RoomState.Discovered;

        foreach (Vector2Int dir in Dirs)
        {
            Vector2Int neighbor = gridPos + dir;
            if (secretRoomGridPositions.Contains(neighbor)) continue;
            if (icons.ContainsKey(neighbor) && !states.ContainsKey(neighbor)) states[neighbor] = RoomState.Adjacent;
        }

        Refresh();
        UpdateLayout();
    }

    void HandleRoomCleared(Vector2Int[] memberCells)
    {
        bool any = false;
        foreach (Vector2Int cell in memberCells)
        {
            if (!icons.ContainsKey(cell)) continue;
            states[cell] = RoomState.Cleared;
            any = true;
        }
        if (any) Refresh();
    }

    void Refresh()
    {
        foreach (KeyValuePair<Vector2Int, Image> kv in icons)
        {
            states.TryGetValue(kv.Key, out RoomState state);
            Color c;
            if (kv.Key == currentGridPos) c = currentRoomColor;
            else if (state == RoomState.Cleared) c = clearedColor;
            else if (state == RoomState.Discovered) c = discoveredColor;
            else if (state == RoomState.Adjacent) c = halfDiscoveredColor;
            else c = undiscoveredColor;
            kv.Value.color = c;
        }

        foreach (KeyValuePair<Vector2Int, GameObject> kv in iconOverlays)
        {
            bool hasEntry = states.TryGetValue(kv.Key, out RoomState state);
            // A secret room's icon only shows once actually entered (Discovered) - never just from
            // being adjacent, same rule as its background color never pre-revealing.
            bool visible = secretRoomGridPositions.Contains(kv.Key) ? (hasEntry && state == RoomState.Discovered) : hasEntry;
            kv.Value.SetActive(visible);
        }
    }

    // The map's own footprint grows (and its content recenters) to match the bounding box of
    // everything revealed so far: a single room at floor start, more as exploration continues.
    void UpdateLayout()
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector2Int p in states.Keys)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }

        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;
        int gridSpanX = maxX - minX + 1;
        int gridSpanY = maxY - minY + 1;

        float step = cellSize + spacing;
        float width = Mathf.Min(maxPanelSize, gridSpanX * step + framePadding * 2f + spacing);
        float height = Mathf.Min(maxPanelSize, gridSpanY * step + framePadding * 2f + spacing);
        GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

        foreach (KeyValuePair<Vector2Int, Image> kv in icons)
        {
            kv.Value.rectTransform.anchoredPosition = new Vector2((kv.Key.x - centerX) * step, (kv.Key.y - centerY) * step);
        }
    }
}
