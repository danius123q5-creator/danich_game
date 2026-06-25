using UnityEngine;

/// <summary>Defense building — Spinning Blades (ЛЕЗВИЯ). A rotor of whirling blades that
/// shreds any zombie standing in its short radius, several times a second. Works like a
/// turret: once built it runs on its own, no metal upkeep or charging. Pricey to build (450).
/// Levels 1-3 add radius, damage and rotor speed.</summary>
public class BladeTrap : Buildable
{
    public override bool IsTrap => false; // it's a solid emplacement: zombies will hit it

    float radius = 3.0f;
    float damage = 18f;
    float rate = 0.2f;     // seconds between cutting ticks
    float spin = 760f;     // visual rotor speed (deg/s)
    float next;
    Transform rotor;       // spinning blade assembly inside the visual

    protected override void Awake()
    {
        BuildCost = 450;
        MaxLevel = 3;
        UpgradeCost = 300;
        BuildTime = 2.5f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 400f; damage = 18f; radius = 3.0f; rate = 0.20f; spin = 760f; break;
            case 2: MaxHealth = 520f; damage = 30f; radius = 3.6f; rate = 0.17f; spin = 920f; break;
            default: MaxHealth = 650f; damage = 46f; radius = 4.2f; rate = 0.14f; spin = 1100f; break;
        }
        Health = MaxHealth;
    }

    protected override void Update()
    {
        base.Update();
        // Keep the blades visually spinning whenever the trap is up (even between ticks).
        if (!Building && !IsPuppet)
        {
            if (rotor == null) rotor = FindRotor();
            if (rotor != null) rotor.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
        }
    }

    Transform FindRotor()
    {
        foreach (var t in GetComponentsInChildren<Transform>())
            if (t.name == "Rotor") return t;
        return null;
    }

    protected override void BuildableTick()
    {
        if (Time.time < next) return;
        next = Time.time + rate;

        float rSq = radius * radius;
        bool hitAny = false;
        Vector3 p = transform.position;
        foreach (var z in Zombie.All)
        {
            if (z == null) continue;
            if (GameRoot.IsZvZ && z.team == Team) continue; // never grind your own ZvZ horde
            if ((z.transform.position - p).sqrMagnitude > rSq) continue;
            z.TakeDamage(damage); // free upkeep — works like a turret
            hitAny = true;
        }
        if (hitAny) Effects.Burst(p + Vector3.up * 0.6f, new Color(1f, 0.85f, 0.3f), 6);
    }
}
