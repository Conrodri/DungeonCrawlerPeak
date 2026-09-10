using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MinimapController : MonoBehaviour
{
    public RoomCameraController roomCamera;
    public List<Vector2Int> allRoomGridPositions = new List<Vector2Int>();
    public float cellSize = 12f;
    public float spacing = 3f;

    public Color undiscoveredColor = new Color(0f, 0f, 0f, 0f);
    public Color halfDiscoveredColor = new Color(0.35f, 0.33f, 0.4f, 0.9f);
    public Color discoveredColor = new Color(0.85f, 0.85f, 0.92f, 0.95f);
    public Color currentRoomColor = new Color(0.95f, 0.8f, 0.25f, 1f);

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    readonly Dictionary<Vector2Int, Image> icons = new Dictionary<Vector2Int, Image>();
    readonly HashSet<Vector2Int> discovered = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> halfDiscovered = new HashSet<Vector2Int>();
    Vector2Int currentGridPos;

    void Start()
    {
        BuildIcons();
        if (roomCamera != null) roomCamera.OnRoomEntered += HandleRoomEntered;
        HandleRoomEntered(Vector2Int.zero); // reveal the starting room immediately
    }

    void OnDestroy()
    {
        if (roomCamera != null) roomCamera.OnRoomEntered -= HandleRoomEntered;
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
        }
    }

    void HandleRoomEntered(Vector2Int gridPos)
    {
        if (!icons.ContainsKey(gridPos)) return;

        currentGridPos = gridPos;
        discovered.Add(gridPos);
        halfDiscovered.Remove(gridPos);

        foreach (Vector2Int dir in Dirs)
        {
            Vector2Int neighbor = gridPos + dir;
            if (icons.ContainsKey(neighbor) && !discovered.Contains(neighbor)) halfDiscovered.Add(neighbor);
        }

        Refresh();
    }

    void Refresh()
    {
        foreach (KeyValuePair<Vector2Int, Image> kv in icons)
        {
            Color c;
            if (kv.Key == currentGridPos) c = currentRoomColor;
            else if (discovered.Contains(kv.Key)) c = discoveredColor;
            else if (halfDiscovered.Contains(kv.Key)) c = halfDiscoveredColor;
            else c = undiscoveredColor;
            kv.Value.color = c;
        }
    }
}
