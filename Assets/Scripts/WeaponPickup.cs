using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    public PlayerController.WeaponType weapon;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerController controller = other.GetComponent<PlayerController>();
        if (controller == null) return;

        controller.EquipWeapon(weapon);
        Destroy(gameObject);
    }
}
