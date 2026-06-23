using UnityEngine;

/// <summary>Tall (20 m) watchtower with a walkable top platform and a ladder up the front
/// you climb to reach it. A sniper/overwatch perch — zombies can't reach the top.</summary>
public class WatchTower : Buildable
{
    public const float Height = 20f;

    protected override void Awake()
    {
        BuildCost = 90;
        MaxLevel = 1;
        BuildTime = 3f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 600f;
        Health = MaxHealth;
    }
}
