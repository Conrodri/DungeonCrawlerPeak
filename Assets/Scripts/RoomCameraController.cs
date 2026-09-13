using System;
using UnityEngine;

public class RoomCameraController : MonoBehaviour
{
    [Serializable]
    public struct RoomEntry
    {
        public Vector2Int gridPos;
        // This one cell's own bounds - used only to detect exactly which cell the player is
        // standing in (drives OnRoomEntered, so per-cell consumers like MinimapController's
        // walked-over reveal and RoomController's per-cell gating keep working unchanged even
        // inside a merged duo/trio/quad room).
        public Rect rect;
        // The bounds the CAMERA actually frames on: the full merged group's rect for every member
        // cell of a duo/trio/quad room (identical value for all of them), or the same as `rect`
        // for an unmerged room. Keeping this separate from `rect` is what stops the camera from
        // recentring/snapping at a former seam inside a merged room - see DungeonGenerator's
        // GroupWorldRect.
        public Rect cameraRect;
    }

    public Transform target;
    public RoomEntry[] rooms;
    public float followSpeed = 22f;

    public event Action<Vector2Int> OnRoomEntered;

    Camera cam;
    Rect currentRoomRect;
    bool hasRoom;
    Vector2Int? currentGridPos;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null || rooms == null) return;

        Vector2 pos = target.position;
        for (int i = 0; i < rooms.Length; i++)
        {
            if (!rooms[i].rect.Contains(pos)) continue;

            currentRoomRect = rooms[i].cameraRect;
            hasRoom = true;

            if (currentGridPos != rooms[i].gridPos)
            {
                currentGridPos = rooms[i].gridPos;
                OnRoomEntered?.Invoke(rooms[i].gridPos);
            }
            break;
        }

        if (!hasRoom) return;

        // Follows the player, clamped so the view never shows outside currentRoomRect. On a plain
        // single-cell room the view is sized to fit the room exactly (see DungeonGenerator's
        // orthographicSize = RoomHeight / 2f), so the clamp range collapses to a single point and
        // this reproduces the old fixed-at-center behavior automatically. On a merged duo/trio/
        // quad room - bigger than the view - the range opens up and the camera actually tracks the
        // player across it instead of freezing on the group's center regardless of where they are.
        float halfHeight = cam != null && cam.orthographic ? cam.orthographicSize : currentRoomRect.height / 2f;
        float halfWidth = cam != null && cam.orthographic ? halfHeight * cam.aspect : currentRoomRect.width / 2f;

        float x = ClampCenter(pos.x, currentRoomRect.xMin, currentRoomRect.xMax, halfWidth);
        float y = ClampCenter(pos.y, currentRoomRect.yMin, currentRoomRect.yMax, halfHeight);

        Vector3 desired = new Vector3(x, y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);
    }

    // Keeps the camera center within [min + halfExtent, max - halfExtent] so the view's edge never
    // crosses the room bounds. Falls back to the room's own center when it's narrower than the
    // view on this axis (the range would otherwise be inverted).
    static float ClampCenter(float playerCoord, float min, float max, float halfExtent)
    {
        float lo = min + halfExtent;
        float hi = max - halfExtent;
        if (lo > hi) return (min + max) / 2f;
        return Mathf.Clamp(playerCoord, lo, hi);
    }
}
