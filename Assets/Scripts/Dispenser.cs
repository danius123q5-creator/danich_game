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
            case 1: MaxHealth = 120f; heal = 8f; metalGive = 12; ammoGive = 6; radius = 2.5f; tick = 0.50f; break;
            case 2: MaxHealth = 160f; heal = 16f; metalGive = 26; ammoGive = 12; radius = 3.5f; tick = 0.32f; break;
            default: MaxHealth = 200f; heal = 28f; metalGive = 48; ammoGive = 20; radius = 4.5f; tick = 0.20f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time < nextHeal) return;
        nextHeal = Time.time + tick;

        foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.IsDead) continue;
            if ((p.transform.position - transform.position).magnitude <= radius)
            {
                p.Heal(heal);
                p.AddMetal(metalGive);
                p.AddAmmo(ammoGive);
            }
        }
    }
}
