using UnityEngine;
using UnityEngine.UI;

// A wall-mounted board in every Safe room variant (Tavern/Restaurant/Arcade) that opens
// AttributeAllocationUI directly - same proximity-prompt/E-to-interact pattern as RestBed, since
// allocating points is a single action with no branching and no longer needs to be gated behind
// the Tavernier's dialogue (2026-09-21: moved out of SpawnTavernNpc so it's reachable from any
// Safe room, not just the first one).
[RequireComponent(typeof(CircleCollider2D))]
public class AttributeBoard : MonoBehaviour
{
    const string DefaultPrompt = "Appuyez sur E pour repartir vos points";

    GameObject promptGO;
    Text promptLabel;
    bool playerNearby;

    void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Start()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        promptLabel = InteractPromptUI.Build(canvasGO.transform, "AttributeBoardPrompt", DefaultPrompt);
        promptGO = promptLabel.gameObject;
    }

    void Update()
    {
        if (promptGO == null) return;

        promptGO.SetActive(playerNearby);
        if (playerNearby && KeyBindings.WasPressedThisFrame(GameAction.Interact) && AttributeAllocationUI.Instance != null)
        {
            AttributeAllocationUI.Instance.Show();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerNearby = false;
    }
}
