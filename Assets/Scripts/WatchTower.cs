using UnityEngine;

/// <summary>Tall (20 m) watchtower with a walkable top platform and a ladder up the front
/// you climb to reach it. A sniper/overwatch perch — zombies can't reach the top.</summary>
public class WatchTower : Buildable
{
    public const float Height = 20f;
    public const float Half = 2.6f;        // platform half-size (5.2x5.2 — roomier than before); model + colliders read this
    public const float Front = Half + 0.3f; // ladder/climb column sits just outside the front edge

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
