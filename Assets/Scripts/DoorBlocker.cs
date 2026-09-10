using UnityEngine;

// A solid barrier placed across a doorway while its room is locked. Destroying it (e.g. via a
// bomb) opens that specific door permanently - RoomController simply skips null entries from
// then on, including across room resets.
public class DoorBlocker : MonoBehaviour
{
}
