using UnityEngine;

/// <summary>Special weapon — Artillery Cannon. Once funded it lobs explosive shells at
/// zombies across the whole map, with a big blast radius and very long range. Each shell
/// costs a hefty chunk of metal.</summary>
public class Artillery : Buildable
{
    public override int FundingRequired => 0;   // oil-only super-weapon (no metal)
    public override int OilRequired => 520;
    public override bool ReserveIsOil => true;
    public override int ReserveMax => 450;      // ammo reserve, paid in oil

    float range = 70f;
    float rate = 1.4f;       // seconds between shells
    float blastRadius = 5f;
    float blastDamage = 400f;
    int shellCost = 50;      // metal per shell
    float next;
    float nextScan;
    Zombie target;

    protected override void Awake()
    {
        BuildCost = 250;
        MaxLevel = 3;
        UpgradeCost = 500;
        BuildTime = 3.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 500f; blastDamage = 400f;  blastRadius = 5.0f; rate = 1.4f;  range = 70f; shellCost = 50; break;
            case 2: MaxHealth = 640f; blastDamage = 700f;  blastRadius = 6.0f; rate = 1.1f;  range = 80f; shellCost = 60; break;
            default: MaxHealth = 820f; blastDamage = 1200f; blastRadius = 7.5f; rate = 0.85f; range = 90f; shellCost = 70; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        // Re-acquire the nearest zombie in range occasionally so the AoE lands in the thick of them.
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.3f;
            target = NearestInRange();
        }

        // Turn the cannon to face the target (yaw), like the sentry does.
        if (target != null)
        {
            Vector3 aim = target.transform.position - transform.position;
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aim), 4f * Time.deltaTime);
        }

        if (Time.time < next) return;
        if (target == null) return;
        next = Time.time + rate;
        if (!SpendMetal(shellCost)) return;

        Vector3 c = transform.position + Vector3.up * 1.4f;
        Vector3 loc = target.transform.position;
        Effects.CannonFire(c);                       // muzzle boom at the cannon
        Effects.Tracer(c, loc + Vector3.up * 0.4f);
        Effects.Explosion(loc + Vector3.up * 0.4f);  // shell impact

        float rSq = blastRadius * blastRadius;
        foreach (var z in Zombie.All)
            if ((z.transform.position - loc).sqrMagnitude < rSq) z.TakeDamage(blastDamage, Lang.T("артиллерия", "artillery"));
    }

    Zombie NearestInRange()
    {
        Zombie best = null;
        float bestSq = range * range;
        foreach (var z in Zombie.All)
        {
            float d = (z.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        return best;
    }
}
