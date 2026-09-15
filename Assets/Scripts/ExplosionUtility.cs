using UnityEngine;

// Shared by Bomb and ExplosiveBarrel so both trigger the exact same area effect - damage, blowing
// open doors/secret walls, and chaining into nearby barrels/fuel puddles.
public static class ExplosionUtility
{
    public static void Explode(Vector2 position, float radius, int damage, int attackerForce, Sprite explosionSprite)
    {
        // Area damage hits everything with a Health component in range, including the player -
        // standing in your own blast is a real risk, same as in Isaac.
        Collider2D[] hits = Physics2D.OverlapCircleAll(position, radius);
        foreach (Collider2D hit in hits)
        {
            Health health = hit.GetComponent<Health>();
            if (health != null) health.TakeDamage(damage);

            // Standing in the blast can set any equipped Tissu piece alight (see PlayerEquipment.
            // TryIgnite) - a real risk of the same "stand in your own explosion" danger as above.
            PlayerEquipment equipment = hit.GetComponent<PlayerEquipment>();
            if (equipment != null) equipment.TryIgnite();

            DestructibleObject destructible = hit.GetComponent<DestructibleObject>();
            if (destructible != null) destructible.TryDamage(damage, attackerForce);

            // Bombing a locked door blows it open for good: RoomController skips a destroyed
            // blocker forever after, even across room resets.
            DoorBlocker blocker = hit.GetComponent<DoorBlocker>();
            if (blocker != null) Object.Destroy(blocker.gameObject);

            // Same permanent-open mechanic as a locked door's DoorBlocker, for a secret room's wall.
            SecretWallBlocker secretWall = hit.GetComponent<SecretWallBlocker>();
            if (secretWall != null) Object.Destroy(secretWall.gameObject);

            // Chain reaction: a nearby barrel detonates too, a nearby fuel puddle catches fire.
            ExplosiveBarrel barrel = hit.GetComponent<ExplosiveBarrel>();
            if (barrel != null) barrel.Detonate();

            FuelPuddle puddle = hit.GetComponent<FuelPuddle>();
            if (puddle != null) puddle.Ignite();
        }

        if (explosionSprite != null)
        {
            GameObject fx = new GameObject("Explosion", typeof(SpriteRenderer), typeof(AttackVisual));
            fx.transform.position = position;
            fx.transform.localScale = Vector3.one * (radius * 2f);
            SpriteRenderer renderer = fx.GetComponent<SpriteRenderer>();
            renderer.sprite = explosionSprite;
            renderer.sortingOrder = 1;
            fx.GetComponent<AttackVisual>().lifetime = 0.2f;
        }
    }
}
