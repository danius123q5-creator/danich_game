using System.Collections.Generic;
using UnityEngine;

/// <summary>Heals players and hands out metal in a radius on a tick. Ported from
/// sent_engi_dispenser.lua. Levels 1-3 scale heal/metal/radius and speed up.</summary>
public class Dispenser : Buildable
{
    // Live-dispenser registry + "the base has been established" flag — lets GameManager reliably
    // declare defeat if the base's lifeline is ever fully gone (belt-and-suspenders for game-over).
    public static readonly List<Dispenser> All = new List<Dispenser>();
    public static bool BaseEstablished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() { All.Clear(); BaseEstablished = false; }
    public static int AliveCount() { int n = 0; foreach (var d in All) if (d != null) n++; return n; }

    protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
    protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

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

    // The single starter dispenser is the base's lifeline: if it's destroyed, the game is lost.
    bool critical;
    public bool Critical { get => critical; set { critical = value; if (value) BaseEstablished = true; } }

    protected override void Awake()
    {
        BuildCost = 100;
        MaxLevel = 3;
        base.Awake();
    }

    protected override void OnDeath()
    {
        // Endless mode: the base lifeline never truly dies — a fresh critical dispenser RESPAWNS in its
        // place, so a lost dispenser is a setback, not game-over. (Sandbox is immortal already.)
        if (Critical && GameRoot.Infinite && !GameRoot.IsZvZ && !GameRoot.IsPvp && !EndgameCinematic.Active)
        {
            RespawnCritical();
            base.OnDeath();
            return;
        }
        // Base lifeline destroyed → defeat. Suppressed during the evac finale, where the cinematic
        // deliberately levels every building (that's victory, not a base loss).
        if (Critical && !GameRoot.IsZvZ && !GameRoot.IsPvp && !EndgameCinematic.Active && !GameRoot.Sandbox)
            GameRoot.BaseLost = true;
        base.OnDeath();
    }

    // Endless: place a brand-new full-health critical dispenser where this one stood (or where the
    // player relocated it), so the base pops right back up instead of ending the run.
    void RespawnCritical()
    {
        var owner = Object.FindFirstObjectByType<PlayerController>();
        Vector3 p = transform.position;
        p.y = GameBootstrap.Hill(p.x, p.z);
        var go = Buildable.Create(Type, p, transform.rotation, owner);
        var nd = go != null ? go.GetComponent<Dispenser>() : null;
        if (nd != null) { nd.LoadState(1, 9999f, 0); nd.Critical = true; }
        Effects.Upgrade(p + Vector3.up * 1f);
        GameRoot.BaseLost = false; // make sure no stale defeat flag lingers
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            // 3.1.1: dispenser is far tankier, hands out more, over a bigger radius, faster.
            case 1: MaxHealth = 260f; heal = 12f; metalGive = 20; ammoGive = 10; radius = 4.0f; tick = 0.34f; accrueRate = 16f; stockCap = 260f; break;
            case 2: MaxHealth = 360f; heal = 22f; metalGive = 40; ammoGive = 18; radius = 5.5f; tick = 0.22f; accrueRate = 32f; stockCap = 480f; break;
            default: MaxHealth = 480f; heal = 36f; metalGive = 72; ammoGive = 30; radius = 7.0f; tick = 0.14f; accrueRate = 60f; stockCap = 800f; break;
        }
        MaxHealth *= ModRuntime.DispenserHpMult; // 3.2: mod multiplier
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
                int give = Mathf.RoundToInt(metalGive * GameRoot.IncomeMult); // endless mode: 2× metal
                if (limited) { give = Mathf.Min(give, Mathf.FloorToInt(stock)); stock -= give; }
                if (give > 0) p.AddMetal(give);
                p.AddAmmo(ammoGive);
            }
        }
    }
}
