using UnityEngine;

// Ring outline shown for the full duration of an enemy's attack windup, sized to the attack's real
// range - 2026-09-23 request: "affiche les zones de degats lors d'un lancement d'un coup ennemi,
// pour le coup de griffe par exemple, je ne connais pas sa range". Reuses DungeonGenerator's shared
// outlineRingSprite (the same white ring the minimap already tints per-use) with a plain
// SpriteRenderer instead of a new LineRenderer/shader - this codebase has zero LineRenderer
// precedent, while SpriteRenderer-based cosmetic visuals (AttackVisual/SwordSwingVisual) are already
// the established pattern. Parented to the attacker (rather than a fixed world position) since both
// Claw and Dash root the enemy in place for their whole cast anyway.
[RequireComponent(typeof(SpriteRenderer))]
public class AttackRangeTelegraph : MonoBehaviour
{
    float lifetime;
    float timer;

    public static void Spawn(Sprite ringSprite, Transform attacker, float range, float duration, Color color)
    {
        if (ringSprite == null || attacker == null || duration <= 0f) return;

        GameObject go = new GameObject("AttackRangeTelegraph", typeof(SpriteRenderer), typeof(AttackRangeTelegraph));
        go.transform.SetParent(attacker);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * (range * 2f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = ringSprite;
        renderer.color = color;
        renderer.sortingOrder = 1;

        go.GetComponent<AttackRangeTelegraph>().lifetime = duration;
    }

    // World-position counterpart to Spawn above - NOT parented to an attacker, for an AOE landing
    // somewhere else entirely (Skinwalker's Pounce lands where the target WAS when the leap started,
    // not on the attacker's own body) - same boss-style "the ring IS the hit zone" tell already used
    // by BossController's own AOE attacks.
    public static void SpawnAt(Sprite ringSprite, Vector2 worldPosition, float range, float duration, Color color)
    {
        if (ringSprite == null || duration <= 0f) return;

        GameObject go = new GameObject("AttackRangeTelegraph", typeof(SpriteRenderer), typeof(AttackRangeTelegraph));
        go.transform.position = worldPosition;
        go.transform.localScale = Vector3.one * (range * 2f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = ringSprite;
        renderer.color = color;
        renderer.sortingOrder = 1;

        go.GetComponent<AttackRangeTelegraph>().lifetime = duration;
    }

    // Directional counterpart to Spawn above, for a frontal attack (Claw) instead of an
    // omnidirectional one - 2026-09-23 follow-up: "pour le coup de griffe, un petit cone en face du
    // monstre serait plus logique". coneSprite is pivoted at its own point (DungeonGenerator.
    // CreateConeSprite), not its center like the ring, so it scales by range (not range * 2f) and
    // rotates to face the attack direction instead of staying a fixed circle.
    public static void SpawnCone(Sprite coneSprite, Transform attacker, Vector2 facingDirection, float range, float duration, Color color)
    {
        if (coneSprite == null || attacker == null || duration <= 0f) return;

        GameObject go = new GameObject("AttackRangeTelegraph", typeof(SpriteRenderer), typeof(AttackRangeTelegraph));
        go.transform.SetParent(attacker);
        go.transform.localPosition = Vector3.zero;
        go.transform.up = facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Vector2.up;
        go.transform.localScale = Vector3.one * range;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = coneSprite;
        renderer.color = color;
        renderer.sortingOrder = 1;

        go.GetComponent<AttackRangeTelegraph>().lifetime = duration;
    }

    // Corridor telegraph for a straight-line lunge (Dash) - 2026-09-23 follow-up: "pour le sort
    // dash, tu affiche un enorme carre alors qu'un rectangle entre le monstre et le crawler est plus
    // logique". rectSprite is pivoted at its own base like coneSprite above, but scaled
    // NON-uniformly (fixed corridor width, length = the dash's actual range) instead of by a single
    // range value, since a lunge's threat is a narrow line, not something that widens with reach.
    public static void SpawnRect(Sprite rectSprite, Transform attacker, Vector2 facingDirection, float length, float width, float duration, Color color)
    {
        if (rectSprite == null || attacker == null || duration <= 0f) return;

        GameObject go = new GameObject("AttackRangeTelegraph", typeof(SpriteRenderer), typeof(AttackRangeTelegraph));
        go.transform.SetParent(attacker);
        go.transform.localPosition = Vector3.zero;
        go.transform.up = facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Vector2.up;
        go.transform.localScale = new Vector3(width, length, 1f);

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = rectSprite;
        renderer.color = color;
        renderer.sortingOrder = 1;

        go.GetComponent<AttackRangeTelegraph>().lifetime = duration;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifetime) Destroy(gameObject);
    }
}
