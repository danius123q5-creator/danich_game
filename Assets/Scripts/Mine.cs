using UnityEngine;

/// <summary>Tripwire mine. Ported from sent_engi_mine.lua: TWO charges joined by
/// a wire; a zombie crossing it detonates a blast at both ends. Levels 1-2.</summary>
public class Mine : Buildable
{
    float wireLength = 8f;
    float tripDist = 1.4f;
    float blastDamage = 45f;
    float blastRadius = 6f;
    bool exploded;

    Vector3 wireEnd;
    LineRenderer line;
    GameObject anchor; // the second charge at the far end of the wire

    public override bool IsTrap => true;

    protected override void Awake()
    {
        BuildCost = 60;
        MaxLevel = 2;
        BuildTime = 1.5f; // arming delay
        base.Awake();

        line = gameObject.AddComponent<LineRenderer>();
        line.startWidth = 0.05f;
        line.endWidth = 0.05f;
        line.material = new Material(GameBootstrap.LineShader());
        line.startColor = Color.red;
        line.endColor = Color.red;
        line.enabled = false;
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 2))
        {
            case 1: MaxHealth = 80f; blastDamage = 45f; blastRadius = 6f; break;
            default: MaxHealth = 120f; blastDamage = 90f; blastRadius = 8.5f; break;
        }
        Health = MaxHealth;
    }

    protected override void OnActivated()
    {
        wireEnd = transform.position + transform.forward * wireLength;
        wireEnd.y = GameBootstrap.Hill(wireEnd.x, wireEnd.z); // sit the far charge on the terrain slope

        // Second charge (anchor drum) at the far end.
        if (anchor == null)
        {
            anchor = Models.BuildMine(Mathf.Clamp(Level, 1, 2));
            anchor.transform.position = wireEnd;
            anchor.transform.rotation = transform.rotation;
        }

        if (line != null)
        {
            line.enabled = true;
            line.SetPosition(0, transform.position + Vector3.up * 0.2f);
            line.SetPosition(1, wireEnd + Vector3.up * 0.2f);
        }
    }

    protected override void BuildableTick()
    {
        if (exploded) return;
        foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            if (DistToSegment2D(z.transform.position, transform.position, wireEnd) < tripDist)
            {
                Detonate();
                return;
            }
        }
    }

    void Detonate()
    {
        if (exploded) return;
        exploded = true;

        Vector3[] ends = { transform.position, wireEnd };
        foreach (var p in ends)
        {
            Effects.Explosion(p + Vector3.up * 0.4f); // spark burst + boom at each charge
            foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
            {
                if ((z.transform.position - p).magnitude < blastRadius)
                {
                    Effects.Explosion(z.transform.position + Vector3.up * 0.6f); // the zombie pops
                    z.TakeDamage(999999f);                                       // mines kill instantly
                }
            }
        }
        if (anchor != null) Destroy(anchor);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (anchor != null) Destroy(anchor);
    }

    static float DistToSegment2D(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 p2 = new Vector2(p.x, p.z);
        Vector2 a2 = new Vector2(a.x, a.z);
        Vector2 b2 = new Vector2(b.x, b.z);
        Vector2 ab = b2 - a2;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / len2) : 0f;
        return Vector2.Distance(p2, a2 + ab * t);
    }
}
