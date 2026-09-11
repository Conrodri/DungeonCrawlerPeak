using UnityEngine;

// Fills a secret room's only doorway until it's bombed open - built from the same wall-colored
// sprite as a normal wall, so it's indistinguishable from any other wall segment from the outside.
// Destroying it (via a bomb, same detection as DoorBlocker in Bomb.cs) unlocks both DoorTriggers on
// this connection for good - there's no RoomController here to drive a relock.
public class SecretWallBlocker : MonoBehaviour
{
    public DoorTrigger triggerA;
    public DoorTrigger triggerB;

    void OnDestroy()
    {
        if (triggerA != null) triggerA.SetLocked(false);
        if (triggerB != null) triggerB.SetLocked(false);
    }
}
