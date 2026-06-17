using UnityEngine;

/// <summary>Coiled barbed wire: a cheap, passable obstacle that slows and slowly
/// shreds any zombie crossing it. Like the mines it is a trap (its collider is a
/// trigger, so zombies walk through instead of attacking it) — but instead of
/// detonating once, it persists and keeps catching zombies. Levels 1-3 increase
/// reach, damage and the slow it inflicts.</summary>
public class BarbedWire : Buildable
{
    float radius = 1.6f;   // how far the coils reach (XZ)
    float dps = 14f;       // damage per second to zombies caught inside
    float slowMul = 0.5f;  // movement multiplier while caught (lower = slower)

    public override bool IsTrap => true; // zombies ignore it and walk through

    protected override void Awake()
    {
        BuildCost = 35;
        MaxLevel = 3;
        BuildTime = 1.2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 200f; dps = 18f; slowMul = 0.30f; radius = 2.6f; break;
            case 2: MaxHealth = 320f; dps = 30f; slowMul = 0.22f; radius = 3.0f; break;
            default: MaxHealth = 480f; dps = 45f; slowMul = 0.15f; radius = 3.4f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        float rSq = radius * radius;
        foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            Vector3 d = z.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= rSq)
            {
                z.Slow(slowMul, 0.5f);
                z.TakeDamage(dps * Time.deltaTime);
            }
        }
    }
}
