using System.Collections.Generic;
using UnityEngine;

/// <summary>A ballistic missile: a cylinder that rises straight up from the silo, tips over at the
/// apex and plummets nose-down onto the target crowd, detonating with a big blast that wipes
/// everything in radius and leaves a scorched crater. Spawned by the missile silo.
///
/// 2.3: missiles RESERVE their target zombie — before a silo fires it skips any zombie already
/// claimed by an in-flight missile, so four rockets never dogpile the same zombie.</summary>
public class BallisticMissile : MonoBehaviour
{
    // ---- target reservation: "is this zombie taken?" ----
    static readonly HashSet<Zombie> Reserved = new HashSet<Zombie>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetReservations() { Reserved.Clear(); }
    /// <summary>True if some in-flight missile has already claimed this zombie.</summary>
    public static bool IsReserved(Zombie z) => z != null && Reserved.Contains(z);

    Zombie targetZombie;   // the claimed zombie (for reservation); may die mid-flight
    Vector3 target;        // ground impact point (centre of the crowd)
    Vector3 launch;        // where it lifted off from
    float blastR;
    float flightTime, arcHeight;
    float nextPuff, life;

    public static void Launch(Vector3 from, Vector3 target, float blastR, Zombie targetZombie = null)
    {
        var go = new GameObject("BallisticMissile");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // the rocket cylinder
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.45f, 1.2f, 0.45f);   // upright (length along Y)
        GameBootstrap.SetColor(body, new Color(0.3f, 0.3f, 0.33f));

        var tipObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);  // warhead nose
        Object.Destroy(tipObj.GetComponent<Collider>());
        tipObj.transform.SetParent(go.transform, false);
        tipObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        tipObj.transform.localScale = Vector3.one * 0.55f;
        GameBootstrap.SetColor(tipObj, new Color(0.8f, 0.25f, 0.2f));

        var fin = GameObject.CreatePrimitive(PrimitiveType.Sphere);     // exhaust glow at the tail
        Object.Destroy(fin.GetComponent<Collider>());
        fin.transform.SetParent(go.transform, false);
        fin.transform.localPosition = new Vector3(0f, -1.2f, 0f);
        fin.transform.localScale = Vector3.one * 0.6f;
        GameBootstrap.SetColor(fin, new Color(1f, 0.7f, 0.2f));

        var m = go.AddComponent<BallisticMissile>();
        m.launch = from;
        m.target = target;
        m.blastR = blastR;
        float dist = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(target.x, target.z));
        m.arcHeight = Mathf.Clamp(dist * 0.45f + 24f, 34f, 80f); // longer shots arc higher
        m.flightTime = Mathf.Clamp(dist / 34f + 1.2f, 1.8f, 4.5f);
        m.targetZombie = targetZombie;
        if (targetZombie != null) Reserved.Add(targetZombie);
    }

    void Update()
    {
        life += Time.deltaTime;
        // A single continuous ballistic arc from launch to target — always visible, no teleport.
        // Nose is oriented along the velocity, so it naturally tips over and dives as it descends.
        float t01 = Mathf.Clamp01(life / flightTime);
        Vector3 prev = transform.position;
        Vector3 basePos = Vector3.Lerp(launch, target, t01);          // straight line launch → target
        float arc = arcHeight * Mathf.Sin(t01 * Mathf.PI);            // parabolic bump peaking mid-flight
        Vector3 pos = new Vector3(basePos.x, basePos.y + arc, basePos.z);
        transform.position = pos;

        Vector3 vel = pos - prev;
        if (vel.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.FromToRotation(Vector3.up, vel.normalized); // nose (local +Y) along flight

        if (t01 >= 1f) { Explode(); return; }

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

        Release();
        Destroy(gameObject);
    }

    void OnDestroy() { Release(); }
    void Release() { if (targetZombie != null) { Reserved.Remove(targetZombie); targetZombie = null; } }
}
