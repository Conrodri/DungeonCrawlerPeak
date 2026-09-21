using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum InventorySlotKind { Inventory, Hotbar, Equipment }

// Shared drag & drop behaviour for the InventoryUI grid slots, HotbarUI slots, and the equipment
// panel's slots (see InventoryUI.BuildEquipmentPanel) - one item can move between any of the
// three: reordered within the inventory, dragged onto a hotbar slot to make it usable with 1-5,
// or dragged onto a matching equipment slot to wear it.
public class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public PlayerInventory inventory;
    public PlayerEquipment equipment;
    // Lets a plain click on an Inventory-kind slot use/throw the item directly, without needing to
    // drag it onto the hotbar first - see OnPointerClick.
    public PlayerController player;
    public InventorySlotKind kind = InventorySlotKind.Inventory;
    // Inventory/Hotbar slot index. Unused for Equipment (see equipmentSlotType/ringIndex instead).
    public int index;
    public EquipmentSlotType equipmentSlotType;
    // Which of the 5 slots for a RingLeft/RingRight equipmentSlotType; unused for every other type.
    public int ringIndex;

    static InventorySlotUI draggedFrom;
    static GameObject dragIcon;

    string ItemId => kind switch
    {
        InventorySlotKind.Hotbar => inventory != null ? inventory.hotbarSlots[index] : null,
        InventorySlotKind.Equipment => equipment != null ? equipment.Get(equipmentSlotType, ringIndex) : null,
        _ => inventory != null ? inventory.GetSlot(index).itemId : null,
    };

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(ItemId)) return;
        draggedFrom = this;
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();

        ItemDefinition definition = ItemDatabase.Get(ItemId);
        Canvas canvas = GetComponentInParent<Canvas>();
        dragIcon = new GameObject("DragIcon", typeof(Image));
        dragIcon.transform.SetParent(canvas.transform, false);

        Image img = dragIcon.GetComponent<Image>();
        img.sprite = definition != null ? definition.Icon : null;
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.sizeDelta = ((RectTransform)transform).sizeDelta;
        dragIcon.transform.position = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragIcon != null) dragIcon.transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragIcon != null) Destroy(dragIcon);
        dragIcon = null;
        draggedFrom = null;
    }

    // Unity calls OnDrop on the slot under the pointer before OnEndDrag on the slot that started
    // the drag, so draggedFrom is still valid here.
    public void OnDrop(PointerEventData eventData)
    {
        if (draggedFrom == null || draggedFrom == this || inventory == null) return;

        if (draggedFrom.kind == InventorySlotKind.Inventory && kind == InventorySlotKind.Inventory)
        {
            inventory.SwapSlots(draggedFrom.index, index);
        }
        else if (draggedFrom.kind == InventorySlotKind.Inventory && kind == InventorySlotKind.Hotbar)
        {
            inventory.AssignHotbar(index, draggedFrom.ItemId);
        }
        else if (draggedFrom.kind == InventorySlotKind.Hotbar && kind == InventorySlotKind.Hotbar)
        {
            string sourceId = inventory.hotbarSlots[draggedFrom.index];
            string targetId = inventory.hotbarSlots[index];
            inventory.AssignHotbar(draggedFrom.index, targetId);
            inventory.AssignHotbar(index, sourceId);
        }
        else if (draggedFrom.kind == InventorySlotKind.Inventory && kind == InventorySlotKind.Equipment)
        {
            EquipFromInventory(draggedFrom.index);
        }
        else if (draggedFrom.kind == InventorySlotKind.Equipment && kind == InventorySlotKind.Inventory)
        {
            string itemId = draggedFrom.ItemId;
            if (string.IsNullOrEmpty(itemId)) return;
            draggedFrom.equipment.Set(draggedFrom.equipmentSlotType, draggedFrom.ringIndex, null);
            inventory.ReturnToSlotOrAdd(index, itemId);
        }
        else if (draggedFrom.kind == InventorySlotKind.Equipment && kind == InventorySlotKind.Equipment)
        {
            if (equipment == null || !PlayerEquipment.IsCompatible(draggedFrom.equipmentSlotType, equipmentSlotType)) return;
            string a = draggedFrom.equipment.Get(draggedFrom.equipmentSlotType, draggedFrom.ringIndex);
            string b = equipment.Get(equipmentSlotType, ringIndex);
            equipment.Set(equipmentSlotType, ringIndex, a);
            draggedFrom.equipment.Set(draggedFrom.equipmentSlotType, draggedFrom.ringIndex, b);
        }
        // Hotbar <-> Equipment: not meaningful (a hotbar slot only references a consumable) - no-op.
        // Hotbar -> Inventory: no-op, a hotbar slot is only a reference to an inventory item.
    }

    // Validates the item at `fromIndex` can go into THIS equipment slot (right category, right
    // slot type - rings accept either hand, see PlayerEquipment.IsCompatible) before touching
    // anything, then delegates to EquipInto below.
    void EquipFromInventory(int fromIndex)
    {
        if (equipment == null) return;
        string itemId = inventory.GetSlot(fromIndex).itemId;
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        if (definition == null || !definition.IsEquipment || !PlayerEquipment.IsCompatible(definition.EquipmentSlot, equipmentSlotType)) return;

        EquipInto(inventory, equipment, fromIndex, equipmentSlotType, ringIndex);
    }

    // Shared by a drag onto a specific equipment slot (EquipFromInventory above, which already
    // knows the exact target) and a double-click auto-equip (TryAutoEquip below, which has to pick
    // one) - swaps whatever's currently in (targetSlot, targetRingIndex) back into the same
    // inventory slot the new item came from.
    static void EquipInto(PlayerInventory inventory, PlayerEquipment equipment, int fromIndex, EquipmentSlotType targetSlot, int targetRingIndex)
    {
        string previouslyEquipped = equipment.Get(targetSlot, targetRingIndex);
        string removed = inventory.RemoveOneFromSlot(fromIndex);
        if (removed == null) return;
        equipment.Set(targetSlot, targetRingIndex, removed);
        if (!string.IsNullOrEmpty(previouslyEquipped)) inventory.ReturnToSlotOrAdd(fromIndex, previouslyEquipped);
    }

    // Double-click-to-equip (2026-09-19 request) - picks the item's own matching slot type
    // instead of requiring a drag onto a specific target the way EquipFromInventory does. A ring
    // goes into the first empty slot across both hands (left checked before right); if all 10 are
    // already full it falls back to swapping RingLeft[0], same "something always happens" outcome
    // a single-slot equipment type gets for free.
    static void TryAutoEquip(PlayerInventory inventory, PlayerEquipment equipment, int fromIndex)
    {
        if (equipment == null || inventory == null) return;
        string itemId = inventory.GetSlot(fromIndex).itemId;
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        if (definition == null || !definition.IsEquipment) return;

        EquipmentSlotType targetSlot = definition.EquipmentSlot;
        int targetRingIndex = 0;
        if (targetSlot == EquipmentSlotType.RingLeft && !FindEmptyRingSlot(equipment, out targetSlot, out targetRingIndex))
        {
            targetSlot = EquipmentSlotType.RingLeft;
            targetRingIndex = 0;
        }

        EquipInto(inventory, equipment, fromIndex, targetSlot, targetRingIndex);
    }

    static bool FindEmptyRingSlot(PlayerEquipment equipment, out EquipmentSlotType slot, out int ringIndex)
    {
        for (int i = 0; i < PlayerEquipment.RingSlotsPerHand; i++)
        {
            if (string.IsNullOrEmpty(equipment.Get(EquipmentSlotType.RingLeft, i))) { slot = EquipmentSlotType.RingLeft; ringIndex = i; return true; }
        }
        for (int i = 0; i < PlayerEquipment.RingSlotsPerHand; i++)
        {
            if (string.IsNullOrEmpty(equipment.Get(EquipmentSlotType.RingRight, i))) { slot = EquipmentSlotType.RingRight; ringIndex = i; return true; }
        }
        slot = EquipmentSlotType.RingLeft;
        ringIndex = 0;
        return false;
    }

    // Double-click-to-unequip (2026-09-19 request) - the mirror of TryAutoEquip, dropped back
    // into inventory.Add's normal stack-or-first-empty-slot placement rather than a specific index
    // (a double-click has no "target inventory slot" the way a drag onto one does).
    void UnequipToInventory()
    {
        if (equipment == null || inventory == null) return;
        string itemId = ItemId;
        if (string.IsNullOrEmpty(itemId)) return;

        equipment.Set(equipmentSlotType, ringIndex, null);
        inventory.Add(itemId, 1);
    }

    // A plain click (not a drag) on an inventory-grid item uses/throws it immediately in whatever
    // direction the player was last aiming - lets a throwable/potion be used straight from the
    // grid instead of requiring it to be dragged onto the hotbar first. A double-click instead
    // transfers the item to/from its equipment slot (2026-09-19 request) - see TryAutoEquip/
    // UnequipToInventory above.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount >= 2)
        {
            if (kind == InventorySlotKind.Inventory) TryAutoEquip(inventory, equipment, index);
            else if (kind == InventorySlotKind.Equipment) UnequipToInventory();
            return;
        }

        if (kind != InventorySlotKind.Inventory || player == null) return;
        string itemId = ItemId;
        if (string.IsNullOrEmpty(itemId)) return;

        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition == null || (!definition.IsThrowable && definition.HealAmount <= 0)) return;

        player.UseItem(itemId);
        InventoryUI.CloseIfOpen();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        string itemId = ItemId;
        if (string.IsNullOrEmpty(itemId) || TooltipUI.Instance == null) return;

        ItemDefinition definition = ItemDatabase.Get(itemId);
        if (definition == null) return;

        string body = ItemRarity.DescriptionWithRarity(definition.Description, definition.Rarity);
        if (definition.Weight > 0) body += "\nNecessite Force " + definition.Weight + ".";
        string coloredName = ItemRarity.ColoredName(definition.DisplayName, definition.Rarity);
        TooltipUI.Instance.Show(coloredName, body, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }
}
