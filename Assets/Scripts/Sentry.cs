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

    // Hardcore: turrets run on ammo (metal loaded into their reserve via E). Empty = silent
    // until you refill. Normal mode keeps infinite ammo (ReserveMax 0 → UsesReserve false).
    public override int ReserveMax => GameRoot.Hardcore ? 300 : 0;
    bool HasAmmo => !UsesReserve || Reserve > 0;
    void UseAmmo(int n) { if (UsesReserve) Reserve = Mathf.Max(0, Reserve - n); }

    protected override void Awake()
    {
        BuildCost = 130;
        MaxLevel = 3;
        base.Awake();
        if (GameRoot.Hardcore) Reserve = 150; // start with some ammo (LoadState overrides for saves)
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

        if (Time.time >= nextShot && HasAmmo) // hardcore: silent when out of ammo
        {
            nextShot = Time.time + fireRate;
            int fired = 0;
            for (int i = 0; i < numShots && HasAmmo; i++) { FireAt(target); UseAmmo(1); fired++; }
            if (fired > 0) Effects.TurretShot(transform.position + Vector3.up * 0.6f); // one shot sound per volley
        }

        if (Level >= 2 && Time.time >= nextRocket && (!UsesReserve || Reserve >= 6))
        {
            nextRocket = Time.time + 3f;
            RocketAt(target);
            UseAmmo(6);
        }
    }

    Zombie FindTarget()
    {
        Zombie best = null;
        float bestSq = range * range;
        Vector3 c = transform.position + Vector3.up * 0.6f;
        foreach (var z in Zombie.All)
        {
            if (GameRoot.IsZvZ && z.team == Team) continue; // ZvZ: don't shoot your own side's horde
            float d = (z.transform.position - c).sqrMagnitude;
            if (d < bestSq && HasLineOfSight(c, z)) { best = z; bestSq = d; }
        }
        return best;
    }

    // Reused across all sentries' line-of-sight checks (no per-call array allocation).
    static readonly RaycastHit[] _losHits = new RaycastHit[32];

    // True only if nothing solid (wall/terrain/building) is between the sentry and the zombie.
    bool HasLineOfSight(Vector3 from, Zombie z)
    {
        Vector3 to = z.transform.position + Vector3.up * 0.4f;
        Vector3 dir = to - from;
        int n = Physics.RaycastNonAlloc(from, dir.normalized, _losHits, dir.magnitude);

        // Find the NEAREST hit that isn't our own collider (single pass, no sort).
        float bestD = float.MaxValue; Collider best = null;
        for (int i = 0; i < n; i++)
        {
            if (_losHits[i].collider.GetComponentInParent<Sentry>() == this) continue; // ignore ourselves
            if (_losHits[i].distance < bestD) { bestD = _losHits[i].distance; best = _losHits[i].collider; }
        }
        if (best == null) return true;                          // clear line of sight
        return best.GetComponentInParent<Zombie>() != null;     // nearest blocker must be a zombie
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
        foreach (var t in Zombie.All)
        {
            if ((t.transform.position - loc).sqrMagnitude < 4f) t.TakeDamage(40f); // 2 m AoE
        }
    }
}
