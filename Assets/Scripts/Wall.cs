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
            case 1: MaxHealth = 300f; break;
            case 2: MaxHealth = 500f; break;
            default: MaxHealth = 800f; break;
        }
        Health = MaxHealth;
    }
}
