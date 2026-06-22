using UnityEngine;
using System.Collections.Generic;

/// <summary>Special weapon — Air Strike Beacon. Once funded it periodically calls a
/// carpet-bombing run on the densest cluster of zombies: a spread of devastating blasts.
/// Levels 1-3 add bombs, radius, rate and colossal damage. Each run burns metal.</summary>
public class AirStrike : Buildable
{
    public override int FundingRequired => 3000;
    public override int ReserveMax => 1800;

    const float ScanRange = 90f;
    int strikeCost = 120;   // metal per bombing run
    float interval = 5f;    // seconds between runs
    float blastRadius = 7f;
    float blastDamage = 300f;
    int bombs = 6;
    float next;

    protected override void Awake()
    {
        BuildCost = 250;
        MaxLevel = 3;
        UpgradeCost = 500;  // metal per level once it's funded & online
        BuildTime = 3f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 400f; blastDamage = 300f; bombs = 6;  blastRadius = 7f; interval = 5.0f; strikeCost = 120; break;
            case 2: MaxHealth = 520f; blastDamage = 550f; bombs = 8;  blastRadius = 8f; interval = 4.2f; strikeCost = 140; break;
            default: MaxHealth = 680f; blastDamage = 950f; bombs = 10; blastRadius = 9f; interval = 3.5f; strikeCost = 160; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;

        // Aim at the densest pocket: the zombie with the most neighbours in blast range.
        var zombies = Zombie.All;
        if (zombies.Count == 0) return;
        Vector3 best = Vector3.zero;
        int bestCount = 0;
        float rSq = blastRadius * blastRadius;
        foreach (var z in zombies)
        {
            Vector3 p = z.transform.position;
            if ((p - transform.position).sqrMagnitude > ScanRange * ScanRange) continue;
            int c = 0;
            foreach (var o in zombies)
                if ((o.transform.position - p).sqrMagnitude < rSq) c++;
            if (c > bestCount) { bestCount = c; best = p; }
        }
        if (bestCount <= 0) return;
        if (!SpendMetal(strikeCost)) return; // can't afford — wait for more metal

        next = Time.time + interval;
        CallStrike(best);
    }

    void CallStrike(Vector3 center)
    {
        // Lay out the bomb pattern (centre hit + a ring spread across the cluster).
        var points = new List<Vector3>(bombs);
        for (int i = 0; i < bombs; i++)
        {
            float ang = i * (Mathf.PI * 2f / bombs);
            float r = i == 0 ? 0f : Random.Range(1.5f, blastRadius);
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            p.y = GameBootstrap.Hill(p.x, p.z);
            points.Add(p);
        }

        // Capture damage/radius so each bomb deals its blast AT the moment it lands (re-scanning
        // zombies then, so moved/spawned ones are caught and the boom syncs with the damage).
        float bd = blastDamage, br = blastRadius, rSq = blastRadius * blastRadius;
        Effects.AirStrikeRun(center, points, br, p =>
        {
            var zs = Zombie.All;
            foreach (var z in zs)
                if (z != null && (z.transform.position - p).sqrMagnitude < rSq)
                    z.TakeDamage(bd);
        });
    }
}
