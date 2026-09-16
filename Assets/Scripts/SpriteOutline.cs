using UnityEngine;

// Cheap outline for a small procedural/masked sprite (no shader): a second SpriteRenderer, same
// sprite, solid-tinted and scaled up slightly, parented directly behind the real one - the edges
// of the bigger copy peek out all the way around, reading as an outline at the small sizes these
// mobs render at (2026-09-16 request: "suivant les endroits les mobs sont difficiles a voir").
// Polls every frame instead of caching the sprite once (same reasoning as StaminaBarUI/HeartHUD -
// a Play Mode domain reload can wipe subscriptions, and this also has to survive the source
// sprite being assigned AFTER this component is added, e.g. RoomController.SpawnEnemies).
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    public Color outlineColor = Color.white;
    public float scale = 1.15f;

    SpriteRenderer source;
    SpriteRenderer outline;
    Sprite lastSprite;

    void Awake()
    {
        source = GetComponent<SpriteRenderer>();

        GameObject go = new GameObject("Outline", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * scale;
        outline = go.GetComponent<SpriteRenderer>();
        outline.color = outlineColor;
    }

    void LateUpdate()
    {
        if (source.sprite != lastSprite)
        {
            lastSprite = source.sprite;
            outline.sprite = lastSprite;
        }
        outline.enabled = source.enabled && lastSprite != null;
        outline.sortingLayerID = source.sortingLayerID;
        outline.sortingOrder = source.sortingOrder - 1;
        outline.flipX = source.flipX;
        outline.flipY = source.flipY;
    }
}
