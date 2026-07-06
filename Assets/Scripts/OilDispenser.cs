using UnityEngine;

/// <summary>Oil doser (ЭКОНОМИКА) — pulls oil from a connected refinery (directly, or down a line
/// of pipes) into its own tank, then automatically tops up any player standing in its radius.
/// Build a НПЗ → труба → дозатор chain at your base and oil flows to you hands-free.</summary>
public class OilDispenser : Buildable
{
    const float Radius = 5f;       // auto-supply radius
    const float Tick = 0.5f;       // seconds between hand-outs
    const int GiveAmount = 25;     // oil per hand-out
    const float PullRate = 35f;    // oil/sec drawn from the network into the tank
    const float TankCap = 250f;

    float stock;
    float nextGive;

    protected override void Awake()
    {
        BuildCost = 150;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 260f; Health = MaxHealth; }

    public float TankFrac => stock / TankCap;          // for any future HUD
    public bool Supplied { get; private set; }

    protected override void BuildableTick()
    {
        // Draw oil from the connected source network (refinery or derrick) into the tank.
        // 2.3: more wired pipes = more efficient doser (+8%/pipe, capped) — pulls & hands out more oil.
        IOilSource src = OilPipe.SupplySource(transform.position);
        Supplied = src != null;
        float pipeBoost = 1f + Mathf.Min(OilPipe.LiveCount(), 25) * 0.08f;
        if (src != null && stock < TankCap)
            stock += src.DrawOil(Mathf.Min(PullRate * pipeBoost * Time.deltaTime, TankCap - stock));

        if (Time.time < nextGive) return;
        nextGive = Time.time + Tick;
        if (stock < 1f) return;

        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).sqrMagnitude > Radius * Radius) continue;
            int give = Mathf.Min(Mathf.RoundToInt(GiveAmount * pipeBoost), Mathf.FloorToInt(stock));
            give = Mathf.Min(give, PlayerController.OilMax - p.Oil); // don't waste on a full player
            if (give > 0) { p.AddOil(give); stock -= give; Effects.Burst(transform.position + Vector3.up * 1.4f, new Color(1f, 0.8f, 0.3f), 3); }
        }
    }
}
