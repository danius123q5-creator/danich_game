using UnityEngine;

/// <summary>Control block for an orbital laser station (super weapon). Costs 3000, paid
/// incrementally (E) like the other super-weapons. Once online, a satellite appears high in
/// the sky and cycles through THREE attacks, burning metal from its own reserve (top it up
/// with E; an empty reserve stops the beams):
///   1) SINGLE SHOT — a few pinpoint lasers, each detonating in a big blast.
///   2) BURN       — one long beam that slides from zombie to zombie, scorching everything under it.
///   3) PRISM      — three long beams in a triangle, spinning around the base and slicing the horde.</summary>
public class OrbitalStation : Buildable
{
    public override int FundingRequired => 3000;
    public override int ReserveMax => 1500;       // metal pool the beams drain as they fire
    const int ShotCost = 20;                       // metal burned per single-shot laser

    enum Mode { Single, Burn, Prism }
    Mode mode = Mode.Single;

    Transform station;
    float restUntil;        // brief pause between attacks
    float phaseEnd;         // when the current Burn/Prism attack ends
    float fireTimer;        // single-shot pacing
    float dmgTimer;         // continuous-beam damage ticking
    int shotsLeft;          // single-shot burst counter
    float prismAngle;       // spinning offset for the triple prism
    Vector3 burnPoint;      // current endpoint of the burning beam
    bool burnReady;         // burnPoint has been seeded this attack
    readonly LineRenderer[] beams = new LineRenderer[3];
    Material beamMat;

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

        // Idle (beams off) while resting between attacks or when the map is clear.
        if (Time.time < restUntil || !AnyTarget()) { HideBeams(); return; }

