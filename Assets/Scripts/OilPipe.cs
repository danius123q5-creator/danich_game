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
            if (p != null && !p.Building && NearSource(p.transform.position, Link) != null) { p.Live = true; q.Enqueue(p); }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var o in All)
                if (o != null && !o.Live && !o.Building &&
                    (o.transform.position - p.transform.position).sqrMagnitude <= Link * Link)
                { o.Live = true; q.Enqueue(o); }
        }
    }

    /// <summary>The oil source (НПЗ or derrick) feeding the given position, if any — directly
    /// within Link, or via a live pipe within Link. Returns null when nothing connects.</summary>
    public static IOilSource SupplySource(Vector3 pos)
    {
        var direct = NearSource(pos, Link);
        if (direct != null) return direct;
        Flood();
        foreach (var p in All)
            if (p != null && p.Live && (p.transform.position - pos).sqrMagnitude <= Link * Link)
                return NearSource(pos, 99999f); // connected — draw from the nearest active source
        return null;
    }

    /// <summary>How many pipes are currently live (connected to a captured source). The oil doser
    /// scales its throughput with this — more wired pipes = more oil/sec.</summary>
    public static int LiveCount()
    {
        Flood();
        int n = 0;
        foreach (var p in All) if (p != null && p.Live) n++;
        return n;
    }

    static IOilSource NearSource(Vector3 pos, float range)
    {
        IOilSource best = null; float bestSq = range * range;
        foreach (var s in OilSources.All)
        {
            if (s == null || !s.OilActive || s.OilTransform == null) continue;
            float d = (s.OilTransform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = s; }
        }
        return best;
    }

    // ---- collect EVERY oil source wired to a position (the oil hub draws from all of them) ----
    static readonly HashSet<OilPipe> _visited = new HashSet<OilPipe>();
    static readonly Queue<OilPipe> _bfs = new Queue<OilPipe>();

    /// <summary>Fill 'into' with every active oil source (НПЗ / derrick — NOT hubs) reachable from
    /// 'pos', directly within Link or through a connected chain of pipes. Used by the oil hub to
    /// pool oil from several sources at once.</summary>
    public static void CollectSources(Vector3 pos, List<IOilSource> into)
    {
        into.Clear();
        _visited.Clear(); _bfs.Clear();

        foreach (var p in All)
            if (p != null && !p.Building && (p.transform.position - pos).sqrMagnitude <= Link * Link && _visited.Add(p))
                _bfs.Enqueue(p);
        while (_bfs.Count > 0)
        {
            var p = _bfs.Dequeue();
            foreach (var o in All)
                if (o != null && !o.Building && !_visited.Contains(o) &&
                    (o.transform.position - p.transform.position).sqrMagnitude <= Link * Link)
                { _visited.Add(o); _bfs.Enqueue(o); }
        }

        foreach (var s in OilSources.All)
        {
            if (s == null || !s.OilActive || s.OilTransform == null) continue;
            if (s is OilHub) continue; // never pool a hub into another hub (avoids loops/double count)
            Vector3 sp = s.OilTransform.position;
            bool reach = (sp - pos).sqrMagnitude <= Link * Link;
            if (!reach)
                foreach (var p in _visited)
                    if ((p.transform.position - sp).sqrMagnitude <= Link * Link) { reach = true; break; }
            if (reach) into.Add(s);
        }
    }
}
