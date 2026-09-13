using UnityEngine;

// A Staircase's Lever lock: walking over it pulls it once, permanently, like FloorTrap's one-shot
// trigger - no E-key prompt (unlike NpcInteractable) to keep this self-contained.
public class Lever : MonoBehaviour
{
    public Staircase target;

    bool pulled;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (pulled || !other.CompareTag("Player")) return;
        pulled = true;

        if (target != null) target.Unlock();
        Debug.Log("Lever: pulled.");
    }
}
