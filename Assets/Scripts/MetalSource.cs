using System.Collections.Generic;
using UnityEngine;

/// <summary>Anything the conveyor network can draw metal ore from — currently a captured mine
/// (шахта). Mirrors IOilSource so the metal logistics (conveyor → vat) reuse the same shape.</summary>
public interface IMetalSource
{
    bool MetalActive { get; }          // currently yielding ore (mine captured)
    Transform MetalTransform { get; }  // world position for range checks
    float DrawMetal(float amount);     // remove up to 'amount' ore, return what was taken
}

/// <summary>Live registry of every metal source on the map (mines).</summary>
public static class MetalSources
{
    public static readonly List<IMetalSource> All = new List<IMetalSource>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => All.Clear();

    public static void Add(IMetalSource s) { if (s != null && !All.Contains(s)) All.Add(s); }
    public static void Remove(IMetalSource s) { All.Remove(s); }
}
