using UnityEngine;

/// <summary>Auto-targeting turret. Ported from sent_engi_sentry.lua. Levels 1-3:
/// more HP/damage/rate; lvl3 double-shots and lobs an AoE "rocket".</summary>
public class Sentry : Buildable
{
    public const float BaseRange = 22f;
    float range = BaseRange;
    float fireRate = 0.15f;
    float damage = 10f;
    int numShots = 1;

    float nextShot;
    float nextScan;
    float nextRocket;
    Zombie target;

    protected override void Awake()
    {
        BuildCost = 130;
        MaxLevel = 3;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 150f; fireRate = 0.15f; damage = 14f; numShots = 1; break;
            case 2: MaxHealth = 180f; fireRate = 0.11f; damage = 24f; numShots = 1; break;
            default: MaxHealth = 216f; fireRate = 0.08f; damage = 38f; numShots = 2; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.25f;
            target = FindTarget();
        }
        if (target == null) return;

        Vector3 to = target.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);

        if (Time.time >= nextShot)
        {
            nextShot = Time.time + fireRate;
            for (int i = 0; i < numShots; i++) FireAt(target);
            Effects.TurretShot(transform.position + Vector3.up * 0.6f); // one shot sound per volley
        }

        if (Level >= 2 && Time.time >= nextRocket)
        {
            nextRocket = Time.time + 3f;
            RocketAt(target);
        }
    }

    Zombie FindTarget()
    {
        Zombie best = null;
        float bestSq = range * range;
        Vector3 c = transform.position + Vector3.up * 0.6f;
        foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            float d = (z.transform.position - c).sqrMagnitude;
            if (d < bestSq && HasLineOfSight(c, z)) { best = z; bestSq = d; }
        }
        return best;
    }

    // True only if nothing solid (wall/terrain/building) is between the sentry and the zombie.
    bool HasLineOfSight(Vector3 from, Zombie z)
    {
        Vector3 to = z.transform.position + Vector3.up * 0.4f;
        Vector3 dir = to - from;
        var hits = Physics.RaycastAll(from, dir.normalized, dir.magnitude);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<Sentry>() == this) continue; // ignore our own collider
            return h.collider.GetComponentInParent<Zombie>() != null;        // first real hit must be a zombie
        }
        return true;
    }

    void FireAt(Zombie z)
    {
        if (z == null) return;
        Vector3 start = transform.position + Vector3.up * 0.6f;
        Effects.Tracer(start, z.transform.position + Vector3.up * 0.4f); // visible shot
        z.TakeDamage(damage);
    }

    void RocketAt(Zombie z)
    {
        Vector3 loc = z.transform.position;
        Effects.Explosion(loc + Vector3.up * 0.5f); // visible rocket blast
        foreach (var t in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            if ((t.transform.position - loc).sqrMagnitude < 4f) t.TakeDamage(40f); // 2 m AoE
        }
    }
}
