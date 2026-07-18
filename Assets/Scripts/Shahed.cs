using UnityEngine;

/// <summary>ГЕРАНЬ-2 (тип 38): площадка, запускающая дельтакрылый дрон-камикадзе (в стиле Shahed-136).
/// Дрон взлетает, выходит на крейсер ~12 м над землёй, идёт к ближайшему зомби и пикирует в него,
/// взрываясь фугасом. Дальнобойнее и мощнее FPV-друна, но медленнее и дороже. Работает сам.</summary>
public class ShahedPad : Buildable
{
    public override bool IsTrap => false;

    float range = 800f;   // LONG-range loitering munition — reaches across the map
    float reload = 4f;
    float blastR = 5.5f;
    float dmg = 320f;
    float next;

    protected override void Awake()
    {
        BuildCost = 200;   // late-game heavy loitering munition — cheap enough to field a battery
        MaxLevel = 3;
        UpgradeCost = 120;
        BuildTime = 2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 240f; range = 800f;  reload = 4.0f; blastR = 5.5f; dmg = 320f; break;
            case 2: MaxHealth = 320f; range = 1400f; reload = 3.0f; blastR = 6.5f; dmg = 460f; break;
            default: MaxHealth = 420f; range = 2000f; reload = 2.2f; blastR = 8.0f; dmg = 620f; break;
        }
        Health = MaxHealth;
    }

    protected override void OnActivated() { next = Time.time + 1.2f; }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        Zombie target = FarthestZombie(range * range);
        if (target == null) return;
        next = Time.time + reload;
        ShahedDrone.Launch(transform.position + Vector3.up * 0.7f, target, blastR, dmg);
    }

    const float MinRange = 250f;   // 3.1.1: HARD minimum — the big drone won't dive on point-blank zombies

    // Long-range loitering munition targets the FAR field: ignores anything closer than MinRange,
    // then picks the farthest zombie within range — so it reaches across the map.
    Zombie FarthestZombie(float rSq)
    {
        float minSq = MinRange * MinRange;
        Zombie best = null; float bestSq = 0f;
        Vector3 p = transform.position;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            if (DroneTargets.IsClaimed(z)) continue; // spread across zombies (shared drone reservation)
            float d = (z.transform.position - p).sqrMagnitude;
            if (d >= minSq && d <= rSq && d > bestSq) { bestSq = d; best = z; }
        }
        return best;
    }
}

/// <summary>The loitering delta-wing munition: climbs to a cruise altitude ~12 m above the ground,
/// flies level toward its target, then tips into a shallow dive and rams it. Spawned by ShahedPad.</summary>
public class ShahedDrone : MonoBehaviour
{
    Zombie target;
    Vector3 launch, lastAim;
    float blastR, dmg, life, flightTime;

    public static void Launch(Vector3 from, Zombie target, float blastR, float dmg)
    {
        var go = new GameObject("ShahedDrone");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;
        Models.BuildShahed(go.transform); // delta-wing visual
        var d = go.AddComponent<ShahedDrone>();
        d.target = target; d.blastR = blastR; d.dmg = dmg;
        d.launch = from;
        d.lastAim = target != null ? target.transform.position : from + Vector3.forward * 10f;
        float dist = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(d.lastAim.x, d.lastAim.z));
        d.flightTime = Mathf.Clamp(dist / 120f + 1.5f, 2f, 12f);
        DroneTargets.Claim(target); // reserve this zombie so pads spread across different ones
    }

    void OnDestroy() { DroneTargets.Release(target); }

    void Update()
    {
        life += Time.deltaTime;
        if (target != null) lastAim = target.transform.position; // homing: track the zombie

        // LEVEL CRUISE ~10 m over the ground (NOT a tall arc): climb, fly flat across the map, dive in.
        float t01 = Mathf.Clamp01(life / flightTime);
        Vector3 prev = transform.position;
        Vector3 pos = DroneFlight.Path(launch, lastAim, t01);
        transform.position = pos;

        Vector3 vel = pos - prev;
        if (vel.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(vel.normalized); // nose along flight

        if (t01 >= 1f) { Detonate(); return; }
        if (Time.frameCount % 4 == 0) Effects.Burst(transform.position, new Color(0.5f, 0.5f, 0.55f), 1);
    }

    void Detonate()
    {
        Effects.Explosion(transform.position);
        Effects.AirBlast(transform.position + Vector3.up * 0.5f, blastR * 1.5f);
        float rSq = blastR * blastR;
        foreach (var z in Zombie.All)
            if (z != null && (z.transform.position - transform.position).sqrMagnitude < rSq)
                z.TakeDamage(dmg);
        Destroy(gameObject);
    }
}
