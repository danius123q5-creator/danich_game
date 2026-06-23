using UnityEngine;

/// <summary>Heals players and hands out metal in a radius on a tick. Ported from
/// sent_engi_dispenser.lua. Levels 1-3 scale heal/metal/radius and speed up.</summary>
public class Dispenser : Buildable
{
    float heal = 8f;
    float radius = 6f;
    float tick = 0.5f;
    int metalGive = 12;
    int ammoGive = 6;
    float nextHeal;

    // Hardcore: the dispenser can't print infinite metal — it slowly STOCKPILES metal and
    // only hands out what it has accumulated. Camp it dry and you wait for it to refill.
    float stock;
    float accrueRate = 10f; // metal/sec it accumulates
    float stockCap = 150f;  // most it can hold

    protected override void Awake()
    {
        BuildCost = 100;
        MaxLevel = 3;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 120f; heal = 8f; metalGive = 12; ammoGive = 6; radius = 2.5f; tick = 0.50f; accrueRate = 10f; stockCap = 150f; break;
            case 2: MaxHealth = 160f; heal = 16f; metalGive = 26; ammoGive = 12; radius = 3.5f; tick = 0.32f; accrueRate = 22f; stockCap = 300f; break;
            default: MaxHealth = 200f; heal = 28f; metalGive = 48; ammoGive = 20; radius = 4.5f; tick = 0.20f; accrueRate = 40f; stockCap = 500f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        bool limited = GameRoot.Hardcore; // hardcore: hand out only what's been stockpiled
        if (limited) stock = Mathf.Min(stockCap, stock + accrueRate * Time.deltaTime);

        if (Time.time < nextHeal) return;
        nextHeal = Time.time + tick;

        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).magnitude <= radius)
            {
                p.Heal(heal);
                int give = metalGive;
                if (limited) { give = Mathf.Min(metalGive, Mathf.FloorToInt(stock)); stock -= give; }
                if (give > 0) p.AddMetal(give);
                p.AddAmmo(ammoGive);
            }
        }
    }
}
