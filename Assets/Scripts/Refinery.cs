using System.Collections.Generic;
using UnityEngine;

/// <summary>Oil refinery (НПЗ) — a capturable map objective (default/waves mode only).
/// Stand in its zone with no zombies around to CAPTURE it; once held, oil drips from the
/// pipe into a barrel (up to a cap). Walk up to the barrel and press E to draw the oil into
/// your personal reserve, then pour it into a super-weapon (oil is needed ON TOP of metal).
/// Zombies that wander into the zone drain your CONTROL; if it hits zero you lose the НПЗ and
/// must recapture it — so defend it with turrets and walls.</summary>
public class Refinery : MonoBehaviour, IOilSource
{
    public static readonly List<Refinery> All = new List<Refinery>();

    // Capture state to restore on continue (filled by GameRoot before SpawnAll runs).
    public struct SaveState { public bool captured; public float oil; }
    public static readonly List<SaveState> Pending = new List<SaveState>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() { All.Clear(); Pending.Clear(); }

    /// <summary>Restore a saved capture state (no metal bonus — that's only for a fresh capture).</summary>
    public void RestoreState(bool cap, float oil)
    {
        if (cap) { Captured = true; Capture = CaptureTime; Control = ControlMax; }
        Oil = Mathf.Clamp(oil, 0f, OilCap);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); OilSources.Add(this); }
    void OnDisable() { All.Remove(this); OilSources.Remove(this); }

    // IOilSource: the pipe network can pull oil from a captured refinery.
    public bool OilActive => Captured;
    public Transform OilTransform => transform;

    public const float Zone = 10f;          // capture / contest radius
    public const float CaptureTime = 4f;    // seconds to capture from neutral
    public const float OilCap = 280f;       // barrel capacity (2.3: bigger barrel — drips more)
    public const float ControlMax = 100f;
    public const int CaptureOilBonus = 600; // 2.3: capturing the НПЗ (вышка) hands you 600 oil

    const float OilRate = 13f;              // oil/sec dripping into the barrel while held (2.3: faster)
    const float ControlRegen = 9f;          // control/sec regained when no zombies are near
    const float DrainPerZombie = 7f;        // control/sec lost per nearby zombie (capped)
    const float CollectReach = 5.5f;        // how close to the barrel you can press E

    public bool Captured { get; private set; }
    public float Capture { get; private set; }   // 0..CaptureTime progress while neutral
    public float Control { get; private set; }   // 0..ControlMax once captured
    public float Oil { get; private set; }       // oil currently in the barrel
    public int NearZombies { get; private set; } // updated each frame (for HUD "под атакой")

    PlayerController player;
    Transform nodder, statusOrb, oilFill, barrel;
    float pumpPhase;

    /// <summary>Place a few refineries around the map centre (away from the player's base).
    /// Default/waves mode only — caller gates on mode.</summary>
    public static void SpawnAll()
    {
        // Three points on a wide ring, at angles chosen to dodge the river trench (~x=40).
        float[] deg = { 90f, 205f, 330f };
        float r = 72f;
        int i = 0;
        foreach (float d in deg)
        {
            float a = d * Mathf.Deg2Rad;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            var rf = Create(new Vector3(x, GameBootstrap.Hill(x, z), z));
            if (rf != null && i < Pending.Count) rf.RestoreState(Pending[i].captured, Pending[i].oil); // continue: restore capture
            i++;
        }
        Pending.Clear(); // consume once
    }

    public static Refinery Create(Vector3 groundPos)
    {
        var go = new GameObject("Refinery");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = groundPos;
        var rf = go.AddComponent<Refinery>();
        rf.Build();
        return rf;
    }

    void Build()
    {
        Color steel = new Color(0.32f, 0.33f, 0.36f);
        Color dark = new Color(0.17f, 0.18f, 0.20f);
        Color rust = new Color(0.45f, 0.32f, 0.18f);
        Color oilCol = new Color(0.08f, 0.07f, 0.06f);
        Color tankC = new Color(0.20f, 0.24f, 0.22f);

        Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.1f, 0f), new Vector3(8f, 0.1f, 8f), new Color(0.33f, 0.33f, 0.31f), false); // pad

        // ── Tall lattice DRILLING DERRICK (4 legs + X-braces, narrows to a crown) ──
        float H = 13f, baseHalf = 1.5f, topHalf = 0.5f;
        Vector3[] foot = {
            new Vector3(-baseHalf, 0f, -baseHalf), new Vector3(baseHalf, 0f, -baseHalf),
            new Vector3(baseHalf, 0f, baseHalf),   new Vector3(-baseHalf, 0f, baseHalf) };
        Vector3[] top = {
            new Vector3(-topHalf, H, -topHalf), new Vector3(topHalf, H, -topHalf),
            new Vector3(topHalf, H, topHalf),   new Vector3(-topHalf, H, topHalf) };
        for (int i = 0; i < 4; i++) Beam(foot[i], top[i], 0.18f, steel);                 // 4 legs
        int rungs = 6;
        for (int r = 1; r <= rungs; r++) // horizontal rings + X cross-braces on each face
        {
            float t0 = (r - 1) / (float)rungs, t1 = r / (float)rungs;
            Vector3[] a = new Vector3[4], b = new Vector3[4];
            for (int i = 0; i < 4; i++) { a[i] = Vector3.Lerp(foot[i], top[i], t0); b[i] = Vector3.Lerp(foot[i], top[i], t1); }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Beam(b[i], b[j], 0.1f, dark);          // ring at this level
                Beam(a[i], b[j], 0.08f, dark);          // diagonal /
                Beam(a[j], b[i], 0.08f, dark);          // diagonal \  → X-brace
            }
        }
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, H + 0.25f, 0f), new Vector3(1.3f, 0.5f, 1.3f), dark, false);       // crown block
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.3f, 0f), new Vector3(2.6f, 0.3f, 2.6f), rust, false);            // drill floor
        Prim(PrimitiveType.Cube, transform, new Vector3(2.1f, 1.0f, 1.6f), new Vector3(1.6f, 1.8f, 1.4f), rust, false);        // doghouse shed

        // ── Nodding pumpjack beside the derrick (kept as 'nodder') ──
        Prim(PrimitiveType.Cube, transform, new Vector3(3.6f, 0.9f, 0f), new Vector3(0.8f, 1.8f, 0.8f), dark, false);          // sampson post
        var pivot = new GameObject("Nodder"); pivot.transform.SetParent(transform, false);
        pivot.transform.localPosition = new Vector3(3.6f, 1.9f, 0f); nodder = pivot.transform;
        Prim(PrimitiveType.Cube, nodder, new Vector3(0f, 0f, 0f), new Vector3(3.6f, 0.3f, 0.3f), steel, false);                // walking beam
        Prim(PrimitiveType.Cube, nodder, new Vector3(1.8f, -0.25f, 0f), new Vector3(0.55f, 0.8f, 0.6f), rust, false);          // horse head
        Prim(PrimitiveType.Cylinder, nodder, new Vector3(-1.7f, -0.05f, 0f), new Vector3(0.9f, 0.3f, 0.9f), dark, false, new Vector3(90f, 0f, 0f)); // counterweight

        // ── Storage tanks ──
        Prim(PrimitiveType.Cylinder, transform, new Vector3(-3.4f, 1.0f, -1.6f), new Vector3(2.0f, 1.0f, 2.0f), tankC, false);
        Prim(PrimitiveType.Cylinder, transform, new Vector3(-3.4f, 1.0f, 1.4f), new Vector3(1.6f, 1.0f, 1.6f), tankC, false);

        // ── Pipe → collectable barrel (E to draw oil) + fill indicator ──
        Prim(PrimitiveType.Cube, transform, new Vector3(-1.6f, 0.5f, 3.0f), new Vector3(0.25f, 0.25f, 3.2f), steel, false);
        var bar = Prim(PrimitiveType.Cylinder, transform, new Vector3(-3.3f, 0.7f, 3.0f), new Vector3(1.5f, 0.7f, 1.5f), rust, true); // collider
        barrel = bar.transform;
        oilFill = Prim(PrimitiveType.Cylinder, barrel, new Vector3(0f, 0f, 0f), new Vector3(0.8f, 0.01f, 0.8f), oilCol, false).transform;

        statusOrb = Prim(PrimitiveType.Sphere, transform, new Vector3(0f, H + 0.7f, 0f), new Vector3(0.8f, 0.8f, 0.8f), Color.grey, false).transform; // beacon on the crown
    }

    // A beam (thin box) spanning two local points — used to build the lattice derrick.
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

        // Count nearby zombies (normal-team only — refineries are a default-mode feature).
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
        // Работяги-пешки тоже держат точку: пешка в зоне засчитывается как захватчик.
        bool friendlyInZone = playerInZone || AllyPawn.AnyInZone(p, Zone);

        if (!Captured)
        {
            if (friendlyInZone && zc == 0)
            {
                Capture += dt;
                if (Capture >= CaptureTime)
                {
                    Captured = true; Capture = CaptureTime; Control = ControlMax;
                    if (player != null) { player.AddMetal(GameManager.CaptureMetalReward()); player.AddOil(CaptureOilBonus); } // +metal (scales w/ wave) & +600 oil
                    Effects.Upgrade(p + Vector3.up * 6f);
                }
            }
            else Capture = Mathf.Max(0f, Capture - dt * 0.6f); // slowly decays if you step out / zombies arrive
        }
        else
        {
            Oil = Mathf.Min(OilCap, Oil + OilRate * GameRoot.IncomeMult * GameManager.ResourceWaveMult() * dt); // pipe keeps dripping (2× endless + 2.7 поздний бонус после 10 волны)

            if (zc > 0) Control -= DrainPerZombie * Mathf.Min(zc, 6) * dt;
            else Control = Mathf.Min(ControlMax, Control + ControlRegen * dt);

            if (Control <= 0f) { Captured = false; Control = 0f; Capture = 0f; Effects.AirBlast(p + Vector3.up * 1f, 8f); } // lost it
        }

        Animate(dt);
    }

    void Animate(float dt)
    {
        pumpPhase += dt * (Captured ? 2.2f : 0.4f);
        if (nodder != null) nodder.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(pumpPhase) * 14f);

        if (oilFill != null)
        {
            float f = Oil / OilCap;
            oilFill.localScale = new Vector3(0.8f, Mathf.Max(0.01f, f), 0.8f);
            oilFill.localPosition = new Vector3(0f, -0.5f + f * 0.5f, 0f); // grow up from the barrel floor
        }
        if (statusOrb != null)
        {
            Color c = !Captured ? new Color(0.6f, 0.6f, 0.6f)
                    : NearZombies > 0 ? new Color(1f, 0.45f, 0.15f)   // contested
                    : new Color(0.3f, 1f, 0.4f);                       // held safely
            GameBootstrap.SetColor(statusOrb.gameObject, c);
            float s = 0.7f + (Captured && NearZombies > 0 ? Mathf.Abs(Mathf.Sin(pumpPhase * 3f)) * 0.25f : 0f);
            statusOrb.localScale = new Vector3(s, s, s);
        }
    }

    /// <summary>E at the barrel: pour the barrel's oil into the player's reserve.</summary>
    public void CollectOil(PlayerController pc)
    {
        if (pc == null) return;
        if (barrel != null && (pc.transform.position - barrel.position).sqrMagnitude > CollectReach * CollectReach) return;
        if (!Captured) { Effects.Burst(transform.position + Vector3.up * 6f, new Color(1f, 0.5f, 0.2f), 4); return; } // not yours yet
        int space = PlayerController.OilMax - pc.Oil;
        int take = Mathf.Min(Mathf.FloorToInt(Oil), space);
        if (take <= 0) return;
        pc.AddOil(take);
        Oil -= take;
        Effects.Burst(barrel.position + Vector3.up * 0.8f, new Color(0.1f, 0.09f, 0.08f), 8);
    }

    /// <summary>Pull oil out of the barrel (used by the oil pipe/doser network). Only a
    /// captured refinery yields oil; returns the amount actually drawn.</summary>
    public float DrawOil(float amount)
    {
        if (!Captured || Oil <= 0f || amount <= 0f) return 0f;
        float take = Mathf.Min(amount, Oil);
        Oil -= take;
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
