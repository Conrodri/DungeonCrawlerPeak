using UnityEngine;

// Cosmetic sweep for the semi-circular sword swing (see PlayerController.SwordSlash) - separate
// from the generic AttackVisual (a static sprite that just fades in place) because a swing actually
// needs to travel across its arc over `duration` instead of appearing once. Orbits `anchor` at half
// of `radius`, rotating from -halfArcDegrees to +halfArcDegrees around aimDirection, so a small
// placeholder sprite reads as a blade sweeping through the hit arc rather than a static icon.
[RequireComponent(typeof(SpriteRenderer))]
public class SwordSwingVisual : MonoBehaviour
{
    public float duration = 0.5f;
    public float radius = 1f;
    public float halfArcDegrees = 90f;
    public Vector2 aimDirection = Vector2.up;
    public Transform anchor;

    SpriteRenderer sr;
    float startAlpha;
    float timer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        startAlpha = sr.color.a;
        transform.localScale = Vector3.one * (radius * 0.9f);
    }

    void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(duration > 0f ? timer / duration : 1f);

        float angle = Mathf.Lerp(-halfArcDegrees, halfArcDegrees, t);
        Vector2 sweepDir = Quaternion.Euler(0f, 0f, angle) * aimDirection;
        Vector2 origin = anchor != null ? (Vector2)anchor.position : (Vector2)transform.position;
        transform.position = origin + sweepDir * (radius * 0.5f);
        transform.up = sweepDir;

        Color c = sr.color;
        c.a = Mathf.Lerp(startAlpha, 0f, t);
        sr.color = c;

        if (timer >= duration) Destroy(gameObject);
    }
}
