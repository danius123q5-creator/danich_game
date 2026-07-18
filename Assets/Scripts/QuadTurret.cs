using UnityEngine;

/// <summary>КВАДРО-ТУРЕЛЬ — a heavy 4-barrel auto-turret. A beefed-up Sentry: longer range, much
/// higher damage and HP, and it fires a 4-round BURST each volley, spreading shots across up to 4
/// different zombies (so it shreds crowds, not just one target). From lvl 2 it also lobs area
/// rockets on a short cooldown. Works on its own — no ammo, no oil, no metal.</summary>
public class QuadTurret : Buildable
{
    float range = 30f;
    float fireRate = 0.12f;
    float damage = 60f;
    float rocketDmg = 160f;
    float rocketCd = 2.4f;

    float nextShot, nextScan, nextRocket;
    readonly Zombie[] targets = new Zombie[4]; // up to 4 distinct targets, one per barrel

    protected override void Awake()
    {
        BuildCost = 380;
        MaxLevel = 3;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 420f; range = 30f; fireRate = 0.12f; damage = 60f;  rocketDmg = 0f;   rocketCd = 99f;  break;
            case 2: MaxHealth = 520f; range = 34f; fireRate = 0.09f; damage = 95f;  rocketDmg = 160f; rocketCd = 2.6f; break;
            default:MaxHealth = 640f; range = 40f; fireRate = 0.06f; damage = 150f; rocketDmg = 240f; rocketCd = 1.8f; break;
        }
        damage *= ModRuntime.TurretDmgMult;
        rocketDmg *= ModRuntime.TurretDmgMult;
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time >= nextScan) { nextScan = Time.time + 0.2f; ScanTargets(); }

        Zombie primary = targets[0];
        if (primary != null)
        {
            Vector3 to = primary.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
        }

        if (Time.time >= nextShot)
        {
            nextShot = Time.time + fireRate;
            int fired = 0;
            for (int i = 0; i < 4; i++)
            {
                Zombie z = targets[i] != null ? targets[i] : primary; // extra barrels dogpile the main target
                if (z == null) break;
                FireAt(z, i); fired++;
            }
            if (fired > 0) Effects.TurretShot(transform.position + Vector3.up * 1.1f);
        }

        if (Level >= 2 && primary != null && Time.time >= nextRocket)
        {
            nextRocket = Time.time + rocketCd;
            RocketAt(primary);
        }
    }

    // Pick up to 4 distinct nearest zombies in range (with line of sight).
    void ScanTargets()
    {
        for (int i = 0; i < 4; i++) targets[i] = null;
        Vector3 c = transform.position + Vector3.up * 1.1f;
        float rSq = range * range;
        foreach (var z in Zombie.All)
        {
            if (z == null) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            float d = (z.transform.position - c).sqrMagnitude;
            if (d >= rSq || !HasLineOfSight(c, z)) continue;
            // insertion into the 4-slot nearest list
            for (int i = 0; i < 4; i++)
            {
                if (targets[i] == null) { targets[i] = z; break; }
                float dd = (targets[i].transform.position - c).sqrMagnitude;
                if (d < dd)
                {
                    for (int k = 3; k > i; k--) targets[k] = targets[k - 1];
                    targets[i] = z; break;
                }
            }
        }
    }

    static readonly RaycastHit[] _losHits = new RaycastHit[32];
    bool HasLineOfSight(Vector3 from, Zombie z)
    {
        Vector3 to = z.transform.position + Vector3.up * 0.4f;
        Vector3 dir = to - from;
        int n = Physics.RaycastNonAlloc(from, dir.normalized, _losHits, dir.magnitude);
        float bestD = float.MaxValue; Collider best = null;
        for (int i = 0; i < n; i++)
        {
            if (_losHits[i].collider.GetComponentInParent<QuadTurret>() == this) continue;
            if (_losHits[i].distance < bestD) { bestD = _losHits[i].distance; best = _losHits[i].collider; }
        }
        if (best == null) return true;
        return best.GetComponentInParent<Zombie>() != null;
    }

    void FireAt(Zombie z, int barrel)
    {
        if (z == null) return;
        // barrels sit at the 4 corners of the head — tracer starts from the matching corner
        float sx = (barrel == 0 || barrel == 2) ? -0.28f : 0.28f;
        float sy = (barrel < 2) ? 1.25f : 0.95f;
        Vector3 start = transform.position + transform.right * sx + Vector3.up * sy + transform.forward * 0.55f;
        Effects.Tracer(start, z.transform.position + Vector3.up * 0.4f);
        z.TakeDamage(damage);
    }

    void RocketAt(Zombie z)
    {
        if (rocketDmg <= 0f) return;
        Vector3 loc = z.transform.position;
        Effects.Explosion(loc + Vector3.up * 0.5f);
        foreach (var t in Zombie.All)
            if (t != null && (t.transform.position - loc).sqrMagnitude < 6.25f) t.TakeDamage(rocketDmg); // 2.5 m AoE
    }
}
