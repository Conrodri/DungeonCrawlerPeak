using UnityEngine;
using UnityEngine.UI;

// Opened from the crafting table (see DialogueOutcome.opensRepairPanel) - one fixed row per
// durability-bearing equipment slot (the 7 armor slots; rings never carry durability, see
// ItemDefinition.MaxDurability). Same "fixed rows, externally refreshed" pattern as
// AttributeAllocationUI - which rows exist never changes, only their live content does, since a
// static per-floor option list can't reflect what's actually equipped/damaged right now.
public class RepairUI : MonoBehaviour, UIWindowStack.IWindow
{
    public static RepairUI Instance { get; private set; }

    public GameObject root;
    public PlayerEquipment equipment;
    public PlayerInventory inventory;
    public EquipmentSlotType[] slots; // fixed 7 (armor only - weapons don't carry durability), in display order
    public Text[] nameLabels;
    public Text[] durabilityLabels;
    public Button[] repairButtons;
    public Button[] dismantleButtons;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show()
    {
        if (root != null) root.SetActive(true);
        UIWindowStack.Push(this);
        Refresh();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        UIWindowStack.Remove(this);
    }

    public bool TryCloseFromStack()
    {
        if (root == null || !root.activeSelf) return false;
        Hide();
        return true;
    }

    public static string MaterialItemId(MaterialType material) => material switch
    {
        MaterialType.Bois => ItemIds.Wood,
        MaterialType.Metal => ItemIds.Metal,
        MaterialType.Pierre => ItemIds.Stone,
        MaterialType.Tissu => ItemIds.Cloth,
        _ => null,
    };

    // 1 materiau pour 5 points de durabilite manquants, au moins 1 tant que l'objet n'est pas deja
    // au maximum - evite qu'une egratignure de 1 point coute 0.
    static int RepairCost(int missing) => missing <= 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(missing / 5f));

    public void Repair(int index)
    {
        EquipmentSlotType slot = slots[index];
        string itemId = equipment.Get(slot);
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        if (definition == null || definition.MaxDurability <= 0) return;

        int missing = definition.MaxDurability - equipment.GetDurability(slot);
        if (missing <= 0) return;

        string materialId = MaterialItemId(definition.Material);
        int cost = RepairCost(missing);
        if (materialId == null || inventory.GetCount(materialId) < cost) return;

        inventory.RemoveAmount(materialId, cost);
        equipment.Repair(slot, 0, missing);
        Refresh();
    }

    // Destroys the equipped item outright, refunding a fraction of its material - "decomposable en
    // materiaux" from the vision spec. Works at any durability, not just when damaged.
    public void Dismantle(int index)
    {
        EquipmentSlotType slot = slots[index];
        string itemId = equipment.Get(slot);
        ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
        if (definition == null) return;

        string materialId = MaterialItemId(definition.Material);
        equipment.Set(slot, 0, null);
        if (materialId != null) inventory.Add(materialId, Mathf.Max(1, definition.MaxDurability / 10));
        Refresh();
    }

    void Refresh()
    {
        if (equipment == null || slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            string itemId = equipment.Get(slots[i]);
            ItemDefinition definition = !string.IsNullOrEmpty(itemId) ? ItemDatabase.Get(itemId) : null;
            bool hasDurability = definition != null && definition.MaxDurability > 0;
            int current = hasDurability ? equipment.GetDurability(slots[i]) : 0;

            if (nameLabels[i] != null) nameLabels[i].text = definition != null ? definition.DisplayName : "(vide)";
            if (durabilityLabels[i] != null)
                durabilityLabels[i].text = hasDurability ? current + " / " + definition.MaxDurability : "-";
            if (repairButtons[i] != null) repairButtons[i].interactable = hasDurability && current < definition.MaxDurability;
            if (dismantleButtons[i] != null) dismantleButtons[i].interactable = definition != null;
        }
    }
}
