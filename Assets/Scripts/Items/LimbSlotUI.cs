using UnityEngine;
using UnityEngine.EventSystems;

// One rectangle of InventoryUI's body silhouette - shows a hover tooltip with this part's exact
// HP/state (same TooltipUI singleton InventorySlotUI already uses for items, see its
// OnPointerEnter). Color/state itself is driven by InventoryUI.Refresh, not here - this component
// only owns the hover interaction.
public class LimbSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PlayerLimbs limbs;
    public BodyPart part;
    public string label;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (limbs == null || TooltipUI.Instance == null) return;

        LimbState state = limbs.GetState(part);
        string stateLabel = state == LimbState.Healthy ? "Sain" : state == LimbState.Damaged ? "Endommage" : "Casse";
        string body = "PV : " + limbs.GetLimbHealth(part) + "/" + PlayerLimbs.MaxLimbHealth + " (" + stateLabel + ")";
        if (state == LimbState.Broken) body += "\nSoins normaux sans effet - va voir le Tavernier pour te reposer.";
        TooltipUI.Instance.Show(label, body, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }
}
