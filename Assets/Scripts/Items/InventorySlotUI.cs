using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared drag & drop behaviour for both the InventoryUI grid slots and the HotbarUI slots, so an
// item can be reordered within the inventory or dragged onto a hotbar slot to equip it.
public class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public PlayerInventory inventory;
    public bool isHotbarSlot;
    public int index;

    static InventorySlotUI draggedFrom;
    static GameObject dragIcon;

    string ItemId => inventory == null ? null : isHotbarSlot ? inventory.hotbarSlots[index] : inventory.GetSlot(index).itemId;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(ItemId)) return;
        draggedFrom = this;

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

        if (!draggedFrom.isHotbarSlot && !isHotbarSlot)
        {
            inventory.SwapSlots(draggedFrom.index, index);
        }
        else if (!draggedFrom.isHotbarSlot && isHotbarSlot)
        {
            inventory.AssignHotbar(index, draggedFrom.ItemId);
        }
        else if (draggedFrom.isHotbarSlot && isHotbarSlot)
        {
            string sourceId = inventory.hotbarSlots[draggedFrom.index];
            string targetId = inventory.hotbarSlots[index];
            inventory.AssignHotbar(draggedFrom.index, targetId);
            inventory.AssignHotbar(index, sourceId);
        }
        // Hotbar -> inventory: no-op, a hotbar slot is only a reference to an inventory item.
    }
}
