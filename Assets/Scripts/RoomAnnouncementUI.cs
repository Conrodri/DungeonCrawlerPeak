using UnityEngine;
using UnityEngine.UI;

// Small transient banner naming the room type the player just walked into ("Vous entrez dans une
// salle de boss", etc.) - same auto-hide pattern as VictoryBannerUI. DungeonGenerator wires this to
// RoomCameraController.OnRoomEntered and only calls Show() when the room TYPE actually changes, so
// walking around inside one merged multi-cell room (which fires OnRoomEntered once per cell
// crossed) never re-triggers it.
public class RoomAnnouncementUI : MonoBehaviour
{
    // Same static-instance convention as TooltipUI - lets a gameplay object with no direct wiring
    // to this (see Lever.cs) show a message without DungeonGenerator having to thread a reference
    // all the way through the Staircase/Lever spawn calls just for this.
    public static RoomAnnouncementUI Instance { get; private set; }

    public GameObject root;
    public Text label;
    public float displayDuration = 2.5f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(string message)
    {
        if (label != null) label.text = message;
        if (root != null) root.SetActive(true);
        CancelInvoke(nameof(Hide));
        Invoke(nameof(Hide), displayDuration);
    }

    void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
