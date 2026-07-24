using UnityEngine;

/// <summary>Special weapon — Tesla Coil. Once funded it arcs lightning to every zombie
/// within range several times a second, vaporising tight packs. Levels 1-3 add range,
/// targets, fire rate and enormous damage. Each arc costs a little metal.</summary>
public class TeslaCoil : Buildable
{
    public override int FundingRequired => 0;   // oil-only super-weapon (no metal)
    public override int OilRequired => 430;
    public override bool ReserveIsOil => true;
    public override int ReserveMax => 400;      // ammo reserve, paid in oil

    float range = 16f;
    float rate = 0.25f;     // seconds between volleys
    float damage = 120f;
    int maxTargets = 5;     // arcs per volley
    int zapCost = 6;        // metal per arc
    float next;

    protected override void Awake()
    {
        BuildCost = 200;
        MaxLevel = 3;
        UpgradeCost = 450;
        BuildTime = 3f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 450f; damage = 120f; range = 16f; rate = 0.25f; maxTargets = 5;  zapCost = 6; break;
            case 2: MaxHealth = 560f; damage = 220f; range = 19f; rate = 0.20f; maxTargets = 7;  zapCost = 7; break;
            default: MaxHealth = 700f; damage = 380f; range = 22f; rate = 0.15f; maxTargets = 10; zapCost = 8; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        next = Time.time + rate; // throttle the scan even when idle

        Vector3 c = transform.position + Vector3.up * 1.6f;
        float rSq = range * range;
        int zapped = 0;
        foreach (var z in Zombie.All)
        {
            if ((z.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!SpendMetal(zapCost)) break; // out of metal — stop arcing
            Effects.Tracer(c, z.transform.position + Vector3.up * 1f);
            z.TakeDamage(damage, Lang.T("тесла", "tesla"));
            if (++zapped >= maxTargets) break;
        }
        if (zapped > 0) Effects.Zap(c);
    }
}
