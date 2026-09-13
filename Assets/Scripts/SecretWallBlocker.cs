using UnityEngine;

// Fills a secret room's only doorway until it's bombed open - built from the same wall-colored
// sprite as a normal wall, so it's indistinguishable from any other wall segment from the outside
// (unless the player wears the vision glasses - see Update()). Destroying it (via a bomb, same
// detection as DoorBlocker in Bomb.cs) unlocks both DoorTriggers on this connection for good -
// there's no RoomController here to drive a relock.
public class SecretWallBlocker : MonoBehaviour
{
    public DoorTrigger triggerA;
    public DoorTrigger triggerB;

    static readonly Color NormalTint = Color.white;
    // Translucent so it still reads as "part of the wall", just visibly not solid.
    static readonly Color RevealedTint = new Color(0.55f, 0.75f, 0.95f, 0.55f);

    SpriteRenderer spriteRenderer;
    PlayerEquipment playerEquipment;
    bool lookedUpPlayer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // Deferred rather than looked up in Awake()/Start() - the player GameObject doesn't exist
        // yet at the point DungeonGenerator.Build() creates this (door carving runs before the
        // player is spawned), and a null result would otherwise never be retried.
        if (!lookedUpPlayer)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerEquipment = player.GetComponent<PlayerEquipment>();
                lookedUpPlayer = true;
            }
        }

        bool revealed = playerEquipment != null && playerEquipment.Get(EquipmentSlotType.Head) == ItemIds.VisionGlasses;
        spriteRenderer.color = revealed ? RevealedTint : NormalTint;
    }

    void OnDestroy()
    {
        if (triggerA != null) triggerA.SetLocked(false);
        if (triggerB != null) triggerB.SetLocked(false);
    }
}
