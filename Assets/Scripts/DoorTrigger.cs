using UnityEngine;

// Fully crossing a doorway teleports the player straight to the connected room - no walkable
// corridor in between. While the destination room is locked, this same collider is switched to
// solid (see SetLocked) instead of a separate visible barrier - the neighboring room must always
// look open, even though the doorway itself silently refuses to let the player through.
[RequireComponent(typeof(BoxCollider2D))]
public class DoorTrigger : MonoBehaviour
{
    public Vector2 destination;
    // The room this trigger leads into (null if that room is never lockable).
    public RoomController destinationRoom;

    BoxCollider2D boxCollider;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
    }

    public void SetLocked(bool locked)
    {
        boxCollider.isTrigger = !locked;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // Set both: whichever one the physics engine treats as authoritative on the next step,
        // the player ends up in the right place either way.
        other.transform.position = destination;
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb != null) rb.position = destination;
    }
}
