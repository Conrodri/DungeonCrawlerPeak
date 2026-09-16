using UnityEngine;

// Fully crossing a doorway teleports the player straight to the connected room - no walkable
// corridor in between. Entering a locked room is always allowed (that's how you engage it); this
// collider is switched solid only to block LEAVING through it while the room it sits in is
// locked - no visible barrier needed, since this collider has no sprite.
[RequireComponent(typeof(BoxCollider2D))]
public class DoorTrigger : MonoBehaviour
{
    public Vector2 destination;

    BoxCollider2D boxCollider;
    // Set once by DoorBlocker.OnDestroy (a bomb blew this specific door open) - from then on
    // SetLocked is a no-op, so the room re-locking for its OTHER doors (RoomController.UpdateDoors
    // runs on every remaining enemy death) can never re-seal this one.
    bool permanentlyOpen;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
    }

    public void SetLocked(bool locked)
    {
        if (permanentlyOpen) return;
        boxCollider.isTrigger = !locked;
    }

    public void ForceUnlockPermanently()
    {
        permanentlyOpen = true;
        boxCollider.isTrigger = true;
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
