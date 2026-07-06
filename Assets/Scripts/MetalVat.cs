using System.Collections.Generic;
using UnityEngine;

/// <summary>Metal vat (ЧАН ДЛЯ РУДЫ) — pulls ore from a connected mine (directly or down a line
/// of conveyors) into its tank, then automatically tops up any player standing in its radius.
/// Build a ШАХТА → конвейер → чан chain and metal flows to you hands-free. Metal twin of OilDispenser.</summary>
public class MetalVat : Buildable
{
    const float Radius = 5f;
    const float Tick = 0.5f;
    const int GiveAmount = 40;     // metal per hand-out
    const float PullRate = 45f;    // ore/sec drawn from the network into the tank
    const float TankCap = 400f;

    float stock;
    float nextGive;
    float nextScan;
    readonly List<IMetalSource> sources = new List<IMetalSource>(); // every mine wired to this vat

    protected override void Awake()
    {
        BuildCost = 200;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 300f; Health = MaxHealth; }

    public bool Supplied { get; private set; }

    // 2.3: from wave 15 on, the vat gets faster every wave (+10%/wave) — pulls & hands out more metal.
    static float WaveBoost()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.WaveNumber > 15) return 1f + (gm.WaveNumber - 15) * 0.1f;
        return 1f;
    }

    protected override void BuildableTick()
    {
        // Throughput scales with the wave AND with how many conveyors feed the vat (+8%/conveyor,
        // capped) — so wiring more conveyors visibly speeds up metal income.
        float boost = WaveBoost() * (1f + Mathf.Min(Conveyor.LiveCount(), 25) * 0.08f);
        // Draw from EVERY captured mine wired to this vat (more mines = more metal/sec).
        if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; Conveyor.CollectSources(transform.position, sources); }
        Supplied = sources.Count > 0;
        if (stock < TankCap)
            foreach (var s in sources)
            {
                if (s == null || !s.MetalActive) continue;
                stock += s.DrawMetal(Mathf.Min(PullRate * boost * Time.deltaTime, TankCap - stock));
                if (stock >= TankCap) break;
            }

        if (Time.time < nextGive) return;
        nextGive = Time.time + Tick;
        if (stock < 1f) return;

        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).sqrMagnitude > Radius * Radius) continue;
            int give = Mathf.Min(Mathf.RoundToInt(GiveAmount * boost), Mathf.FloorToInt(stock));
            give = Mathf.Min(give, PlayerController.MetalMax - p.Metal); // don't waste on a full wallet
            if (give > 0) { p.AddMetal(give); stock -= give; Effects.Burst(transform.position + Vector3.up * 1.4f, new Color(0.7f, 0.8f, 1f), 3); }
        }
    }
}
