using UnityEngine;

// Fully crossing a doorway teleports the player straight to the connected room - no walkable
// corridor in between.
public class DoorTrigger : MonoBehaviour
{
    public Vector2 destination;

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
