using UnityEngine;

/// <summary>A raised walkable deck (a wall turned 90°) for crossing the river.</summary>
public class Bridge : Buildable
{
    protected override void Awake()
    {
        BuildCost = 35;
        MaxLevel = 1;     // no upgrade — it's just a crossing
        BuildTime = 1.0f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 400f;
        Health = MaxHealth;
    }
}
