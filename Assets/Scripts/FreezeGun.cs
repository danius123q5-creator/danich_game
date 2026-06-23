using UnityEngine;

/// <summary>Freeze tower (super weapon): every ~16s it emits a stasis wave that stops
/// EVERY zombie on the map dead for 10 seconds. Cheap to build (136), no metal upkeep.</summary>
public class FreezeGun : Buildable
{
    const float Interval = 16f;
    const float FreezeTime = 10f;
    float next;

    protected override void Awake()
    {
        BuildCost = 136;
        MaxLevel = 1;
        BuildTime = 2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 350f;
        Health = MaxHealth;
    }

    protected override void OnActivated() { next = Time.time + 3f; } // first pulse shortly after it's up

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        next = Time.time + Interval;
        foreach (var z in Zombie.All) if (z != null) z.Freeze(FreezeTime);
        Effects.FreezeWave(transform.position + Vector3.up * 1f);
    }
}