        switch (mode)
        {
            case Mode.Single: TickSingle(); break;
            case Mode.Burn:   TickBurn();   break;
            default:          TickPrism();  break;
        }
    }

    // --- attack 1: a short burst of pinpoint lasers, each with a big blast ---
    void TickSingle()
    {
        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f) return;
        fireTimer = 0.45f;

        Zombie best = Nearest(transform.position);
        if (best == null) { NextMode(); return; }
        if (!SpendMetal(ShotCost)) { HideBeams(); return; }

        Vector3 to = best.transform.position + Vector3.up * 1f;
        Effects.Laser(station.position, to, new Color(1f, 0.25f, 0.2f));
        Effects.AirBlast(to, 9f);                 // big explosion on impact
        Scorch(to, 6.5f, 160f);

        if (--shotsLeft <= 0) NextMode();
    }

    // --- attack 2: one long beam that slides from zombie to zombie, burning a trail ---
    void TickBurn()
    {
        if (Time.time >= phaseEnd) { NextMode(); return; }

        Zombie t = Nearest(burnReady ? burnPoint : transform.position);
        if (t == null) { NextMode(); return; }
        Vector3 targetPos = t.transform.position + Vector3.up * 1f;
        if (!burnReady) { burnPoint = targetPos; burnReady = true; }

        burnPoint = Vector3.MoveTowards(burnPoint, targetPos, 26f * Time.deltaTime); // sweeps over
        SetBeam(0, station.position, burnPoint, new Color(1f, 0.5f, 0.12f), 0.9f);

        dmgTimer -= Time.deltaTime;
        if (dmgTimer <= 0f)
        {
            dmgTimer = 0.12f;
            if (!SpendMetal(6)) { HideBeams(); return; } // empty reserve halts the burn
            Scorch(burnPoint, 4.5f, 110f);
            Effects.Burst(burnPoint, new Color(1f, 0.6f, 0.2f), 6);
        }
    }

    // --- attack 3: three long beams in a triangle, spinning around the base ---
    void TickPrism()
    {
        if (Time.time >= phaseEnd) { NextMode(); return; }

        prismAngle += 130f * Time.deltaTime; // rotation speed
        const float R = 8.5f;
        var ends = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            float a = (prismAngle + k * 120f) * Mathf.Deg2Rad;
            Vector3 gp = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R;
            gp.y = GameBootstrap.Hill(gp.x, gp.z) + 0.5f;
            ends[k] = gp;
            SetBeam(k, station.position, gp, new Color(0.55f, 0.4f, 1f), 0.8f);
        }

        dmgTimer -= Time.deltaTime;
        if (dmgTimer <= 0f)
        {
            dmgTimer = 0.12f;
            if (!SpendMetal(8)) { HideBeams(); return; } // empty reserve halts the prism
            for (int k = 0; k < 3; k++)
            {
                Scorch(ends[k], 3.6f, 70f);
                Effects.Burst(ends[k], new Color(0.7f, 0.5f, 1f), 4);
            }
        }
    }

    // Advance to the next attack in the cycle, after a short rest.
    void NextMode()
    {
        HideBeams();
        mode = mode == Mode.Single ? Mode.Burn : mode == Mode.Burn ? Mode.Prism : Mode.Single;
        restUntil = Time.time + 0.8f;
        StartMode();
    }

    void StartMode()
    {
        dmgTimer = 0f;
        if (mode == Mode.Single) { shotsLeft = 4; fireTimer = 0f; }
        else { phaseEnd = restUntil + 4.5f; burnReady = false; prismAngle = 0f; }
    }

    // --- helpers ---
    bool AnyTarget()
    {
        foreach (var z in Zombie.All)
            if (z != null && !z.IsPuppet && !(GameRoot.IsZvZ && z.team == Team)) return true;
        return false;
    }

    Zombie Nearest(Vector3 from)
    {
        Zombie best = null; float bestSq = float.MaxValue;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            float d = (z.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        return best;
    }

    void Scorch(Vector3 at, float radius, float dmg)
    {
        float rSq = radius * radius;
        var list = new System.Collections.Generic.List<Zombie>(Zombie.All);
        foreach (var z in list)
        {
            if (z == null || z.IsPuppet) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue;
            if ((z.transform.position - at).sqrMagnitude <= rSq) z.TakeDamage(dmg);
        }
    }

    LineRenderer Beam(int i)
    {
        if (beams[i] == null)
        {
            var go = new GameObject("OrbBeam" + i);
            go.transform.SetParent(station, false);
            var lr = go.AddComponent<LineRenderer>();
            if (beamMat == null) beamMat = new Material(GameBootstrap.LineShader());
            lr.sharedMaterial = beamMat;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.enabled = false;
            beams[i] = lr;
        }
        return beams[i];
    }

    void SetBeam(int i, Vector3 from, Vector3 to, Color c, float w)
    {
        var lr = Beam(i);
        lr.enabled = true;
        lr.startColor = lr.endColor = c;
        lr.startWidth = w * 0.6f; lr.endWidth = w;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
    }

    void HideBeams()
    {
        for (int i = 0; i < beams.Length; i++)
            if (beams[i] != null) beams[i].enabled = false;
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
        StartMode();                     // begin the attack cycle on the single shot
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
    public float delay = 1.5f;
    public float groundY;
    float t, vy;

    void Start()
    {
        // Light the wreck up so it reads as a burning satellite against the mushroom cloud.
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r != null) GameBootstrap.SetColor(r.gameObject, new Color(1f, 0.5f, 0.2f));

        // Fat fire/smoke trail — visible streaking down across the whole frame.
        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time = 1.4f;
        tr.startWidth = 4.5f; tr.endWidth = 0.3f;
        tr.minVertexDistance = 0.2f;
        var mat = new Material(GameBootstrap.LineShader());
        mat.color = new Color(1f, 0.55f, 0.2f);
        tr.sharedMaterial = mat;
        tr.startColor = new Color(1f, 0.65f, 0.2f, 1f);
        tr.endColor = new Color(1f, 0.3f, 0.1f, 0f);
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < delay) return;
        vy += 42f * Time.deltaTime;
        transform.position += Vector3.down * vy * Time.deltaTime;
        transform.Rotate(60f * Time.deltaTime, 110f * Time.deltaTime, 35f * Time.deltaTime, Space.Self);
        if (transform.position.y <= groundY + 1.5f)
        {
            var at = new Vector3(transform.position.x, groundY + 1f, transform.position.z);
            Effects.AirBlast(at, 34f); // big crash blast (double-punched)
            Effects.AirBlast(at, 18f);
            Destroy(gameObject);
        }
    }
}
