using System;
using UnityEngine;

public class RoomCameraController : MonoBehaviour
{
    [Serializable]
    public struct RoomEntry
    {
        public Vector2Int gridPos;
        // This one cell's own bounds - used only to detect exactly which cell the player is
        // standing in (drives OnRoomEntered, so per-cell consumers like MinimapController's
        // walked-over reveal and RoomController's per-cell gating keep working unchanged even
        // inside a merged duo/trio/quad room).
        public Rect rect;
        // The bounds the CAMERA actually frames on: the full merged group's rect for every member
        // cell of a duo/trio/quad room (identical value for all of them), or the same as `rect`
        // for an unmerged room. Keeping this separate from `rect` is what stops the camera from
        // recentring/snapping at a former seam inside a merged room - see DungeonGenerator's
        // GroupWorldRect.
        public Rect cameraRect;
    }

    public Transform target;
    public RoomEntry[] rooms;
    public float followSpeed = 22f;
    // Set by BossRoomController's intro cutscene (2026-09-21 request) to pan the camera onto the
    // boss instead of the player, without disturbing room-entry detection below (which always
    // stays keyed on the real player position - see LateUpdate) - null resumes normal following.
    public Transform overrideTarget;

    public event Action<Vector2Int> OnRoomEntered;

    // This component lives on the persistent Main Camera, reused across every floor rebuild
    // (DungeonGenerator.Build only ever destroys/recreates DungeonRoot, never the camera) - a bare
    // event has no owning object to unsubscribe it automatically, unlike RoomController/
    // BossRoomController's own subscriptions (cleaned up in their OnDestroy). DungeonGenerator.Build
    // calls this before re-wiring its own listeners each floor, so a previous floor's inline
    // lambda (capturing a UI object already destroyed along with the old Canvas) never lingers.
    public void ClearListeners() => OnRoomEntered = null;

    // Same static-instance convention as TooltipUI/RoomAnnouncementUI - lets a gameplay object
    // with no direct wiring to the camera (see Lever.cs) trigger a shake without DungeonGenerator
    // threading a reference through every spawn call that might ever want one.
    public static RoomCameraController Instance { get; private set; }

    Camera cam;
    Rect currentRoomRect;
    bool hasRoom;
    Vector2Int? currentGridPos;

    // Screen shake - a random offset added on top of the normal follow position each frame,
    // decaying linearly to zero over shakeDuration (2026-09-16 request: lever activation). Lives
    // here rather than a separate component: this is the only thing that ever sets the camera's
    // transform.position every frame (see LateUpdate below), so a separate shaker fighting over
    // the same field would just get overwritten on alternating frames.
    float shakeEndTime = -999f;
    float shakeDuration;
    float shakeMagnitude;

    void Awake()
    {
        cam = GetComponent<Camera>();
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Shake(float duration, float magnitude)
    {
        shakeDuration = duration;
        shakeMagnitude = magnitude;
        shakeEndTime = Time.time + duration;
    }

    void LateUpdate()
    {
        if (target == null || rooms == null) return;

        // Room-entry detection ALWAYS stays keyed on the real player position, even while
        // overrideTarget is panning the view elsewhere (see BossRoomController's intro cutscene) -
        // otherwise a boss sitting in a different cell of a merged arena than the player entered
        // from would fire a spurious OnRoomEntered the instant the camera reaches it, re-triggering
        // room logic (minimap reveal, RoomController resets, etc.) for a cell the player never
        // actually walked into.
        Vector2 pos = target.position;
        for (int i = 0; i < rooms.Length; i++)
        {
            if (!rooms[i].rect.Contains(pos)) continue;

            currentRoomRect = rooms[i].cameraRect;
            hasRoom = true;

            if (currentGridPos != rooms[i].gridPos)
            {
                currentGridPos = rooms[i].gridPos;
                OnRoomEntered?.Invoke(rooms[i].gridPos);
            }
            break;
        }

        if (!hasRoom) return;

        // Follows the player (or overrideTarget, if set), clamped so the view never shows outside
        // currentRoomRect. On a plain single-cell room the view is sized to fit the room exactly
        // (see DungeonGenerator's orthographicSize = RoomHeight / 2f), so the clamp range collapses
        // to a single point and this reproduces the old fixed-at-center behavior automatically. On
        // a merged duo/trio/quad room - bigger than the view - the range opens up and the camera
        // actually tracks the target across it instead of freezing on the group's center.
        Vector2 followPos = overrideTarget != null ? (Vector2)overrideTarget.position : pos;
        float halfHeight = cam != null && cam.orthographic ? cam.orthographicSize : currentRoomRect.height / 2f;
        float halfWidth = cam != null && cam.orthographic ? halfHeight * cam.aspect : currentRoomRect.width / 2f;

        float x = ClampCenter(followPos.x, currentRoomRect.xMin, currentRoomRect.xMax, halfWidth);
        float y = ClampCenter(followPos.y, currentRoomRect.yMin, currentRoomRect.yMax, halfHeight);

        Vector3 desired = new Vector3(x, y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);

        if (Time.time < shakeEndTime)
        {
            float t = shakeDuration > 0f ? (shakeEndTime - Time.time) / shakeDuration : 0f; // 1 -> 0
            Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeMagnitude * t;
            transform.position += (Vector3)offset;
        }
    }

    // Keeps the camera center within [min + halfExtent, max - halfExtent] so the view's edge never
    // crosses the room bounds. Falls back to the room's own center when it's narrower than the
    // view on this axis (the range would otherwise be inverted).
    static float ClampCenter(float playerCoord, float min, float max, float halfExtent)
    {
        float lo = min + halfExtent;
        float hi = max - halfExtent;
        if (lo > hi) return (min + max) / 2f;
        return Mathf.Clamp(playerCoord, lo, hi);
    }
}
