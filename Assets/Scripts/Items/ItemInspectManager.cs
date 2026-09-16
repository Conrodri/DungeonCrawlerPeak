using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Proximity prompt + description panel for ground items that need a look before picking up
// (weight/curse/trap/weapon) - same E-key interact pattern as DialogueManager/NpcInteractable, kept
// deliberately separate so the two never fight over which one reacts to E.
public class ItemInspectManager : MonoBehaviour, UIWindowStack.IWindow
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
            // Escape (see TryCloseFromStack) also closes this without picking up - explicit
            // request, the player can look at a description and simply decide not to take it.
            return;
        }

        if (promptGO != null) promptGO.SetActive(nearbyItem != null);
        if (nearbyItem != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Open();
    }

    void Open()
    {
        isOpen = true;
        UIWindowStack.Push(this);
        if (promptGO != null) promptGO.SetActive(false);
        panel.SetActive(true);

        ItemDefinition definition = ItemDatabase.Get(nearbyItem.ItemId);
        nameText.text = definition != null
            ? ItemRarity.ColoredName(definition.DisplayName, definition.Rarity)
            : nearbyItem.ItemId;

        string body = definition != null ? definition.Description : "";
        if (definition != null) body = ItemRarity.DescriptionWithRarity(body, definition.Rarity);
        if (definition != null && definition.Weight > 0) body += "\nNecessite Force " + definition.Weight + ".";
        body += "\n\n[E] Ramasser   [ECHAP] Laisser";
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

    public bool TryCloseFromStack()
    {
        if (!isOpen) return false;
        Close();
        return true;
    }

    void Close()
    {
        isOpen = false;
        panel.SetActive(false);
        UIWindowStack.Remove(this);
    }
}
