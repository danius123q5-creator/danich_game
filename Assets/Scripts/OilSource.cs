using System.Collections.Generic;
using UnityEngine;

/// <summary>Anything the oil pipe network can draw oil from: a captured refinery (НПЗ) or a
/// player-built oil derrick (нефтяная вышка). The pipe/doser logic treats them uniformly.</summary>
public interface IOilSource
{
    bool OilActive { get; }            // currently yielding oil (НПЗ captured / derrick built)
    Transform OilTransform { get; }    // world position for range checks
    float DrawOil(float amount);       // remove up to 'amount' oil, return what was taken
}

/// <summary>Live registry of every oil source on the map (refineries + derricks).</summary>
public static class OilSources
{
    public static readonly List<IOilSource> All = new List<IOilSource>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => All.Clear();

    public static void Add(IOilSource s) { if (s != null && !All.Contains(s)) All.Add(s); }
    public static void Remove(IOilSource s) { All.Remove(s); }
}
