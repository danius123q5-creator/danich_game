using UnityEngine;

/// <summary>A plain vertical ladder — climb straight up/down (W/S) to reach wall
/// tops and bridge decks. Unlike the ramp (Stairs), it stands upright and is
/// climbed, not walked up. Its only collider is a trigger "climb zone" the player
/// detects to enter climb mode (see PlayerController.NearbyLadder).</summary>
public class Ladder : Buildable
{
    public const float Height = 4.0f; // tall enough to reach tall walls / bridge decks

    protected override void Awake()
    {
        BuildCost = 30;
        MaxLevel = 1;
        BuildTime = 1.0f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 250f;
        Health = MaxHealth;
    }
}
