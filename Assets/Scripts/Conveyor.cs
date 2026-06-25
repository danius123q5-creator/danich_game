using System.Collections.Generic;
using UnityEngine;

/// <summary>Conveyor (ЭКОНОМИКА) — a relay that carries ore from a captured mine toward a metal
/// vat. Live when it links (directly or through other conveyors) to a captured mine. Drag-built
/// like the oil pipe: hold LMB at the mine, walk to base, release to lay the run. Metal twin of OilPipe.</summary>
public class Conveyor : Buildable
{
    public static readonly List<Conveyor> All = new List<Conveyor>();
    public const float Link = 16f;

    public bool Live { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetConveyors() => All.Clear();

    protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
    protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

    protected override void Awake()
    {
        BuildCost = 15;   // per segment — a drag lays a whole chain
        MaxLevel = 1;
        BuildTime = 1.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 240f; Health = MaxHealth; }

    static float lastFlood = -999f;

    public static void Flood()
    {
        if (Time.time - lastFlood < 0.25f) return;
        lastFlood = Time.time;

        foreach (var c in All) if (c != null) c.Live = false;
        var q = new Queue<Conveyor>();
        foreach (var c in All)
            if (c != null && !c.Building && NearSource(c.transform.position, Link) != null) { c.Live = true; q.Enqueue(c); }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var o in All)
                if (o != null && !o.Live && !o.Building &&
                    (o.transform.position - c.transform.position).sqrMagnitude <= Link * Link)
                { o.Live = true; q.Enqueue(o); }
        }
    }

    /// <summary>The captured mine feeding the given position — directly within Link, or via a
    /// live conveyor within Link. Null when nothing connects.</summary>
    public static IMetalSource SupplySource(Vector3 pos)
    {
        var direct = NearSource(pos, Link);
        if (direct != null) return direct;
        Flood();
        foreach (var c in All)
            if (c != null && c.Live && (c.transform.position - pos).sqrMagnitude <= Link * Link)
                return NearSource(pos, 99999f);
        return null;
    }

    static IMetalSource NearSource(Vector3 pos, float range)
    {
        IMetalSource best = null; float bestSq = range * range;
        foreach (var s in MetalSources.All)
        {
            if (s == null || !s.MetalActive || s.MetalTransform == null) continue;
            float d = (s.MetalTransform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = s; }
        }
        return best;
    }
}
