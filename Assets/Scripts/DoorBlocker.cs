using System.Collections.Generic;
using UnityEngine;

// A solid barrier placed across a doorway while its room is locked. Destroying it (e.g. via a
// bomb) opens that specific door permanently - RoomController simply skips null entries from
// then on, including across room resets. That alone only removed the visual/physical barrier
// object itself though: the room's actual exit-blocking collider is its DoorTrigger, locked/
// unlocked as a whole by RoomController/BossRoomController.UpdateDoors based on the room's
// overall clear state, not per-door - so without linkedTriggers below, bombing one door open
// mid-fight had no effect on whether the player could actually leave through it (a real bug:
// "j'ai fait exploser la porte, je n'ai pas pu revenir dans la salle precedente"). Wired up in
// DungeonGenerator.CreateDoorLink, matched by position to the DoorTrigger(s) sitting at this
// same doorway.
public class DoorBlocker : MonoBehaviour
{
    public List<DoorTrigger> linkedTriggers = new List<DoorTrigger>();

    void OnDestroy()
    {
        foreach (DoorTrigger trigger in linkedTriggers)
        {
            if (trigger != null) trigger.ForceUnlockPermanently();
        }
    }
}
