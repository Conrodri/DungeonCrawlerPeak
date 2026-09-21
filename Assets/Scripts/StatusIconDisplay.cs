using System.Collections.Generic;
using UnityEngine;

// Shared floating icon strip above any entity - player or monster - for buffs/debuffs that come
// and go over time (e.g. PlayerController's Hole movement debuff, EnemyController's Elite
// modifier badge). Several active icons line up side by side above the head instead of stacking
// on top of each other. Not used by DungeonGenerator's NPC "!" badge, which is a static role
// marker rather than a toggled effect.
public class StatusIconDisplay : MonoBehaviour
{
    public float height = 0.9f;
    public float iconScale = 0.5f;
    public float spacing = 0.45f;

    class ActiveIcon
    {
        public GameObject go;
        public float expireTime; // <= 0 means "shown until HideIcon(key) is called explicitly"
    }

    readonly Dictionary<string, ActiveIcon> icons = new Dictionary<string, ActiveIcon>();

    // `tint` only applies the first time this key is shown (an icon's color doesn't change across
    // repeated ShowIcon calls, e.g. a refreshed duration) - null keeps the sprite's own color, same
    // as every call site before 2026-09-21 (PlayerController/EnemyController's broken-limb debuffs,
    // which reuse the same icon for leg/arm/wing and tell them apart by color instead).
    public void ShowIcon(string key, Sprite sprite, float duration = -1f, Color? tint = null)
    {
        if (!icons.TryGetValue(key, out ActiveIcon active))
        {
            GameObject go = new GameObject("StatusIcon_" + key, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * iconScale;

            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 2;
            if (tint.HasValue) renderer.color = tint.Value;

            active = new ActiveIcon { go = go };
            icons[key] = active;
            Reflow();
        }
        active.expireTime = duration > 0f ? Time.time + duration : -1f;
    }

    public void HideIcon(string key)
    {
        if (!icons.TryGetValue(key, out ActiveIcon active)) return;
        if (active.go != null) Destroy(active.go);
        icons.Remove(key);
        Reflow();
    }

    void Update()
    {
        if (icons.Count == 0) return;

        List<string> expired = null;
        foreach (KeyValuePair<string, ActiveIcon> kv in icons)
        {
            if (kv.Value.expireTime > 0f && Time.time >= kv.Value.expireTime)
                (expired ??= new List<string>()).Add(kv.Key);
        }
        if (expired != null) foreach (string key in expired) HideIcon(key);
    }

    // Centers the row of active icons above the entity, left to right in insertion order.
    void Reflow()
    {
        float totalWidth = (icons.Count - 1) * spacing;
        int i = 0;
        foreach (ActiveIcon active in icons.Values)
        {
            active.go.transform.localPosition = new Vector3(-totalWidth / 2f + i * spacing, height, 0f);
            i++;
        }
    }
}
