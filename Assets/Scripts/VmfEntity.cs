using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A raw Hammer entity carried over from an imported .vmf so the game KNOWS it: its classname,
/// every key/value, and its I/O connections (outputs). This is the data foundation for map
/// scripting — a future runtime can walk VmfEntity.All, find entities by targetname/classname,
/// and fire their outputs (OnTrigger → target,input,param,delay) to drive doors, relays, triggers…
/// It does NOT implement each Source entity's behaviour; it makes them all queryable.
/// </summary>
public class VmfEntity : MonoBehaviour
{
    public string classname = "";
    public readonly Dictionary<string, string> kv = new Dictionary<string, string>();
    public readonly List<Connection> outputs = new List<Connection>();
    public Transform moveRoot;   // для движимых брашевых энтити (func_door/movelinear/button) — их СОБСТВЕННАЯ геометрия, которую рантайм двигает
    public float boundsRadius;   // радиус зоны энтити в Unity-юнитах (для проксимити дверей) — считается импортёром
    public Vector3 boundsCenter; // центр AABB зоны (Unity) — для точного бокс-теста триггеров
    public Vector3 boundsHalf;   // полуразмеры AABB зоны (Unity) — игрок внутри если |p-center|<half по всем осям

    public string Targetname => Get("targetname");
    public string Get(string key, string def = "") => kv.TryGetValue(key, out var v) ? v : def;

    /// <summary>A Source I/O link: when 'outputName' fires, send 'input' (+param, after delay) to 'target'.</summary>
    public struct Connection
    {
        public string outputName; // e.g. "OnTrigger", "OnPressed"
        public string target;     // targetname to fire at
        public string input;      // input to call, e.g. "Open", "Toggle"
        public string param;      // parameter (may be empty)
        public float delay;       // seconds
        public int times;         // fire limit; -1 = infinite
    }

    // ---- registry so scripts can look entities up ----
    public static readonly List<VmfEntity> All = new List<VmfEntity>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>All entities of a classname (e.g. "func_door").</summary>
    public static IEnumerable<VmfEntity> OfClass(string cls)
    {
        foreach (var e in All) if (e != null && e.classname == cls) yield return e;
    }
    /// <summary>Entity by targetname (first match), or null.</summary>
    public static VmfEntity ByName(string name)
    {
        foreach (var e in All) if (e != null && e.Get("targetname") == name) return e;
        return null;
    }
}
