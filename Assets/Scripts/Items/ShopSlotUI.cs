using UnityEngine;
using UnityEngine.EventSystems;

// Hover tooltip for a single slot in the Marchand's visual shop grid (see
// DialogueManager.BuildShopGrid) - the click itself is handled by the sibling Button component,
// this only shows the item's name/description/rarity, same pattern as InventorySlotUI.OnPointerEnter.
public class ShopSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public DialogueOption option;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (TooltipUI.Instance == null || option == null || string.IsNullOrEmpty(option.purchaseItemId)) return;

        ItemDefinition definition = ItemDatabase.Get(option.purchaseItemId);
        if (definition == null) return;

        string body = definition.Description;
        body = (string.IsNullOrEmpty(body) ? "" : body + "\n") + ItemRarity.Name(definition.Rarity);
        string coloredName = "<color=#" + ItemRarity.HexColor(definition.Rarity) + ">" + definition.DisplayName + "</color>";
        TooltipUI.Instance.Show(coloredName, body, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }
}
