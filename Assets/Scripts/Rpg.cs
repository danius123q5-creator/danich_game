using UnityEngine;

/// <summary>Cheap rocket turret (РПГ). Auto-targets the nearest zombie and lobs an
/// explosive rocket with splash damage — great against clumps, but fragile and slow
/// to reload. Turns to face its target like the sentry/artillery.</summary>
public class Rpg : Buildable
{
    float range = 22f;
    float rate = 1.8f;       // seconds between rockets
    float blastRadius = 2.8f;
    float blastDamage = 40f;
    float next;
    float nextScan;
    Zombie target;

    protected override void Awake()
    {
        BuildCost = 40;
        MaxLevel = 3;
        UpgradeCost = 150;
        BuildTime = 2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            // 3.1.1: RPG hits much harder & faster with bigger splash (was 65/105/155 dmg).
            case 1: MaxHealth = 170f; rate = 1.5f; blastDamage = 130f; blastRadius = 3.4f; range = 26f; break;
            case 2: MaxHealth = 220f; rate = 1.1f; blastDamage = 210f; blastRadius = 4.0f; range = 30f; break;
            default: MaxHealth = 290f; rate = 0.85f; blastDamage = 320f; blastRadius = 4.6f; range = 34f; break;
        }
        blastDamage *= ModRuntime.RpgDmgMult; // 3.2: mod multiplier
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.25f;
            target = NearestInRange();
        }
        if (target == null) return;

        // Aim at the target (yaw), like the other turrets.
        Vector3 aim = target.transform.position - transform.position; aim.y = 0f;
        if (aim.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aim), 6f * Time.deltaTime);

        if (Time.time < next) return;
        next = Time.time + rate;

        // Launch a real rocket from the tube tip; it flies out and explodes on impact.
        Vector3 muzzle = transform.position + transform.forward * 0.9f + Vector3.up * 0.95f;
        Vector3 aimPt = target.transform.position + Vector3.up * 0.4f;
        Effects.GunShot(muzzle);
        Effects.Burst(muzzle, new Color(1f, 0.8f, 0.3f), 6); // launch flash
        Rocket.Launch(muzzle, aimPt, blastRadius, blastDamage);
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
