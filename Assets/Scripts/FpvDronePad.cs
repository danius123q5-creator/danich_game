using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared target reservation for ALL drones (FPV + Big FPV / Герань). A zombie targeted by
/// an in-flight drone is CLAIMED, so other pads pick different zombies instead of dogpiling one —
/// same idea as the ballistic missile's reservation.</summary>
public static class DroneTargets
{
    static readonly HashSet<Zombie> claimed = new HashSet<Zombie>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => claimed.Clear();
    public static bool IsClaimed(Zombie z) => z != null && claimed.Contains(z);
    public static void Claim(Zombie z) { if (z != null) claimed.Add(z); }
    public static void Release(Zombie z) { if (z != null) claimed.Remove(z); }
}

/// <summary>Shared flight path for ALL drones/ФАУ (FPV, Big-FPV, V-1): a LEVEL cruise ~10 m over the
/// ground toward the target — NOT a high ballistic arc — then a short dive onto the target at the end.</summary>
public static class DroneFlight
{
    public const float CruiseAlt = 10f;

    public static Vector3 Path(Vector3 launch, Vector3 aim, float t01)
    {
        float x = Mathf.Lerp(launch.x, aim.x, t01);
        float z = Mathf.Lerp(launch.z, aim.z, t01);
        float cruiseY = GameBootstrap.Hill(x, z) + CruiseAlt; // terrain-following, ~10 m up
        float y;
        if (t01 < 0.12f)      y = Mathf.Lerp(launch.y, cruiseY, t01 / 0.12f);               // climb to cruise
        else if (t01 > 0.82f) y = Mathf.Lerp(cruiseY, aim.y + 0.4f, (t01 - 0.82f) / 0.18f); // dive onto target
        else                  y = cruiseY;                                                  // level cruise at 10 m
        return new Vector3(x, y, z);
    }
}

/// <summary>FPV-ДРОН (тип 37): дешёвая площадка (20 мет.), которая сама запускает дрон-камикадзе
/// в ближайшего зомби. Дрон взлетает, наводится на цель, таранит её и взрывается небольшим сплешем.
/// Работает сам — без нефти и металла, только перезарядка. Улучшай (E) — быстрее/дальше/больше урона.</summary>
public class FpvDronePad : Buildable
{
    public override bool IsTrap => false;

    float range = 45f;    // SHORT-range: the small FPV covers zombies near the base
    float reload = 3.5f;  // seconds between launches
    float blastR = 3.2f;  // drone detonation radius
    float dmg = 130f;     // damage at the epicentre
    float next;

    protected override void Awake()
    {
        BuildCost = 20;      // deliberately cheap — a spammy harasser
        MaxLevel = 3;
        UpgradeCost = 40;
        BuildTime = 1f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 140f; range = 45f; reload = 3.5f; blastR = 3.2f; dmg = 130f; break;
            case 2: MaxHealth = 180f; range = 65f; reload = 2.6f; blastR = 3.8f; dmg = 190f; break;
            default: MaxHealth = 220f; range = 90f; reload = 1.9f; blastR = 4.4f; dmg = 260f; break;
        }
        Health = MaxHealth;
    }

    protected override void OnActivated() { next = Time.time + 1f; }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        Zombie target = NearestZombie(range * range);
        if (target == null) return;
        next = Time.time + reload;
        Vector3 from = transform.position + Vector3.up * 0.6f;
        FpvDrone.Launch(from, target, blastR, dmg);
    }

    // The small FPV eats any KIND of zombie near the base — but it must pick an UNCLAIMED one so drones
    // SPREAD across different targets instead of a whole crowd diving on a single zombie (shared
    // reservation). If everything nearby is already claimed it briefly waits rather than dogpiling.
    Zombie NearestZombie(float rSq)
    {
        Zombie best = null; float bestSq = rSq;
        Vector3 p = transform.position;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            if (DroneTargets.IsClaimed(z)) continue; // another drone already dives on this one
            float d = (z.transform.position - p).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        return best;
    }
}

/// <summary>The flying kamikaze quadcopter: lifts off, homes onto its target zombie and rams it,
/// detonating for a small area blast. Spawned by <see cref="FpvDronePad"/>.</summary>
public class FpvDrone : MonoBehaviour
{
    Zombie target;
    Vector3 launch, lastAim;
    float blastR, dmg, life, flightTime;

    public static void Launch(Vector3 from, Zombie target, float blastR, float dmg)
    {
        var go = new GameObject("FpvDrone");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;
        Models.BuildFpvDrone(go.transform); // quadcopter visual
        var d = go.AddComponent<FpvDrone>();
        d.target = target; d.blastR = blastR; d.dmg = dmg;
        d.launch = from;
        d.lastAim = target != null ? target.transform.position : from + Vector3.forward * 5f;
        float dist = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(d.lastAim.x, d.lastAim.z));
        d.flightTime = Mathf.Clamp(dist / 40f + 0.9f, 1.2f, 4f);
        DroneTargets.Claim(target); // reserve this zombie so other drones pick different ones
    }

    void OnDestroy() { DroneTargets.Release(target); }

    void Update()
    {
        life += Time.deltaTime;
        if (target != null) lastAim = target.transform.position; // homing: track the zombie

        // LEVEL CRUISE ~10 m over the ground (NOT a high arc): climb, fly flat toward the target, dive in.
        float t01 = Mathf.Clamp01(life / flightTime);
        Vector3 prev = transform.position;
        Vector3 pos = DroneFlight.Path(launch, lastAim, t01);
        transform.position = pos;

        Vector3 vel = pos - prev;
        if (vel.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(vel.normalized); // nose along flight

        if (t01 >= 1f) { Detonate(); return; }
        if (Time.frameCount % 3 == 0) Effects.Burst(transform.position, new Color(0.55f, 0.55f, 0.6f), 1);
    }

    void Detonate()
    {
        Effects.Explosion(transform.position);
        Effects.AirBlast(transform.position + Vector3.up * 0.5f, blastR * 1.4f);
        float rSq = blastR * blastR;
        foreach (var z in Zombie.All)
            if (z != null && (z.transform.position - transform.position).sqrMagnitude < rSq)
                z.TakeDamage(dmg, Lang.T("друн", "drone"));
        Destroy(gameObject);
    }
}
