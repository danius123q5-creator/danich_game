using System.Collections.Generic;
using UnityEngine;

/// <summary>Oil pipe (ЭКОНОМИКА) — a relay that carries oil from a captured refinery toward an
/// oil doser. A pipe is "live" when it links (directly or through other pipes) to a captured НПЗ,
/// extending oil supply across the map so the doser can sit at your base, far from the refinery.</summary>
public class OilPipe : Buildable
{
    public static readonly List<OilPipe> All = new List<OilPipe>();
    public const float Link = 16f; // connection range: pipe↔pipe, pipe↔НПЗ, pipe↔doser

    public bool Live { get; private set; } // connected back to a captured refinery

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPipes() => All.Clear();

    protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
    protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

    protected override void Awake()
    {
        BuildCost = 15;   // per segment — a drag lays a whole chain
        MaxLevel = 1;
        BuildTime = 1.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 220f; Health = MaxHealth; }

    protected override void BuildableTick()
    {
        // gentle glow handled by the model; nothing per-frame needed here
    }

    // ---- oil network helpers ----

    static float lastFlood = -999f;

    /// <summary>Recompute which pipes are connected (flood out from captured refineries).
    /// Throttled so repeated SupplySource calls in the same frame are cheap.</summary>
    public static void Flood()
    {
        if (Time.time - lastFlood < 0.25f) return;
        lastFlood = Time.time;

        foreach (var p in All) if (p != null) p.Live = false;
        var q = new Queue<OilPipe>();
        foreach (var p in All)
            if (p != null && !p.Building && NearCaptured(p.transform.position, Link) != null) { p.Live = true; q.Enqueue(p); }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var o in All)
                if (o != null && !o.Live && !o.Building &&
                    (o.transform.position - p.transform.position).sqrMagnitude <= Link * Link)
                { o.Live = true; q.Enqueue(o); }
        }
    }

    /// <summary>The captured refinery feeding the given position, if any — directly within Link,
    /// or via a live pipe within Link. Returns null when nothing connects (no supply).</summary>
    public static Refinery SupplySource(Vector3 pos)
    {
        var direct = NearCaptured(pos, Link);
        if (direct != null) return direct;
        Flood();
        foreach (var p in All)
            if (p != null && p.Live && (p.transform.position - pos).sqrMagnitude <= Link * Link)
                return NearCaptured(pos, 99999f); // connected — draw from the nearest captured НПЗ
        return null;
    }

    static Refinery NearCaptured(Vector3 pos, float range)
    {
        Refinery best = null; float bestSq = range * range;
        foreach (var r in Refinery.All)
        {
            if (r == null || !r.Captured) continue;
            float d = (r.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = r; }
        }
        return best;
    }
}
