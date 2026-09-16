using UnityEngine;

// Which condition gates this floor's one staircase down - rolled once per floor in
// DungeonGenerator.SetupStaircase. Open still has to be found (Stairs is hidden on the minimap
// until visited, same rule as Secret) - it just has no additional lock on top of that.
public enum StairsLockType { Open, Timed, BossKill, Lever }

// The lone way down to the next floor. A child "blocker" GameObject (solid, NOT a DoorBlocker -
// see DungeonGenerator.SetupStaircase for why) sits over the trigger while locked; whichever
// condition applies deactivates it via Unlock().
public class Staircase : MonoBehaviour
{
    public StairsLockType lockType = StairsLockType.Open;
    public float unlockAtElapsedSeconds; // Timed only
    public FloorTimer floorTimer; // Timed only
    public GameObject blocker;
    // Lever only - how many distinct Lever components must be pulled before this actually
    // unlocks (see NotifyLeverPulled). Defaults to 1, matching every floor before the 2026-09-15
    // floor-1 4-lever puzzle (see DungeonGenerator.SetupStaircase's FirstFloorLeverCount).
    public int leversRequired = 1;

    bool unlocked;
    int leversPulled;
    // Descend() rebuilds the whole DungeonRoot (this object included) - deferred to the next
    // Update() instead of firing straight from OnTriggerEnter2D, which runs mid-physics-step and
    // would otherwise destroy this GameObject's own hierarchy while Unity is still iterating that
    // step's other trigger callbacks.
    bool descendPending;

    void Start()
    {
        unlocked = lockType == StairsLockType.Open;
        if (blocker != null) blocker.SetActive(!unlocked);
    }

    void Update()
    {
        if (descendPending)
        {
            descendPending = false;
            DungeonGenerator.Descend();
            return;
        }

        if (unlocked || lockType != StairsLockType.Timed || floorTimer == null) return;
        if (floorTimer.Elapsed >= unlockAtElapsedSeconds) Unlock();
    }

    // Called by Lever.cs on every pull instead of Unlock() directly - only the LAST of
    // leversRequired pulls actually unlocks (a no-op default of 1 makes a single-lever floor
    // unlock on its one and only pull, exactly like before this existed). Returns the new
    // pulled/required tally so the calling Lever can show the player an on-screen count - pulling
    // one used to be entirely silent (a console-only Debug.Log), which was part of why levers were
    // so easy to miss/not understand (2026-09-16 report).
    public (int pulled, int required) NotifyLeverPulled()
    {
        leversPulled++;
        Debug.Log("Staircase: lever " + leversPulled + "/" + leversRequired + " pulled.");
        if (leversPulled >= leversRequired) Unlock();
        return (leversPulled, leversRequired);
    }

    public void Unlock()
    {
        if (unlocked) return;
        unlocked = true;
        if (blocker != null) blocker.SetActive(false);
        Debug.Log("Staircase: unlocked.");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!unlocked || descendPending || !other.CompareTag("Player")) return;
        descendPending = true;
    }
}
