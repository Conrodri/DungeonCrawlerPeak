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

    Rect currentRoomRect;
    bool hasRoom;
    Vector2Int? currentGridPos;

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

        Vector3 desired = new Vector3(currentRoomRect.center.x, currentRoomRect.center.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);
    }
}
