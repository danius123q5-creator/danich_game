using UnityEngine;

/// <summary>A visible rocket projectile: flies from the launcher to its target trailing
/// exhaust, then explodes with splash damage. Spawned by the RPG turret (host/offline).</summary>
public class Rocket : MonoBehaviour
{
    Vector3 target;
    float speed;
    float blastRadius, blastDamage;
    float life;
    float nextPuff;
    bool cosmetic;

    /// <summary>Client: a visual-only rocket mirroring the host's (no damage, no networked boom).</summary>
    public static void LaunchCosmetic(Vector3 start, Vector3 target)
    {
        var r = Spawn(start, target, 0f, 0f, 40f);
        r.cosmetic = true;
    }

    public static void Launch(Vector3 start, Vector3 target, float blastRadius, float blastDamage, float speed = 40f)
    {
        Spawn(start, target, blastRadius, blastDamage, speed);
        // Host: let clients see the rocket fly (the impact boom rides along via Effects.Explosion).
        var lan = LanManager.Instance;
        if (lan != null && lan.Active && lan.IsHost) lan.FxRocket(start, target);
    }

    static Rocket Spawn(Vector3 start, Vector3 target, float blastRadius, float blastDamage, float speed)
    {
        var go = new GameObject("Rocket");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = start;
        Vector3 dir = target - start;
        if (dir.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(dir);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // length along +Z
        body.transform.localScale = new Vector3(0.18f, 0.35f, 0.18f);
        GameBootstrap.SetColor(body, new Color(0.25f, 0.25f, 0.28f));

        var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere); // warhead
        Object.Destroy(tip.GetComponent<Collider>());
        tip.transform.SetParent(go.transform, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.36f);
        tip.transform.localScale = Vector3.one * 0.22f;
        GameBootstrap.SetColor(tip, new Color(0.7f, 0.25f, 0.2f));

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere); // exhaust glow
        Object.Destroy(flame.GetComponent<Collider>());
        flame.transform.SetParent(go.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0f, -0.36f);
        flame.transform.localScale = Vector3.one * 0.3f;
        GameBootstrap.SetColor(flame, new Color(1f, 0.7f, 0.2f));

        var r = go.AddComponent<Rocket>();
        r.target = target; r.speed = speed; r.blastRadius = blastRadius; r.blastDamage = blastDamage;
        return r;
    }

    void Update()
    {
        life += Time.deltaTime;
        Vector3 to = target - transform.position;
        float step = speed * Time.deltaTime;
        if (to.magnitude <= step || life > 3f) { Explode(); return; }

        transform.position += to.normalized * step;
        transform.rotation = Quaternion.LookRotation(to);

        if (Time.time >= nextPuff) // smoke trail
        {
            nextPuff = Time.time + 0.02f;
            Effects.Burst(transform.position - transform.forward * 0.4f, new Color(0.55f, 0.55f, 0.55f), 2);
        }
    }

    void Explode()
    {
        // Cosmetic (client) rockets don't boom or damage — the host's networked explosion handles that.
        if (!cosmetic)
        {
            Effects.Explosion(transform.position);
            float rSq = blastRadius * blastRadius;
            foreach (var z in Zombie.All)
                if ((z.transform.position - transform.position).sqrMagnitude < rSq) z.TakeDamage(blastDamage);
        }
        Destroy(gameObject);
    }
}
