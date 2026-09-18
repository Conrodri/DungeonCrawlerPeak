using UnityEngine;
using UnityEngine.UI;

// Floating red damage numbers above a target (2026-09-19 request: "affiches les degats en rouge
// au dessus de la cible (uniquement le punching ball)") - deliberately not wired onto any real
// enemy, just DungeonGenerator.BuildTrainingRoom's dummy, so ordinary combat stays uncluttered.
// Screen-space (repositioned from the target's world position every frame, same technique
// InteractPromptUI's tooltip-style text uses) rather than a world-space TextMesh, so it reads
// correctly regardless of camera angle/zoom and reuses the exact same Text/font path as every
// other piece of UI in the project.
public class DamageNumberDisplay : MonoBehaviour
{
    public Health target;

    int lastHealth;
    Canvas canvas;
    Font font;

    void Start()
    {
        if (target == null) target = GetComponent<Health>();
        lastHealth = target.currentHealth;
        target.OnHealthChanged += HandleHealthChanged;

        GameObject canvasGO = GameObject.Find("Canvas");
        canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : null;
        font = Font.CreateDynamicFontFromOSFont("Arial", 28);
    }

    void OnDestroy()
    {
        if (target != null) target.OnHealthChanged -= HandleHealthChanged;
    }

    void HandleHealthChanged(int current, int max)
    {
        int amount = lastHealth - current;
        lastHealth = current;
        if (amount > 0) Spawn(amount);
    }

    void Spawn(int amount)
    {
        if (canvas == null) return;

        GameObject go = new GameObject("DamageNumber", typeof(Text));
        go.transform.SetParent(canvas.transform, false);

        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.95f, 0.15f, 0.1f);
        text.raycastTarget = false;
        text.text = amount.ToString();
        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(120f, 40f);

        DamageNumberPopup popup = go.AddComponent<DamageNumberPopup>();
        popup.followTarget = transform;
    }
}

// Drives one spawned number's rise+fade+destroy - split out from DamageNumberDisplay so several
// popups from a fast combo can each run their own lifetime independently.
public class DamageNumberPopup : MonoBehaviour
{
    public Transform followTarget;
    public float worldHeight = 1.1f;
    public float riseSpeed = 40f; // screen pixels/second
    public float lifetime = 0.8f;

    RectTransform rect;
    Text text;
    Color startColor;
    float endTime;
    float risen;

    void Start()
    {
        rect = GetComponent<RectTransform>();
        text = GetComponent<Text>();
        startColor = text.color;
        endTime = Time.time + lifetime;
        UpdatePosition();
    }

    void Update()
    {
        if (followTarget == null || Camera.main == null || Time.time >= endTime)
        {
            Destroy(gameObject);
            return;
        }

        risen += riseSpeed * Time.deltaTime;
        UpdatePosition();

        float alpha = Mathf.Clamp01((endTime - Time.time) / lifetime);
        text.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
    }

    void UpdatePosition()
    {
        Vector3 worldPos = followTarget.position + new Vector3(0f, worldHeight, 0f);
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        rect.position = screenPos + new Vector3(0f, risen, 0f);
    }
}
