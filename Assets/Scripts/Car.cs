using UnityEngine;

/// <summary>
/// A drivable car. Placed from the build menu ("ОСТАЛЬНОЕ"). Walk up, press E to get in,
/// drive with WASD, press F to get out. The PlayerController calls Drive() each frame
/// while occupied; the car just turns and rolls along the terrain. Zombies ignore it
/// (IsTrap) so your ride doesn't get chewed up.
/// </summary>
public class Car : Buildable
{
    public override bool IsTrap => true; // zombies don't path to / attack the car

    const float MaxSpeed = 22f;
    const float Accel = 24f;
    const float TurnRate = 80f;
    const float RideHeight = 0.55f;

    float curSpeed;
    bool occupied;

    protected override void Awake()
    {
        BuildCost = 150;
        MaxLevel = 1;
        BuildTime = 2f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 600f; Health = MaxHealth; }

    public bool Occupied => occupied;
    public void SetOccupied(bool o) { occupied = o; if (!o) curSpeed = 0f; }

    /// <summary>Driven by the PlayerController each frame: throttle (-1..1), steer (-1..1).</summary>
    public void Drive(float throttle, float steer)
    {
        curSpeed = Mathf.MoveTowards(curSpeed, throttle * MaxSpeed, Accel * Time.deltaTime);
        if (Mathf.Abs(curSpeed) > 0.3f)
            transform.Rotate(0f, steer * TurnRate * Time.deltaTime, 0f); // turn only while rolling

        Vector3 pos = transform.position + transform.forward * curSpeed * Time.deltaTime;
        float half = GameBootstrap.MapSize * 0.49f;
        pos.x = Mathf.Clamp(pos.x, -half, half);
        pos.z = Mathf.Clamp(pos.z, -half, half);
        pos.y = GameBootstrap.Hill(pos.x, pos.z) + RideHeight; // hug the ground
        transform.position = pos;
    }
}
