using UnityEngine;
using UnityEngine.UI;

// Fades an Image's alpha in and out between minAlpha and maxAlpha - a reusable attention-drawing
// pulse, not tied to any one use (currently the boss room's minimap glow).
[RequireComponent(typeof(Image))]
public class PulsingGlow : MonoBehaviour
{
    public float minAlpha = 0.25f;
    public float maxAlpha = 0.75f;
    public float speed = 2f;

    Image image;
    Color baseColor;

    void Awake()
    {
        image = GetComponent<Image>();
        baseColor = image.color;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
        float a = Mathf.Lerp(minAlpha, maxAlpha, t);
        image.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
    }
}
