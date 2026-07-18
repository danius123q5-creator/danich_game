using System.Collections.Generic;
using UnityEngine;

/// <summary>Oil hub (НЕФТ. ХАБ) — a junction that pools oil from SEVERAL sources at once. Run pipes
/// to it from multiple captured НПЗ / oil derricks and it draws from ALL of them into one tank, then
/// acts as a single combined oil source on its output — connect a pipe from the hub to your doser and
/// you get the summed oil of every wired source. (Oil twin of the metal vat's "draw from all mines".)</summary>
public class OilHub : Buildable, IOilSource
{
    const float TankCap = 500f;
    const float PullRate = 60f;   // oil/sec pulled from EACH wired source into the tank
    const float Radius = 6f;      // auto-dispense radius
    const float Tick = 0.5f;      // seconds between hand-outs
    const int GiveAmount = 45;    // base oil per hand-out (scaled by connected pipe count)
    float stock;
    float nextScan, nextGive;
    readonly List<IOilSource> sources = new List<IOilSource>();

    // Register as an oil source so downstream pipes/dosers can draw the pooled oil from us.
    protected override void OnEnable() { base.OnEnable(); OilSources.Add(this); }
    protected override void OnDisable() { base.OnDisable(); OilSources.Remove(this); }

    protected override void Awake()
    {
        BuildCost = 180;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 300f; Health = MaxHealth; }

    public float TankFrac => stock / TankCap; // for any future HUD
    public bool Supplied { get; private set; }

    protected override void BuildableTick()
    {
        // Pull oil from EVERY source wired to this hub (directly or down a chain of pipes).
        if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; OilPipe.CollectSources(transform.position, sources, this); }
        Supplied = sources.Count > 0;
        if (stock < TankCap)
            foreach (var s in sources)
            {
                if (s == null || !s.OilActive) continue;
                stock += s.DrawOil(Mathf.Min(PullRate * Time.deltaTime, TankCap - stock));
                if (stock >= TankCap) break;
            }

        // The hub is ALSO a high-throughput dispenser: it hands oil to a nearby player at a rate
        // that scales with how many pipes feed it (+10%/pipe) — so 5 pipes clearly beats 2-3.
        if (Time.time < nextGive) return;
        nextGive = Time.time + Tick;
        if (stock < 1f) return;
        float pipeBoost = 1f + Mathf.Min(OilPipe.LiveCount(), 25) * 0.10f;
        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).sqrMagnitude > Radius * Radius) continue;
            int give = Mathf.Min(Mathf.RoundToInt(GiveAmount * pipeBoost), Mathf.FloorToInt(stock));
            give = Mathf.Min(give, PlayerController.OilMax - p.Oil);
            if (give > 0) { p.AddOil(give); stock -= give; Effects.Burst(transform.position + Vector3.up * 1.6f, new Color(1f, 0.8f, 0.3f), 3); }
        }
    }

    // IOilSource: hand the pooled oil to whatever draws from the hub (a doser, or another pipe run).
    public bool OilActive => !Building && stock > 0f;
    public Transform OilTransform => transform;
    public float DrawOil(float amount)
    {
        if (stock <= 0f || amount <= 0f) return 0f;
        float take = Mathf.Min(amount, stock);
        stock -= take;
        return take;
    }
}
