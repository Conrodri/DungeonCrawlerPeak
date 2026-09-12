using UnityEngine;

// Hidden floor hazard - looks like any other floor decal, damages once on first contact then goes
// inert (visually indistinguishable from a spent FuelPuddle).
public class FloorTrap : MonoBehaviour
{
    public int damage = 2;

    bool triggered;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered || !other.CompareTag("Player")) return;
        triggered = true;

        Health health = other.GetComponent<Health>();
        if (health != null) health.TakeDamage(damage);
    }
}
