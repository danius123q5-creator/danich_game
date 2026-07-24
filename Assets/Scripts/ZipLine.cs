using System.Collections.Generic;
using UnityEngine;

/// <summary>Zip-line anchor (ЗИП-ЛАЙН) — a ground post you plant next to a WATCHTOWER or big
/// PLATFORM. It auto-strings a steel cable from the top of the nearest tall structure down to its
/// own spool. Once you're up on the tower, look near the cable head and press E — the player rides
/// the cable down fast (see PlayerController.BoardZip / RideZip), a quick escape from a high perch.</summary>
public class ZipLine : Buildable
{
    public static readonly List<ZipLine> All = new List<ZipLine>();

    const float SearchRange = 42f;   // horizontal range to find a tower to string a cable to
    const float BoardRange = 5.5f;   // how close to the TOP anchor you press E to board
    public const float RideSpeed = 22f; // m/s down the cable ("едет по тросу быстро")

    Vector3 topPoint, botPoint;      // world endpoints of the cable (top = tower, bottom = our spool)
    bool linked;                     // true once a tower is found in range
    Transform cable;                 // the drawn cable segment (lives under the world, not scaled)
    float nextScan;

    public bool Linked => linked;
    public Vector3 TopPoint => topPoint;
    public Vector3 BotPoint => botPoint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLines() => All.Clear();

    protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
    protected override void OnDisable()
    {
        base.OnDisable();
        All.Remove(this);
        if (cable != null) Destroy(cable.gameObject);
    }

    protected override void Awake()
    {
        BuildCost = 200;   // ~200 metal, as requested
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 260f; Health = MaxHealth; }

    protected override void BuildableTick()
    {
        if (Time.time >= nextScan) { nextScan = Time.time + 1f; Relink(); }
    }

    // Find the nearest tall structure (watchtower / big platform) whose top clearly rises above this
    // anchor, and string a cable from its top down to our spool.
    void Relink()
    {
        botPoint = transform.position + Vector3.up * 2.05f;   // our cable spool height
        Vector3 best = Vector3.zero; float bestSq = SearchRange * SearchRange; bool found = false;
        foreach (var b in Buildable.All)
        {
            if (b == null || b == this || b.Building || b.IsPuppet) continue;
            float topY;
            if (b.Type == 23) topY = 19.7f;                       // watchtower platform deck
            else if (b.Type == 26) topY = BigPlatform.Height - 0.3f; // big platform deck
            else continue;
            Vector3 tp = b.transform.position + Vector3.up * (topY + 0.25f);
            if (tp.y <= botPoint.y + 3f) continue;                // must be meaningfully above us
            Vector3 flat = tp - transform.position; flat.y = 0f;
            float d = flat.sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = tp; found = true; }
        }
        bool wasLinked = linked; Vector3 wasTop = topPoint;
        linked = found;
        if (found) topPoint = best;
        // Only redraw when the link actually changed (cheap 1 Hz check otherwise).
        if (linked != wasLinked || (linked && (wasTop - topPoint).sqrMagnitude > 0.01f)) DrawCable();
    }

    void DrawCable()
    {
        if (cable != null) { Destroy(cable.gameObject); cable = null; }
        if (!linked) return;
        var seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(seg.GetComponent<Collider>());              // visual only — no collision on the cable
        if (GameBootstrap.World != null) seg.transform.SetParent(GameBootstrap.World, true);
        Vector3 d = botPoint - topPoint; float len = d.magnitude;
        seg.transform.position = (topPoint + botPoint) * 0.5f;
        if (len > 0.001f) seg.transform.up = d.normalized;         // Unity cylinder's axis is local +Y
        seg.transform.localScale = new Vector3(0.07f, Mathf.Max(0.1f, len * 0.5f), 0.07f);
        GameBootstrap.SetColor(seg, new Color(0.09f, 0.09f, 0.10f));
        cable = seg.transform;
    }

    /// <summary>Called from PlayerController.Interact: if the player stands near a strung cable's TOP
    /// anchor, mount them onto it for the ride down. Returns true if a ride started.</summary>
    public static bool TryBoard(PlayerController p)
    {
        if (p == null) return false;
        Vector3 pos = p.transform.position;
        ZipLine best = null; float bestSq = BoardRange * BoardRange;
        foreach (var z in All)
        {
            if (z == null || !z.linked || z.Building || z.IsPuppet) continue;
            float d = (z.topPoint - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = z; }
        }
        if (best == null) return false;
        p.BoardZip(best.topPoint, best.botPoint);
        return true;
    }
}
