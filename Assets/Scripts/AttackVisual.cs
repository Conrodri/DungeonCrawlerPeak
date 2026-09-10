using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class AttackVisual : MonoBehaviour
{
    public float lifetime = 0.12f;

    SpriteRenderer sr;
    float startAlpha;
    float timer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        startAlpha = sr.color.a;
    }

    void Update()
    {
        timer += Time.deltaTime;
        Color c = sr.color;
        c.a = Mathf.Lerp(startAlpha, 0f, timer / lifetime);
        sr.color = c;

        if (timer >= lifetime) Destroy(gameObject);
    }
}
