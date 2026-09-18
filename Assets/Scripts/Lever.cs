using UnityEngine;

// A Staircase's Lever lock: walk up and press E to pull it, once, permanently (2026-09-16 rework -
// used to be a silent walk-OVER trigger with no prompt, no on-screen feedback when pulled, and a
// plain solid-color square sprite identical in style to ordinary room decor, which together made
// levers essentially undiscoverable - reported directly: "aucune interaction possible, aucune
// description, sprite basique, indiscernable d'un bloc quelconque"). Same proximity-E-prompt
// pattern as RestBed (a single instant action, no full panel needed).
[RequireComponent(typeof(CircleCollider2D))]
public class Lever : MonoBehaviour
{
    const string PullPrompt = "Appuyez sur E pour actionner le levier";
    // Multiplies the sprite's baked brown (see DungeonGenerator's leverSprite) rather than
    // replacing it outright - reads as "darkened/spent" without needing a second texture.
    static readonly Color PulledTint = new Color(0.5f, 0.8f, 0.5f);

    public Staircase target;

    GameObject promptGO;
    SpriteRenderer spriteRenderer;
    bool playerNearby;
    bool pulled;

    void Awake()
    {
        CircleCollider2D trigger = GetComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.9f; // default 0.5 read as too tight for a comfortable E-interact range
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;
        promptGO = InteractPromptUI.Build(canvasGO.transform, "LeverPrompt", PullPrompt).gameObject;
    }

    void Update()
    {
        if (pulled) return;

        if (promptGO != null) promptGO.SetActive(playerNearby);
        if (playerNearby && KeyBindings.WasPressedThisFrame(GameAction.Interact)) Pull();
    }

    const float ShakeDuration = 1f;
    const float ShakeMagnitude = 0.2f;

    void Pull()
    {
        pulled = true;
        if (promptGO != null) promptGO.SetActive(false);
        if (spriteRenderer != null) spriteRenderer.color = PulledTint;
        RoomCameraController.Instance?.Shake(ShakeDuration, ShakeMagnitude);

        if (target == null) return;
        (int pulledCount, int required) = target.NotifyLeverPulled();
        if (RoomAnnouncementUI.Instance != null)
            RoomAnnouncementUI.Instance.Show("Levier actionne (" + pulledCount + "/" + required + ")");
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
