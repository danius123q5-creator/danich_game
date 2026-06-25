using UnityEngine;

/// <summary>Huge raised platform standing on 4 thick columns (ПЛАТФОРМА). A big open deck you
/// climb via the front ladder — room to plant several turrets and a whole firing line up top,
/// out of the zombies' reach. Lower and far wider than the watchtower.</summary>
public class BigPlatform : Buildable
{
    public const float Height = 10f;        // column height (deck height)
    public const float Half = 6.0f;         // platform half-size (12x12 — huge); model + colliders read this
    public const float Front = Half + 0.3f; // ladder/climb column sits just outside the front edge

    protected override void Awake()
    {
        BuildCost = 220;
        MaxLevel = 1;
        BuildTime = 4f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 1200f;
        Health = MaxHealth;
    }
}
