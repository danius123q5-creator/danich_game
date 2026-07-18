using UnityEngine;

/// <summary>A cheap, high-HP barrier for walling off your base. Levels 1-3 add HP.</summary>
public class Wall : Buildable
{
    protected override void Awake()
    {
        BuildCost = 25;
        MaxLevel = 3;
        BuildTime = 1.0f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 550f; break;   // 3.1.1: walls tankier (was 300/500/800)
            case 2: MaxHealth = 950f; break;
            default: MaxHealth = 1500f; break;
        }
        MaxHealth *= ModRuntime.WallHpMult; // 3.2: mod multiplier
        Health = MaxHealth;
    }
}
