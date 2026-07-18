using System.Collections.Generic;
using UnityEngine;

/// <summary>Enemy kamikaze drone: flies in from the map edge, dives onto one of YOUR buildings
/// (or the base) and detonates, damaging structures and the player. Shot down by the ЗЕНИТКА like
/// a bird. Spawned in drone raids by <see cref="GameManager"/>.</summary>
public class EnemyDrone : MonoBehaviour
{
    public static readonly List<EnemyDrone> All = new List<EnemyDrone>();

    Vector3 target;
    Transform targetT;           // a building to chase (may get destroyed), else a fixed point
    float life;
    const float Speed = 30f;
    const float Blast = 6f;
    const float Damage = 200f;   // to buildings caught in the blast

    // One interception roll per ЗЕНИТКА per drone (mirrors Bird) so several guns stack their chance.
    readonly HashSet<int> engagedBy = new HashSet<int>();
    public bool TryEngage(int gunId) => engagedBy.Add(gunId);

    public static void Spawn(Vector3 baseCentre)
    {
        var go = new GameObject("EnemyDrone");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        Vector2 edge = Random.insideUnitCircle.normalized * 95f;         // fly in from the edge
        go.transform.position = baseCentre + new Vector3(edge.x, 28f, edge.y);
        Models.BuildFpvDrone(go.transform);                              // reuse the quadcopter look
        var d = go.AddComponent<EnemyDrone>();
        d.PickTarget(baseCentre);
    }

    void PickTarget(Vector3 baseCentre)
    {
        var blds = Buildable.All;
        if (blds != null && blds.Count > 0)
        {
            var b = blds[Random.Range(0, blds.Count)];
            if (b != null) { targetT = b.transform; target = b.transform.position; return; }
        }
        target = baseCentre;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDestroy() { All.Remove(this); }

    void Update()
    {
        life += Time.deltaTime;
        if (targetT != null) target = targetT.position;
        Vector3 aim = target + Vector3.up * 0.6f;
        Vector3 to = aim - transform.position;
        float dist = to.magnitude;
        if (dist < 1.4f || life > 22f) { Detonate(); return; }

        Vector3 dir = to / dist;
        transform.position += dir * Speed * Time.deltaTime;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 6f * Time.deltaTime);
        if (Time.frameCount % 4 == 0) Effects.Burst(transform.position, new Color(0.7f, 0.3f, 0.25f), 1);
    }

    /// <summary>Shot down by the ЗЕНИТКА — pops harmlessly, deals no damage to the base.</summary>
    public void ShootDown()
    {
        Effects.Burst(transform.position, new Color(1f, 0.6f, 0.2f), 6);
        Destroy(gameObject);
    }

    void Detonate()
    {
        Effects.Explosion(transform.position);
        Effects.AirBlast(transform.position + Vector3.up * 0.5f, Blast * 1.4f);
        float rSq = Blast * Blast;
        foreach (var b in new List<Buildable>(Buildable.All))
            if (b != null && (b.transform.position - transform.position).sqrMagnitude <= rSq) b.TakeDamage(Damage);
        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (pc != null && (pc.transform.position - transform.position).sqrMagnitude <= rSq) pc.TakeDamage(35f);
        Destroy(gameObject);
    }
}
