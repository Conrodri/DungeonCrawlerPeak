using System;
using UnityEngine;

// One per floor, lives under DungeonRoot so it's destroyed and rebuilt with each new floor.
// Counts UP (not down) so Staircase's Timed lock can compare "minutes elapsed" directly against
// its own unlock threshold - FloorTimerUI derives the on-screen countdown from duration - Elapsed.
public class FloorTimer : MonoBehaviour
{
    public float duration = 600f;
    public event Action OnCollapse;

    float startTime;
    bool collapsed;

    public float Elapsed => Time.time - startTime;
    public float Remaining => Mathf.Max(0f, duration - Elapsed);

    void Awake()
    {
        startTime = Time.time;
    }

    void Update()
    {
        if (collapsed) return;
        if (Elapsed >= duration)
        {
            collapsed = true;
            OnCollapse?.Invoke();
        }
    }
}
