using UnityEngine;

/// <summary>ФАУ-1 (тип 39): огромная наклонная пусковая рампа с крылатой бомбой «Фау-1». Самый тяжёлый
/// «друн» — пускает бомбу по высокой БАЛЛИСТИЧЕСКОЙ дуге в дальнего зомби и накрывает ОГРОМНЫМ фугасом.
/// Дальнобойный и мощный, но дорогой и с долгой перезарядкой. Работает сам, без нефти.</summary>
public class VOnePad : Buildable
{
    public override bool IsTrap => false;

    float range = 1000f, reload = 6f, blastR = 10f, dmg = 500f, next;

    protected override void Awake()
    {
        BuildCost = 350;
        MaxLevel = 3;
        UpgradeCost = 200;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            // 3.1.1: ФАУ-1 is a HUGE flying bomb — its warhead does GIGANTIC damage over a massive
            // radius. The priciest super-drone: a proper map-clearing nuke on a long reload.
            case 1: MaxHealth = 320f; range = 1000f; reload = 3.5f; blastR = 20f; dmg = 1800f; break;
            case 2: MaxHealth = 440f; range = 1500f; reload = 2.4f; blastR = 26f; dmg = 2800f; break;
            default: MaxHealth = 560f; range = 2000f; reload = 1.6f; blastR = 32f; dmg = 4200f; break;
        }
        Health = MaxHealth;
    }

    protected override void OnActivated() { next = Time.time + 1.5f; }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        Zombie t = FarthestZombie(range * range);
        if (t == null) return;
        next = Time.time + reload;
        VOneBomb.Launch(transform.position + Vector3.up * 1.2f, t, blastR, dmg);
    }

    const float MinRange = 350f;   // 3.1.1: HARD minimum — ФАУ-1 never wastes itself point-blank

    // A long-range weapon reaches the FAR field, not point-blank zombies (turrets/RPG/FPV handle
    // those). Ignores anything closer than MinRange, then picks the farthest within range.
    Zombie FarthestZombie(float rSq)
    {
        float minSq = MinRange * MinRange;
        Zombie best = null; float bestSq = 0f; Vector3 p = transform.position;
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

/// <summary>The flying V-1 buzz bomb: a big ballistic arc from the ramp to the target, then a huge blast.</summary>
public class VOneBomb : MonoBehaviour
{
    Zombie target;
    Vector3 launch, lastAim;
    float blastR, dmg, life, flightTime;

    public static void Launch(Vector3 from, Zombie target, float blastR, float dmg)
    {
        var go = new GameObject("VOneBomb");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;
        Models.BuildVOne(go.transform); // V-1 flying bomb visual
        go.transform.localScale = Vector3.one * 2.6f; // ФАУ-1 is a HUGE drone — big menacing airframe
        var d = go.AddComponent<VOneBomb>();
        d.target = target; d.blastR = blastR; d.dmg = dmg;
        d.launch = from;
        d.lastAim = target != null ? target.transform.position : from + Vector3.forward * 10f;
        float dist = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(d.lastAim.x, d.lastAim.z));
        d.flightTime = Mathf.Clamp(dist / 110f + 2f, 2.5f, 13f);
        DroneTargets.Claim(target);
    }

    void OnDestroy() { DroneTargets.Release(target); }

    void Update()
    {
        life += Time.deltaTime;
        if (target != null) lastAim = target.transform.position; // homing

        // LEVEL CRUISE ~10 m over the ground (NOT a towering arc): the V-1 buzzes in flat, then dives.
        float t01 = Mathf.Clamp01(life / flightTime);
        Vector3 prev = transform.position;
        Vector3 pos = DroneFlight.Path(launch, lastAim, t01);
        transform.position = pos;

        Vector3 vel = pos - prev;
        if (vel.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(vel.normalized); // nose along flight

        if (t01 >= 1f) { Detonate(); return; }
        if (Time.frameCount % 3 == 0) Effects.Burst(transform.position, new Color(0.6f, 0.6f, 0.62f), 2);
    }

    void Detonate()
    {
        Effects.Explosion(transform.position);
        Effects.AirBlast(transform.position + Vector3.up * 1f, blastR * 1.6f);
        Effects.FlashLight(transform.position, 12f, 60f, new Color(1f, 0.6f, 0.25f));
        float rSq = blastR * blastR;
        foreach (var z in Zombie.All)
            if (z != null && (z.transform.position - transform.position).sqrMagnitude < rSq)
                z.TakeDamage(dmg);
        Destroy(gameObject);
    }
}
