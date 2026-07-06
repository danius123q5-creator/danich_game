using UnityEngine;

/// <summary>Stationary flamethrower (ОГНЕМЁТ) — sprays a short cone of fire: HUGE damage but very
/// short range. Anything that gets close melts; it does nothing at distance. Free-running like a
/// turret (no ammo/upkeep). Rotates to face the nearest zombie and torches the cone in front.</summary>
public class Flamethrower : Buildable
{
    public override bool IsTrap => false;

    float range = 8f;     // short reach
    float dps = 90f;      // damage/sec to everything in the cone
    const float Tick = 0.1f;
    float nextTick, nextFx;

    protected override void Awake()
    {
        BuildCost = 220;
        MaxLevel = 3;
        UpgradeCost = 160;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 360f; range = 8f;   dps = 90f;  break;
            case 2: MaxHealth = 460f; range = 9.5f; dps = 150f; break;
            default: MaxHealth = 560f; range = 11f; dps = 220f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        Vector3 origin = transform.position + Vector3.up * 1.2f;

        // Aim at the nearest zombie (rotate the whole rig — the nozzle points where it faces).
        Zombie target = Nearest();
        if (target != null)
        {
            Vector3 dir = target.transform.position - origin; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), 8f * Time.deltaTime);
        }

        // Torch everything in the forward cone within range.
        if (Time.time >= nextTick)
        {
            nextTick = Time.time + Tick;
            float rSq = range * range;
            Vector3 fwd = transform.forward;
            foreach (var z in Zombie.All)
            {
                if (z == null || z.IsPuppet) continue;
                if (GameRoot.IsZvZ && z.team == Team) continue;
                Vector3 to = z.transform.position - origin; to.y = 0f;
                if (to.sqrMagnitude > rSq) continue;
                if (Vector3.Dot(fwd, to.normalized) < 0.35f) continue; // ~70° cone in front
                z.TakeDamage(dps * Tick);
            }
        }

        // Flame plume when there's something to burn.
        if (target != null && Time.time >= nextFx)
        {
            nextFx = Time.time + 0.05f;
            Vector3 tip = origin + transform.forward * (range * 0.55f);
            Effects.Burst(tip, new Color(1f, 0.5f, 0.15f), 4);
            Effects.Burst(origin + transform.forward * (range * 0.25f), new Color(1f, 0.75f, 0.25f), 2);
        }
    }

    Zombie Nearest()
    {
        Zombie best = null; float bestSq = range * range * 1.5f;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            float d = (z.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        return best;
    }
}
