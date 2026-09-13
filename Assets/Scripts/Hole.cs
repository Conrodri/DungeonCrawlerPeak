using UnityEngine;

// Permanent pit in the floor: a solid Collider2D blocks any grounded Rigidbody2D (player,
// non-flying enemies) the same way a wall or DestructibleObject would. EnemyController's Start()
// looks up every Hole in the scene and ignores collision against it for isFlying enemies, so a
// Chauve-souris crosses freely instead of being stopped.
[RequireComponent(typeof(Collider2D))]
public class Hole : MonoBehaviour
{
}
