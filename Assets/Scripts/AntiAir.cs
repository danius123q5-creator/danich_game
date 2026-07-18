using UnityEngine;

/// <summary>Anti-air emplacement (ЗЕНИТКА). Short-range point defence: intercepts grenades lobbed
/// by grenadier zombies, birds carrying zombies, AND enemy bomber raiders — each target that enters
/// range gets exactly one shot at a 50% chance to be knocked down. Does not target ground zombies
/// or your own airstrike plane. (The ПЗРК/РЗК is the reliable long-range answer to aircraft.)</summary>
public class AntiAir : Buildable
{
    // 3.1.1: the AA gun was USELESS vs planes — range 25 never reached bombers cruising at ~56 m
    // altitude. Now it has ЗРК-class reach and a reliable intercept, so it actually protects the base.
    const float Range = 120f;
    const float InterceptChance = 0.9f;

    // Unique, stable per-instance id used to let each AA roll once per bird.
    // Replaces the deprecated GetInstanceID() (made a hard error in Unity 6000.5).
    static int _nextId = 1;
    int _myId;

    // Autocannon: keep spitting tracers at whatever's in range on a fast cadence, so the gun
    // is visibly FIRING the whole time a target is overhead — not one silent shot per target.
    float fireTimer;
    const float FireInterval = 0.1f;

    protected override void Awake()
    {
        BuildCost = 120;
        MaxLevel = 1;
        BuildTime = 1.5f;
        _myId = _nextId++;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 320f;   // 3.1.1: tougher (was 220) — it's now the main anti-air
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
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 8f * Time.deltaTime);

            // Autocannon: hose a steady stream of tracers at the tracked target while it's in range.
            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                fireTimer = FireInterval;
                Effects.Tracer(muzzle, aim.position);
                Effects.TurretShot(muzzle);
            }
        }

        foreach (var g in Object.FindObjectsByType<Grenade>(FindObjectsSortMode.None))
        {
            if ((g.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!g.ClaimForIntercept()) continue; // only the first AA in range rolls for this grenade
            if (Random.value < InterceptChance) g.ShootDown();
        }

        // Birds carrying zombies — EACH AA gets its own one-time 50% roll, so multiple
        // emplacements on the base stack their chances (1 - 0.5^N to down the bird).
        int myId = _myId;
        foreach (var bird in Object.FindObjectsByType<Bird>(FindObjectsSortMode.None))
        {
            if ((bird.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!bird.TryEngage(myId)) continue;
            if (Random.value < InterceptChance) bird.ShootDown();
        }

        // ENEMY bomber raids — short-range point defence also barrages the raiders. One 50% roll per
        // gun per plane (the ПЗРК/РЗК stays the reliable long-range answer). Own airstrike untouched.
        foreach (var bmb in Bomber.All)
        {
            if (bmb == null || !bmb.enemy) continue;
            if ((bmb.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!bmb.TryAaEngage(myId)) continue;
            if (Random.value < InterceptChance) bmb.CrashDown();
        }

        // ENEMY kamikaze drones — one 50% roll per gun per drone (several AA stack the odds).
        foreach (var dr in EnemyDrone.All)
        {
            if (dr == null) continue;
            if ((dr.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (!dr.TryEngage(myId)) continue;
            if (Random.value < InterceptChance) dr.ShootDown();
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
        foreach (var bmb in Bomber.All)
        {
            if (bmb == null || !bmb.enemy) continue;
            float d = (bmb.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = bmb.transform; }
        }
        foreach (var dr in EnemyDrone.All)
        {
            if (dr == null) continue;
            float d = (dr.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = dr.transform; }
        }
        return best;
    }
}
