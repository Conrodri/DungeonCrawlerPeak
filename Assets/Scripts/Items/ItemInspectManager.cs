using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Proximity prompt + description panel for ground items that need a look before picking up
// (weight/curse/trap) - same E-key interact pattern as DialogueManager/NpcInteractable, kept
// deliberately separate so the two never fight over which one reacts to E.
public class ItemInspectManager : MonoBehaviour
{
    public static ItemInspectManager Instance { get; private set; }

    public Transform player;
    public GameObject promptGO;
    public GameObject panel;
    public Text nameText;
    public Text bodyText;

    ItemPickup nearbyItem;
    bool isOpen;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetNearbyItem(ItemPickup item)
    {
        nearbyItem = item;
    }

    public void ClearNearbyItem(ItemPickup item)
    {
        if (nearbyItem != item) return;
        nearbyItem = null;
        if (isOpen) Close();
    }

    void Update()
    {
        if (DialogueManager.IsOpen) return;

        if (isOpen)
        {
            if (nearbyItem == null) { Close(); return; }
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) PickUp();
            return;
        }

        if (promptGO != null) promptGO.SetActive(nearbyItem != null);
        if (nearbyItem != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Open();
    }

    void Open()
    {
        isOpen = true;
        if (promptGO != null) promptGO.SetActive(false);
        panel.SetActive(true);

        ItemDefinition definition = ItemDatabase.Get(nearbyItem.ItemId);
        nameText.text = definition != null
            ? "<color=#" + ItemRarity.HexColor(definition.Rarity) + ">" + definition.DisplayName + "</color>"
            : nearbyItem.ItemId;

        string body = definition != null ? definition.Description : "";
        if (definition != null) body = (string.IsNullOrEmpty(body) ? "" : body + "\n") + ItemRarity.Name(definition.Rarity);
        if (definition != null && definition.Weight > 0) body += "\nNecessite Force " + definition.Weight + ".";
        body += "\n\n[E] Ramasser";
        bodyText.text = body;
    }

    void PickUp()
    {
        if (nearbyItem != null && player != null)
        {
            Collider2D playerCollider = player.GetComponent<Collider2D>();
            if (playerCollider != null) nearbyItem.TryPickup(playerCollider);
        }
        Close();
    }

    void Close()
    {
        isOpen = false;
        panel.SetActive(false);
    }
}
