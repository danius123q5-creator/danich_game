using UnityEngine;

/// <summary>Control block for an orbital laser station (super weapon). Costs 3000, paid
/// incrementally (E) like the other super-weapons. Once online, a satellite appears high
/// in the sky and fires a killing laser at the nearest zombie — burning metal from its own
/// reserve on every shot (top it up with E; an empty reserve stops the beam).</summary>
public class OrbitalStation : Buildable
{
    public override int FundingRequired => 3000;
    public override int ReserveMax => 1500;       // metal pool the beam drains as it fires
    const int ShotCost = 20;                       // metal burned per laser shot

    Transform station;
    float next;

    protected override void Awake()
    {
        BuildCost = 200;   // place the control block, then fund it to 3000
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 600f;
        Health = MaxHealth;
    }

    // Runs only once funded (Buildable.Update gates BuildableTick on !IsFunding).
    protected override void BuildableTick()
    {
        if (station == null) BuildStation();
        if (Time.time < next) return;
        next = Time.time + 0.45f;

        Zombie best = null; float bestSq = float.MaxValue;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            float d = (z.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        if (best == null) return;
        if (!SpendMetal(ShotCost)) return; // burns metal from its reserve; empty → beam stops (RELOAD)

        Vector3 to = best.transform.position + Vector3.up * 1f;
        Effects.Laser(station.position, to, new Color(1f, 0.25f, 0.2f));

        // Big explosion on impact: heavy splash damage to every zombie nearby.
        Effects.AirBlast(to, 9f);
        const float blastR = 6.5f;
        float rSq = blastR * blastR;
        var hitList = new System.Collections.Generic.List<Zombie>(Zombie.All);
        foreach (var z in hitList)
        {
            if (z == null || z.IsPuppet) continue;
            if ((z.transform.position - to).sqrMagnitude <= rSq) z.TakeDamage(160f);
        }
    }

    void BuildStation()
    {
        station = new GameObject("OrbitalStation").transform;
        if (GameBootstrap.World != null) station.SetParent(GameBootstrap.World);
        station.position = transform.position + Vector3.up * 130f; // high in the sky

        Color metal = new Color(0.6f, 0.62f, 0.66f);
        Color panel = new Color(0.15f, 0.25f, 0.45f);
        Color emit = new Color(1f, 0.3f, 0.25f);
        SP(PrimitiveType.Cube, Vector3.zero, new Vector3(2.4f, 1.4f, 2.4f), metal);                 // body
        SP(PrimitiveType.Cube, new Vector3(-4.2f, 0f, 0f), new Vector3(5f, 0.1f, 2.6f), panel);      // left solar panel
        SP(PrimitiveType.Cube, new Vector3(4.2f, 0f, 0f), new Vector3(5f, 0.1f, 2.6f), panel);       // right solar panel
        SP(PrimitiveType.Cylinder, new Vector3(0f, -1.1f, 0f), new Vector3(0.7f, 0.7f, 0.7f), emit);  // down-firing emitter

        if (Reserve <= 0) Reserve = 600; // starting charge so it fires right after coming online
    }

    void SP(PrimitiveType t, Vector3 pos, Vector3 scale, Color c)
    {
        var g = GameObject.CreatePrimitive(t);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(station, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
    }

    // Tear down the sky station whenever the control block goes away (death, sell, or
    // the whole world being destroyed) — it lives under World, not under this object.
    void OnDestroy()
    {
        if (station != null) Destroy(station.gameObject);
    }

    /// <summary>Endgame: detach the sky station and let it plummet and explode (used by the
    /// nuke finale). Clears our reference so destroying the control block won't kill it.</summary>
    public void Crash(float delay)
    {
        if (station == null) return;
        var st = station;
        station = null; // survive the control block being shredded
        var f = st.gameObject.AddComponent<FallingStation>();
        f.delay = delay;
        f.groundY = GameBootstrap.Hill(st.position.x, st.position.z);
    }
}

/// <summary>The orbital station after the nuke: hangs a beat, then falls out of the sky,
/// tumbling, and detonates in a big blast on impact.</summary>
public class FallingStation : MonoBehaviour
{
    public float delay = 1.8f;
    public float groundY;
    float t, vy;

    void Update()
    {
        t += Time.deltaTime;
        if (t < delay) return;
        vy += 50f * Time.deltaTime;
        transform.position += Vector3.down * vy * Time.deltaTime;
        transform.Rotate(45f * Time.deltaTime, 90f * Time.deltaTime, 25f * Time.deltaTime, Space.Self);
        if (transform.position.y <= groundY + 1.5f)
        {
            Effects.AirBlast(new Vector3(transform.position.x, groundY + 1f, transform.position.z), 24f); // big crash blast
            Destroy(gameObject);
        }
    }
}
