using System.Collections.Generic;
using UnityEngine;

// Boss volleys (BossController.FireVolley) and the player's staff/thrown items (PlayerController.
// LaunchProjectile) both spawn short-lived Projectile GameObjects, several times a second during
// an intense fight - continuous Instantiate/Destroy churn right when frame-time consistency
// matters most (2026-09-16 cleanup, found by a full-codebase review). Pooled instead: Release()
// deactivates and requeues a spent projectile rather than destroying it, and Get() reuses one
// before ever allocating a new GameObject.
public static class ProjectilePool
{
    static readonly Queue<GameObject> pool = new Queue<GameObject>();

    public static GameObject Get()
    {
        while (pool.Count > 0)
        {
            GameObject go = pool.Dequeue();
            // A pooled-but-inactive projectile can still be destroyed out from under the pool (a
            // scene reload, or anything else that wipes the hierarchy) - Unity's overridden null
            // check catches that, so this just skips it and tries the next one instead of handing
            // back a dead reference.
            if (go != null)
            {
                go.SetActive(true);
                return go;
            }
        }

        return new GameObject("Projectile", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(Projectile));
    }

    public static void Release(GameObject go)
    {
        if (go == null) return;
        Projectile projectile = go.GetComponent<Projectile>();
        if (projectile != null) projectile.ResetOptionalFields();
        go.SetActive(false);
        pool.Enqueue(go);
    }
}
