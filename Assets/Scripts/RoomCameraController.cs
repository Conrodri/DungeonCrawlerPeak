using UnityEngine;

public class RoomCameraController : MonoBehaviour
{
    public Transform target;
    public Rect[] roomBounds;
    public float followSpeed = 10f;

    Rect currentRoom;
    bool hasRoom;

    void LateUpdate()
    {
        if (target == null || roomBounds == null) return;

        Vector2 pos = target.position;
        for (int i = 0; i < roomBounds.Length; i++)
        {
            if (roomBounds[i].Contains(pos))
            {
                currentRoom = roomBounds[i];
                hasRoom = true;
                break;
            }
        }

        if (!hasRoom) return;

        Vector3 desired = new Vector3(currentRoom.center.x, currentRoom.center.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);
    }
}
