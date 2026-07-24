using System.Collections.Generic;
using UnityEngine;

/// <summary>Ore hub (РУДА-ХАБ) — the metal twin of the OilHub. A junction that pools ore from
/// SEVERAL captured mines/drills at once. Run conveyors to it from multiple ШАХТ / БУРОВЫХ and it
/// draws from ALL of them into one tank, then hands metal to a nearby player at a rate that scales
/// with how many conveyors feed it. (Same idea as the metal vat's "draw from all mines", but a
/// dedicated summing dispenser like the oil hub — one hub for a whole field of mines.)</summary>
public class OreHub : Buildable
{
    const float TankCap = 700f;
    const float PullRate = 80f;   // ore/sec pulled from the network into the tank
    const float Radius = 6f;      // auto-dispense radius
    const float Tick = 0.5f;      // seconds between hand-outs
    const int GiveAmount = 60;    // base metal per hand-out (scaled by connected conveyor count)
    float stock;
    float nextScan, nextGive;
    readonly List<IMetalSource> sources = new List<IMetalSource>();

    public bool Supplied { get; private set; }
    public float TankFrac => stock / TankCap; // for any future HUD

    protected override void Awake()
    {
        BuildCost = 250;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 320f; Health = MaxHealth; }

    // Late-game the hub ramps like the vat: faster every wave past 8, doubled in endless mode.
    static float WaveBoost()
    {
        var gm = GameManager.Instance;
        float wave = (gm != null && gm.WaveNumber > 8) ? 1f + (gm.WaveNumber - 8) * 0.15f : 1f;
        return wave * GameRoot.IncomeMult;
    }

    protected override void BuildableTick()
    {
        float boost = WaveBoost() * (1f + Mathf.Min(Conveyor.LiveCount(), 25) * 0.10f);

        // Pull ore from EVERY captured mine wired to this hub (directly or down a conveyor chain).
        if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; Conveyor.CollectSources(transform.position, sources); }
        Supplied = sources.Count > 0;
        if (stock < TankCap)
            foreach (var s in sources)
            {
                if (s == null || !s.MetalActive) continue;
                stock += s.DrawMetal(Mathf.Min(PullRate * boost * Time.deltaTime, TankCap - stock));
                if (stock >= TankCap) break;
            }

        // High-throughput dispenser: hand metal to a nearby player, faster with more conveyors.
        if (Time.time < nextGive) return;
        nextGive = Time.time + Tick;
        if (!Supplied || stock < 1f) return;
        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).sqrMagnitude > Radius * Radius) continue;
            int give = Mathf.Min(Mathf.RoundToInt(GiveAmount * boost), Mathf.FloorToInt(stock));
            give = Mathf.Min(give, PlayerController.MetalMax - p.Metal);
            if (give > 0) { p.AddMetal(give); stock -= give; Effects.Burst(transform.position + Vector3.up * 1.6f, new Color(0.7f, 0.8f, 1f), 3); }
        }
    }
}
