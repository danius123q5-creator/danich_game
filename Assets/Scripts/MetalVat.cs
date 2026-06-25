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

    protected override void Awake()
    {
        BuildCost = 200;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 300f; Health = MaxHealth; }

    public bool Supplied { get; private set; }

    protected override void BuildableTick()
    {
        IMetalSource src = Conveyor.SupplySource(transform.position);
        Supplied = src != null;
        if (src != null && stock < TankCap)
            stock += src.DrawMetal(Mathf.Min(PullRate * Time.deltaTime, TankCap - stock));

        if (Time.time < nextGive) return;
        nextGive = Time.time + Tick;
        if (stock < 1f) return;

        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).sqrMagnitude > Radius * Radius) continue;
            int give = Mathf.Min(GiveAmount, Mathf.FloorToInt(stock));
            give = Mathf.Min(give, PlayerController.MetalMax - p.Metal); // don't waste on a full wallet
            if (give > 0) { p.AddMetal(give); stock -= give; Effects.Burst(transform.position + Vector3.up * 1.4f, new Color(0.7f, 0.8f, 1f), 3); }
        }
    }
}
