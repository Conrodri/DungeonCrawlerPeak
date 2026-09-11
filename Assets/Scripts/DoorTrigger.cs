using UnityEngine;

// Fully crossing a doorway teleports the player straight to the connected room - no walkable
// corridor in between.
public class DoorTrigger : MonoBehaviour
{
    public Vector2 destination;
    // The room this trigger leads into (null if that room is never lockable). Checked so a
    // locked door can't be bypassed by teleporting straight past its DoorBlocker from the
    // neighboring room.
    public RoomController destinationRoom;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (destinationRoom != null && destinationRoom.IsLocked) return;

        // Set both: whichever one the physics engine treats as authoritative on the next step,
        // the player ends up in the right place either way.
        other.transform.position = destination;
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb != null) rb.position = destination;
    }
}
