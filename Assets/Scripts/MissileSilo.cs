using UnityEngine;

/// <summary>Defense building — Ballistic Missile Silo (РАКЕТ. ШАХТА). Works like a turret:
/// once built it watches for a CROWD (3+ zombies bunched together) and launches a rocket that
/// flies into the densest cluster and detonates for heavy splash damage. No metal upkeep — it
/// just holds fire until enough zombies group up. Pricey to build (550). Levels add damage,
/// blast radius and reload speed.</summary>
public class MissileSilo : Buildable
{
    public override bool IsTrap => false;

    // A powerful "super" defense that FREE-RUNS like a turret — no oil, no metal upkeep. It just
    // holds fire until a crowd forms, then launches a ballistic missile. Pricey to build.
    float reload = 5f;       // seconds between launches
    float blastR = 5.5f;     // explosion radius (everything inside dies)
    float range = 600f;      // LONG-RANGE weapon — reaches crowds far across the map
    int minCrowd = 3;        // only fire at a cluster of at least this many zombies
    float next;

    protected override void Awake()
    {
        BuildCost = 550;
        MaxLevel = 3;
        UpgradeCost = 350;
        BuildTime = 3f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 500f; blastR = 10f; reload = 5f; range = 600f; break;
            case 2: MaxHealth = 640f; blastR = 13f; reload = 4f; range = 1300f; break;
            default: MaxHealth = 800f; blastR = 16f; reload = 3f; range = 2000f; break;
        }
        Health = MaxHealth;
    }

    protected override void OnActivated() { next = Time.time + 1.5f; }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;

        int count;
        Zombie center = FindCrowd(blastR, out count);
        if (center == null || count < minCrowd) return; // hold fire until a crowd forms

        next = Time.time + reload;
        Vector3 from = transform.position + Vector3.up * 2.2f;        // launch from the silo mouth
        Vector3 to = center.transform.position;                       // ground at the crowd's heart
        BallisticMissile.Launch(from, to, blastR, center);            // claim this zombie & dive on the crowd
        Effects.TurretShot(from);
    }

    // The zombie with the most neighbours within 'radius' — i.e. the heart of the densest pack.
    Zombie FindCrowd(float radius, out int bestCount)
    {
        float rSq = radius * radius;
        float rangeSq = range * range;
        const float minRangeSq = 180f * 180f;   // 3.1.1: don't waste a long-range missile point-blank
        Vector3 silo = transform.position;
        Zombie best = null; bestCount = 0;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            if (BallisticMissile.IsReserved(z)) continue;                        // another missile already claimed it
            float dSilo = (z.transform.position - silo).sqrMagnitude;
            if (dSilo > rangeSq || dSilo < minRangeSq) continue;                 // only FAR crowds within range
            int c = 0;
            foreach (var w in Zombie.All)
            {
                if (w == null || w.IsPuppet) continue;
                if (GameRoot.IsZvZ && w.team == Team) continue;
                if ((w.transform.position - z.transform.position).sqrMagnitude <= rSq) c++;
            }
            if (c > bestCount) { bestCount = c; best = z; }
        }
        return best;
    }
}
