using UnityEngine;

/// <summary>A walkable ramp ("ladder") — climb it to reach wall tops or the bridge.</summary>
public class Stairs : Buildable
{
    protected override void Awake()
    {
        BuildCost = 30;
        MaxLevel = 1;
        BuildTime = 1.0f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 300f;
        Health = MaxHealth;
    }
}
