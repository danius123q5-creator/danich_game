using UnityEngine;

/// <summary>A flat "pancake" proximity landmine: detonates when a zombie steps near it.</summary>
public class ProxyMine : Buildable
{
    float triggerRadius = 1.8f;
    float blastRadius = 5f;
    bool exploded;

    public override bool IsTrap => true;

    protected override void Awake()
    {
        BuildCost = 8;   // dirt-cheap one-shot consumable
        MaxLevel = 1;
        BuildTime = 1.0f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        MaxHealth = 60f;
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (exploded) return;
        foreach (var z in Zombie.All)
        {
            Vector3 d = z.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude < triggerRadius) { Detonate(); return; }
        }
    }

    void Detonate()
    {
        if (exploded) return;
        exploded = true;
        Effects.Explosion(transform.position + Vector3.up * 0.3f);
        foreach (var z in Zombie.All)
        {
            if ((z.transform.position - transform.position).magnitude < blastRadius)
            {
                Effects.Explosion(z.transform.position + Vector3.up * 0.6f); // the zombie pops
                z.TakeDamage(999999f);                                       // mines kill instantly
            }
        }
        Destroy(gameObject);
    }
}
