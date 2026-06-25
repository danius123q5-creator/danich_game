using UnityEngine;

/// <summary>A ballistic missile: a cylinder that rises straight up from the silo, then plummets
/// down onto the target crowd and detonates with a big blast that wipes everything in radius.
/// Spawned by the missile silo.</summary>
public class BallisticMissile : MonoBehaviour
{
    Vector3 target;     // ground impact point (centre of the crowd)
    float apexY;        // top of the climb
    float blastR;
    bool descending;
    const float UpSpeed = 40f, DownSpeed = 70f;
    float nextPuff, life;

    public static void Launch(Vector3 from, Vector3 target, float blastR)
    {
        var go = new GameObject("BallisticMissile");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // the rocket cylinder
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.45f, 1.2f, 0.45f);   // upright (length along Y)
        GameBootstrap.SetColor(body, new Color(0.3f, 0.3f, 0.33f));

        var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);     // warhead nose
        Object.Destroy(tip.GetComponent<Collider>());
        tip.transform.SetParent(go.transform, false);
        tip.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        tip.transform.localScale = Vector3.one * 0.55f;
        GameBootstrap.SetColor(tip, new Color(0.8f, 0.25f, 0.2f));

        var fin = GameObject.CreatePrimitive(PrimitiveType.Sphere);     // exhaust glow at the tail
        Object.Destroy(fin.GetComponent<Collider>());
        fin.transform.SetParent(go.transform, false);
        fin.transform.localPosition = new Vector3(0f, -1.2f, 0f);
        fin.transform.localScale = Vector3.one * 0.6f;
        GameBootstrap.SetColor(fin, new Color(1f, 0.7f, 0.2f));

        var m = go.AddComponent<BallisticMissile>();
        m.target = target;
        m.blastR = blastR;
        m.apexY = Mathf.Max(from.y, target.y) + 48f; // climb high before the dive
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life += dt;

        if (!descending)
        {
            transform.position += Vector3.up * UpSpeed * dt;
            transform.rotation = Quaternion.identity; // nose up
            if (transform.position.y >= apexY)
            {
                descending = true;
                transform.position = new Vector3(target.x, apexY, target.z); // line up over the crowd
            }
        }
        else
        {
            transform.rotation = Quaternion.Euler(180f, 0f, 0f); // nose down for the dive
            float step = DownSpeed * dt;
            if (transform.position.y - target.y <= step || life > 8f) { Explode(); return; }
            transform.position += Vector3.down * step;
        }

        if (Time.time >= nextPuff)
        {
            nextPuff = Time.time + 0.02f;
            Effects.Burst(transform.position, new Color(0.6f, 0.6f, 0.6f), 2);
        }
    }

    void Explode()
    {
        Effects.Explosion(transform.position);
        Effects.AirBlast(transform.position + Vector3.up * 1f, blastR * 1.6f); // big shockwave
        float rSq = blastR * blastR;
        foreach (var z in Zombie.All)
            if (z != null && (z.transform.position - transform.position).sqrMagnitude < rSq)
                z.TakeDamage(99999f); // everything in the blast dies
        Destroy(gameObject);
    }
}
