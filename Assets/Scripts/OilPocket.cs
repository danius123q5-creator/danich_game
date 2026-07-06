using UnityEngine;

/// <summary>Oil pocket (НЕФТ. КАРМАН) — a reserve tank that raises your PERSONAL oil capacity by
/// 365 while it stands, so you can stockpile more oil for the super-weapons. Build several to stack
/// the bonus; if one is destroyed or sold, that capacity is lost again.</summary>
public class OilPocket : Buildable
{
    public const int Capacity = 365;
    bool applied;

    protected override void Awake()
    {
        BuildCost = 200;
        MaxLevel = 1;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 320f; Health = MaxHealth; }

    // Bonus applies once the pocket finishes building, and is removed on death / sell / teardown.
    protected override void OnActivated() { Apply(true); }
    protected override void OnDeath() { Apply(false); base.OnDeath(); }
    protected override void OnDisable() { base.OnDisable(); Apply(false); }

    void Apply(bool on)
    {
        if (on == applied) return;
        applied = on;
        PlayerController.ExtraOilCap = Mathf.Max(0, PlayerController.ExtraOilCap + (on ? Capacity : -Capacity));
    }
}
