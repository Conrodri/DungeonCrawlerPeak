using System;
using UnityEngine;

public class RoomCameraController : MonoBehaviour
{
    [Serializable]
    public struct RoomEntry
    {
        public Vector2Int gridPos;
        public Rect rect;
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

            currentRoomRect = rooms[i].rect;
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
