using UnityEngine;

// Permanent floor hazard, unlike FloorTrap's one-time spring - every fresh entry (OnTriggerEnter2D,
// not a continuous OnTriggerStay2D, so standing still doesn't keep re-triggering it) knocks out
// the player's sprint/roll for a few seconds, a penalty for crossing the pit instead of going
// around it. Ground enemies just walk over it - only the player has sprint/roll to lose.
public class Hole : MonoBehaviour
{
    public float debuffDuration = 5f;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerController controller = other.GetComponent<PlayerController>();
        if (controller != null) controller.ApplyMovementDebuff(debuffDuration);
    }
}
