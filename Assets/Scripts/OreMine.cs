using System.Collections.Generic;
using UnityEngine;

/// <summary>Metal mine (ШАХТА) — a capturable map objective (default/waves mode). Stand in its
/// zone with no zombies to CAPTURE it (instant +2500 metal reward); once held it digs ore into a
/// pile that a conveyor carries to a metal vat. Zombies in the zone drain CONTROL — lose it and
/// you must recapture. Mirrors the refinery, but for metal. (Named OreMine — Mine is the landmine.)</summary>
public class OreMine : MonoBehaviour, IMetalSource
{
    public static readonly List<OreMine> All = new List<OreMine>();

    // Capture state to restore on continue (filled by GameRoot before SpawnAll runs).
    public struct SaveState { public bool captured; public float ore; }
    public static readonly List<SaveState> Pending = new List<SaveState>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() { All.Clear(); Pending.Clear(); }

    /// <summary>Restore a saved capture state (no metal bonus — that's only for a fresh capture).</summary>
    public void RestoreState(bool cap, float ore)
    {
        if (cap) { Captured = true; Capture = CaptureTime; Control = ControlMax; }
        Ore = Mathf.Clamp(ore, 0f, OreCap);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); MetalSources.Add(this); }
    void OnDisable() { All.Remove(this); MetalSources.Remove(this); }

    public const float Zone = 10f;
    public const float CaptureTime = 4f;
    public const float OreCap = 400f;
    public const float ControlMax = 100f;
    public const int CaptureOilBonus = 300; // 2.3: capturing the mine hands you 300 oil

    const float OreRate = 12f;          // ore/sec mined while held
    const float ControlRegen = 9f;
    const float DrainPerZombie = 7f;

    public bool Captured { get; private set; }
    public float Capture { get; private set; }
    public float Control { get; private set; }
    public float Ore { get; private set; }
    public int NearZombies { get; private set; }

    PlayerController player;
    Transform drill, statusOrb, orePile;
    float spin;

    public static void SpawnAll()
    {
        // Two mines, on angles that dodge the river trench (~x=40) and the refineries.
        float[] deg = { 150f, 30f };
        float r = 78f;
        int i = 0;
        foreach (float d in deg)
        {
            float a = d * Mathf.Deg2Rad;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            var mn = Create(new Vector3(x, GameBootstrap.Hill(x, z), z));
            if (mn != null && i < Pending.Count) mn.RestoreState(Pending[i].captured, Pending[i].ore); // continue: restore capture
            i++;
        }
        Pending.Clear(); // consume once
    }

    public static OreMine Create(Vector3 groundPos)
    {
        var go = new GameObject("OreMine");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = groundPos;
        var m = go.AddComponent<OreMine>();
        m.Build();
        return m;
    }

    void Build()
    {
        Color rock = new Color(0.30f, 0.29f, 0.27f);
        Color dark = new Color(0.17f, 0.16f, 0.15f);
        Color timber = new Color(0.40f, 0.28f, 0.16f);
        Color steel = new Color(0.34f, 0.35f, 0.38f);
        Color ore = new Color(0.58f, 0.47f, 0.30f);

        Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.1f, 0f), new Vector3(8f, 0.1f, 8f), new Color(0.30f, 0.29f, 0.27f), false); // pad

        // ── Rock face with a timber-framed tunnel entrance (at the back, -Z) ──
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.8f, -2.2f), new Vector3(6.5f, 3.6f, 2.4f), rock, false);          // cliff
        Prim(PrimitiveType.Cube, transform, new Vector3(-2.4f, 2.6f, -1.6f), new Vector3(1.6f, 1.4f, 1.4f), rock, false);       // boulder
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.1f, -1.0f), new Vector3(2.0f, 2.2f, 0.8f), dark, false);          // tunnel mouth (dark)
        Prim(PrimitiveType.Cube, transform, new Vector3(-1.2f, 1.2f, -0.9f), new Vector3(0.3f, 2.4f, 0.5f), timber, false);     // frame left
        Prim(PrimitiveType.Cube, transform, new Vector3(1.2f, 1.2f, -0.9f), new Vector3(0.3f, 2.4f, 0.5f), timber, false);      // frame right
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 2.4f, -0.9f), new Vector3(2.7f, 0.3f, 0.5f), timber, false);        // lintel

        // ── A-frame HEADFRAME (mine shaft tower) over the pit, leaning to a peak with a hoist wheel ──
        Vector3 peak = new Vector3(0f, 6.6f, 0.4f);
        Beam(new Vector3(-1.3f, 0f, 1.6f), peak, 0.22f, steel);   // front-left leg
        Beam(new Vector3(1.3f, 0f, 1.6f), peak, 0.22f, steel);    // front-right leg
        Beam(new Vector3(-1.1f, 0f, -1.0f), peak, 0.22f, steel);  // back-left leg
        Beam(new Vector3(1.1f, 0f, -1.0f), peak, 0.22f, steel);   // back-right leg
        Beam(new Vector3(-1.3f, 3.2f, 1.6f), new Vector3(1.3f, 3.2f, 1.6f), 0.12f, dark); // front cross-brace
        Beam(new Vector3(-1.2f, 2.0f, 0.3f), new Vector3(1.2f, 2.0f, 0.3f), 0.12f, dark); // mid cross-brace

        Beam(new Vector3(0f, 5.2f, -2.2f), peak, 0.16f, steel);                                     // rear back-stay to the peak
        Beam(new Vector3(-2.2f, 0f, -1.6f), new Vector3(0f, 5.2f, -2.2f), 0.14f, dark);              // brace
        Beam(new Vector3(2.2f, 0f, -1.6f), new Vector3(0f, 5.2f, -2.2f), 0.14f, dark);               // brace

        // Big hoist wheel at the peak (kept as 'drill' — it spins) with spokes + rim.
        Color wheelC = new Color(0.5f, 0.5f, 0.55f);
        drill = Prim(PrimitiveType.Cylinder, transform, peak + new Vector3(0f, 0.2f, 0f), new Vector3(2.1f, 0.16f, 2.1f), wheelC, false, new Vector3(90f, 0f, 0f)).transform;
        Prim(PrimitiveType.Cylinder, drill, Vector3.zero, new Vector3(1.15f, 1.05f, 1.15f), dark, false);                        // dark inner (recessed)
        for (int s = 0; s < 6; s++)                                                                   // spokes
            Prim(PrimitiveType.Cube, drill, Vector3.zero, new Vector3(0.14f, 0.9f, 1.9f), new Color(0.4f, 0.4f, 0.44f), false, new Vector3(0f, 0f, s * 30f));
        Prim(PrimitiveType.Cylinder, transform, peak + new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.22f, 0.5f), dark, false, new Vector3(90f, 0f, 0f)); // hub
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.2f, 0.4f), new Vector3(0.1f, 2.4f, 0.1f), dark, false);            // hoist cable down the shaft

        // ── Winch/hoist house at the base of the headframe ──
        Prim(PrimitiveType.Cube, transform, new Vector3(-3.0f, 0.9f, 2.4f), new Vector3(2.4f, 1.8f, 2.0f), timber, false);       // shed body
        Prim(PrimitiveType.Cube, transform, new Vector3(-3.0f, 2.0f, 2.4f), new Vector3(2.7f, 0.35f, 2.3f), dark, false);        // shed roof
        Prim(PrimitiveType.Cylinder, transform, new Vector3(-3.0f, 0.9f, 3.5f), new Vector3(0.5f, 0.6f, 0.5f), steel, false, new Vector3(90f, 0f, 0f)); // winch drum

        // ── Ore chute → cart on rails, plus a couple of ore heaps ──
        var chute = Prim(PrimitiveType.Cube, transform, new Vector3(1.6f, 1.4f, 1.4f), new Vector3(0.7f, 0.25f, 2.2f), timber, false);
        chute.transform.localRotation = Quaternion.Euler(28f, -50f, 0f);
        Prim(PrimitiveType.Cube, transform, new Vector3(2.9f, 0.18f, 0.0f), new Vector3(0.1f, 0.12f, 3.0f), steel, false);       // rail
        Prim(PrimitiveType.Cube, transform, new Vector3(2.3f, 0.18f, 0.0f), new Vector3(0.1f, 0.12f, 3.0f), steel, false);       // rail
        for (int s = -2; s <= 2; s++)                                                                 // rail sleepers
            Prim(PrimitiveType.Cube, transform, new Vector3(2.6f, 0.12f, s * 1.2f), new Vector3(1.0f, 0.1f, 0.18f), timber, false);
        var cart = Prim(PrimitiveType.Cube, transform, new Vector3(2.6f, 0.55f, 0.8f), new Vector3(1.7f, 0.9f, 1.3f), dark, true); // collectable cart (collider)
        orePile = Prim(PrimitiveType.Cube, cart.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.85f, 0.1f, 0.85f), ore, false).transform;
        Prim(PrimitiveType.Sphere, transform, new Vector3(3.6f, 0.35f, 2.4f), new Vector3(1.6f, 0.7f, 1.6f), ore, false);        // ore heap
        Prim(PrimitiveType.Sphere, transform, new Vector3(-1.2f, 0.3f, 3.4f), new Vector3(1.2f, 0.5f, 1.2f), ore, false);        // ore heap

        statusOrb = Prim(PrimitiveType.Sphere, transform, peak + new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 0.8f, 0.8f), Color.grey, false).transform; // beacon on the peak
    }

    // A beam (thin box) spanning two local points — for the headframe lattice.
    void Beam(Vector3 a, Vector3 b, float thick, Color c)
    {
        Vector3 mid = (a + b) * 0.5f, d = b - a;
        float len = d.magnitude;
        var g = Prim(PrimitiveType.Cube, transform, mid, new Vector3(thick, len, thick), c, false);
        if (len > 0.001f) g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (player == null) player = FindFirstObjectByType<PlayerController>();

        int zc = 0;
        Vector3 p = transform.position;
        foreach (var z in Zombie.All)
        {
            if (z == null || z.team >= 0) continue;
            if ((z.transform.position - p).sqrMagnitude <= Zone * Zone) zc++;
        }
        NearZombies = zc;

        bool playerInZone = player != null && !player.IsDead &&
                            (player.transform.position - p).sqrMagnitude <= Zone * Zone;

        if (!Captured)
        {
            if (playerInZone && zc == 0)
            {
                Capture += dt;
                if (Capture >= CaptureTime)
                {
                    Captured = true; Capture = CaptureTime; Control = ControlMax;
                    if (player != null) { player.AddMetal(GameManager.CaptureMetalReward()); player.AddOil(CaptureOilBonus); } // +metal (scales w/ wave) & +300 oil
                    Effects.Upgrade(p + Vector3.up * 5f);
                }
            }
            else Capture = Mathf.Max(0f, Capture - dt * 0.6f);
        }
        else
        {
            Ore = Mathf.Min(OreCap, Ore + OreRate * GameRoot.IncomeMult * GameManager.ResourceWaveMult() * dt); // 2× endless + 2.7 поздний бонус после 10 волны
            if (zc > 0) Control -= DrainPerZombie * Mathf.Min(zc, 6) * dt;
            else Control = Mathf.Min(ControlMax, Control + ControlRegen * dt);
            if (Control <= 0f) { Captured = false; Control = 0f; Capture = 0f; Effects.AirBlast(p + Vector3.up * 1f, 8f); }
        }

        Animate(dt);
    }

    void Animate(float dt)
    {
        spin += dt * (Captured ? 160f : 25f);
        if (drill != null) drill.localRotation = Quaternion.Euler(90f, 0f, spin);
        if (orePile != null) { float f = Ore / OreCap; orePile.localScale = new Vector3(0.85f, Mathf.Max(0.05f, f * 1.2f), 0.85f); }
        if (statusOrb != null)
        {
            Color c = !Captured ? new Color(0.6f, 0.6f, 0.6f)
                    : NearZombies > 0 ? new Color(1f, 0.45f, 0.15f)
                    : new Color(0.3f, 1f, 0.4f);
            GameBootstrap.SetColor(statusOrb.gameObject, c);
        }
    }

    // IMetalSource: the conveyor network pulls ore from a captured mine's pile.
    public bool MetalActive => Captured;
    public Transform MetalTransform => transform;
    public float DrawMetal(float amount)
    {
        if (!Captured || Ore <= 0f || amount <= 0f) return 0f;
        float take = Mathf.Min(amount, Ore);
        Ore -= take;
        return take;
    }

    GameObject Prim(PrimitiveType t, Transform parent, Vector3 lp, Vector3 ls, Color c, bool collider, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = ls;
        GameBootstrap.SetColor(g, c);
        return g;
    }
}
