using System.Collections.Generic;
using UnityEngine;

/// <summary>Metal mine (ШАХТА) — a capturable map objective (default/waves mode). Stand in its
/// zone with no zombies to CAPTURE it (instant +2500 metal reward); once held it digs ore into a
/// pile that a conveyor carries to a metal vat. Zombies in the zone drain CONTROL — lose it and
/// you must recapture. Mirrors the refinery, but for metal. (Named OreMine — Mine is the landmine.)</summary>
public class OreMine : MonoBehaviour, IMetalSource
{
    public static readonly List<OreMine> All = new List<OreMine>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); MetalSources.Add(this); }
    void OnDisable() { All.Remove(this); MetalSources.Remove(this); }

    public const float Zone = 10f;
    public const float CaptureTime = 4f;
    public const float OreCap = 400f;
    public const float ControlMax = 100f;

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
        foreach (float d in deg)
        {
            float a = d * Mathf.Deg2Rad;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            Create(new Vector3(x, GameBootstrap.Hill(x, z), z));
        }
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
        Color rock = new Color(0.32f, 0.30f, 0.28f);
        Color dark = new Color(0.20f, 0.19f, 0.18f);
        Color ore = new Color(0.55f, 0.45f, 0.30f);

        Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.1f, 0f), new Vector3(7f, 0.1f, 7f), new Color(0.30f, 0.29f, 0.27f), false); // pad
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.4f, -1.5f), new Vector3(4.5f, 2.8f, 2.5f), rock, false);   // mound
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 0.9f, -0.3f), new Vector3(1.6f, 1.8f, 0.6f), dark, false);   // tunnel mouth
        for (int i = 0; i < 4; i++) // headframe legs
        {
            float sx = (i & 1) == 0 ? 1f : -1f, sz = (i & 2) == 0 ? 1f : -1f;
            var leg = Prim(PrimitiveType.Cube, transform, new Vector3(sx * 0.8f, 2.4f, 1.6f + sz * 0.5f), new Vector3(0.2f, 5f, 0.2f), dark, false);
            leg.transform.localRotation = Quaternion.Euler(0f, 0f, -sx * 7f);
        }
        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 4.7f, 1.6f), new Vector3(2.0f, 0.4f, 2.0f), dark, false);
        drill = Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 4.7f, 1.6f), new Vector3(1.2f, 0.15f, 1.2f), new Color(0.5f, 0.5f, 0.55f), false, new Vector3(90f, 0f, 0f)).transform; // hoist wheel
        var cart = Prim(PrimitiveType.Cube, transform, new Vector3(2.6f, 0.55f, 0f), new Vector3(1.8f, 1.0f, 1.4f), dark, true); // collectable cart (collider)
        orePile = Prim(PrimitiveType.Cube, cart.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.85f, 0.1f, 0.85f), ore, false).transform;
        statusOrb = Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 5.4f, 1.6f), new Vector3(0.7f, 0.7f, 0.7f), Color.grey, false).transform;
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
                    if (player != null) player.AddMetal(PlayerController.CaptureMetalBonus); // +2500 on capture
                    Effects.Upgrade(p + Vector3.up * 5f);
                }
            }
            else Capture = Mathf.Max(0f, Capture - dt * 0.6f);
        }
        else
        {
            Ore = Mathf.Min(OreCap, Ore + OreRate * dt);
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
