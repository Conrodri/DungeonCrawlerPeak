// The 6 tabs of the inventory filter (2026-09-21 request: "fait dans l'inventaire des categories,
// pour trier les items de l'inventaire : Tout, equippements, consommables, ressources, objets de
// quete, autres") - "Tout" isn't a value here, it's the UI's null-filter state (see InventoryUI).
public enum ItemCategory { Equipement, Consommable, Ressource, ObjetDeQuete, Autre }
