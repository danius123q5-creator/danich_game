using UnityEngine;

/// <summary>An explosive lobbed by a grenadier zombie. Arcs toward the target point and
/// blasts on arrival, hurting the player and buildings caught in the radius.</summary>
public class Grenade : MonoBehaviour
{
    const float Duration = 1.3f;   // flight time
    const float ArcHeight = 4f;
    const float BlastRadius = 3.5f;
    const float Damage = 25f;

    Vector3 start, target;
    float t;
    bool claimed; // an AA turret has already rolled to intercept this grenade

    /// <summary>An AA turret claims the right to evaluate this grenade. Returns true only
    /// for the first turret, so the 50% intercept roll happens once (never stacks).</summary>
    public bool ClaimForIntercept()
    {
        if (claimed) return false;
        claimed = true;
        return true;
    }

    /// <summary>Knocked out of the sky — pops harmlessly, deals no damage.</summary>
    public void ShootDown()
    {
        Effects.Burst(transform.position, new Color(1f, 0.9f, 0.4f), 10);
        Destroy(gameObject);
    }

    public static void Launch(Vector3 from, Vector3 to)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;
        go.transform.localScale = Vector3.one * 0.32f;
        GameBootstrap.SetColor(go, new Color(0.14f, 0.17f, 0.12f));
        var g = go.AddComponent<Grenade>();
        g.start = from;
        g.target = to;
    }

    void Update()
    {
        t += Time.deltaTime;
        float f = Mathf.Clamp01(t / Duration);
        Vector3 p = Vector3.Lerp(start, target, f);
        p.y += Mathf.Sin(f * Mathf.PI) * ArcHeight; // simple parabola
        transform.position = p;
        if (f >= 1f) Explode();
    }

    void Explode()
    {
        Effects.Explosion(transform.position);
        float rSq = BlastRadius * BlastRadius;

        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null && !player.IsDead &&
            (player.transform.position - transform.position).sqrMagnitude < rSq)
            player.TakeDamage(Damage);

        foreach (var b in Object.FindObjectsByType<Buildable>(FindObjectsSortMode.None))
            if ((b.transform.position - transform.position).sqrMagnitude < rSq) b.TakeDamage(Damage);

        Destroy(gameObject);
    }
}
