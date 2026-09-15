using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A physical rest point in the Safe room (2026-09-14: replaces the Tavernier's old "Se reposer"
// dialogue option - resting is something the player does in the room now, not a menu item an NPC
// sells access to). Same proximity-prompt/E-to-interact/self-built-UI pattern as TutorialNpc, since
// this is a single action with no branching - no need to route it through the shared dialogue panel.
[RequireComponent(typeof(CircleCollider2D))]
public class RestBed : MonoBehaviour
{
    const string DefaultPrompt = "Appuyez sur E pour vous reposer";
    const float MessageDuration = 2f;

    public PlayerInventory playerInventory;
    public PlayerStats playerStats;
    public Health playerHealth;
    public Stamina playerStamina;
    public PlayerController playerController;
    public PlayerEquipment playerEquipment;
    public PlayerLimbs playerLimbs;

    GameObject promptGO;
    Text promptLabel;
    bool playerNearby;
    float messageUntil;

    void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Start()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) return;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);
        promptGO = new GameObject("RestPrompt", typeof(Text));
        promptGO.transform.SetParent(canvasGO.transform, false);
        promptLabel = promptGO.GetComponent<Text>();
        promptLabel.font = font;
        promptLabel.fontSize = 28;
        promptLabel.alignment = TextAnchor.MiddleCenter;
        promptLabel.color = Color.white;
        promptLabel.text = DefaultPrompt;
        RectTransform rect = promptLabel.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 120f);
        rect.sizeDelta = new Vector2(500f, 40f);
        promptGO.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current == null || promptGO == null) return;

        if (Time.time < messageUntil)
        {
            promptGO.SetActive(true);
            return;
        }
        if (promptLabel.text != DefaultPrompt) promptLabel.text = DefaultPrompt;

        promptGO.SetActive(playerNearby);
        if (playerNearby && Keyboard.current.eKey.wasPressedThisFrame) Rest();
    }

    // Same heal+repair+save sequence the Tavernier's "Se reposer" option used to trigger via
    // DialogueOutcome.savesGame (see DialogueManager.ApplyOutcome) - applied directly here now.
    void Rest()
    {
        if (playerHealth != null) playerHealth.Heal(playerHealth.maxHealth);
        if (playerLimbs != null) playerLimbs.RepairAll();
        SaveManager.Save(DungeonGenerator.CurrentSeed, DungeonGenerator.CurrentFloor, playerInventory, playerStats, playerHealth, playerStamina, playerController,
            playerEquipment, DungeonGenerator.ClearedRoomsThisFloor, DungeonGenerator.BossDefeatedThisFloor, playerLimbs, DungeonGenerator.UsedBossBiomesBeforeCurrentFloor);

        promptLabel.text = "Vous vous reposez un moment. Votre progression est sauvegardee.";
        messageUntil = Time.time + MessageDuration;
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
