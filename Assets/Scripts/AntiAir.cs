using UnityEngine;

/// <summary>Anti-air emplacement (ПВО). Intercepts grenades lobbed by grenadier zombies
/// AND birds carrying zombies: each one that enters range gets exactly one shot — a 50%
/// chance to be knocked out of the sky. Does not target zombies on the ground.</summary>
public class AntiAir : Buildable
{
    const float Range = 40f;
    const float InterceptChance = 0.5f;

    protected override void Awake()
    {
        BuildCost = 120;
        MaxLevel = 1;
        BuildTime = 1.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 220f;
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        float rSq = Range * Range;
        Vector3 muzzle = transform.position + Vector3.up * 1.2f;

        // Turn to face the nearest incoming threat (grenade or bird), like the sentry/artillery.
        Transform aim = NearestAirThreat(rSq);
        if (aim != null)
        {
            Vector3 to = aim.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 5f * Time.deltaTime);
        }

        foreach (var g in Object.FindObjectsByType<Grenade>(FindObjectsSortMode.None))
        {
            if ((g.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!g.ClaimForIntercept()) continue; // only the first AA in range rolls for this grenade
            Effects.Tracer(muzzle, g.transform.position);
            Effects.TurretShot(muzzle);
            if (Random.value < InterceptChance) g.ShootDown();
        }

        // Birds carrying zombies — EACH AA gets its own one-time 50% roll, so multiple
        // emplacements on the base stack their chances (1 - 0.5^N to down the bird).
        int myId = GetInstanceID();
        foreach (var bird in Object.FindObjectsByType<Bird>(FindObjectsSortMode.None))
        {
            if ((bird.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!bird.TryEngage(myId)) continue;
            Effects.Tracer(muzzle, bird.transform.position);
            Effects.TurretShot(muzzle);
            if (Random.value < InterceptChance) bird.ShootDown();
        }
    }

    // Nearest grenade or bird within range — used only to point the gun at it.
    Transform NearestAirThreat(float rSq)
    {
        Transform best = null;
        float bestSq = rSq;
        foreach (var g in Object.FindObjectsByType<Grenade>(FindObjectsSortMode.None))
        {
            float d = (g.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = g.transform; }
        }
        foreach (var b in Object.FindObjectsByType<Bird>(FindObjectsSortMode.None))
        {
            float d = (b.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = b.transform; }
        }
        return best;
    }
}
