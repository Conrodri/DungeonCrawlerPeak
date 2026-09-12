using System.Collections.Generic;
using UnityEngine;

// A talkable NPC: DialogueManager drives the actual conversation/roll flow, this component just
// holds its content and tells DialogueManager when the player is close enough to start one.
[RequireComponent(typeof(CircleCollider2D))]
public class NpcInteractable : MonoBehaviour
{
    public string npcName;
    public string greeting;
    public List<DialogueOption> options = new List<DialogueOption>();

    void Awake()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
    }

    void OnDestroy()
    {
        if (DialogueManager.Instance != null) DialogueManager.Instance.NotifyNpcRemoved(this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || DialogueManager.Instance == null) return;
        DialogueManager.Instance.SetNearbyNpc(this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || DialogueManager.Instance == null) return;
        DialogueManager.Instance.ClearNearbyNpc(this);
    }
}
