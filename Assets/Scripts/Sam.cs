using System.Collections.Generic;
using UnityEngine;

/// <summary>ПЗРК — a stationary surface-to-air missile launcher. Unlike the ЗЕНИТКА (which rolls a
/// chance to swat birds/grenades), the ПЗРК locks onto a BIRD and fires a real homing missile that
/// climbs and knocks it out of the sky — a guaranteed kill, but on a reload. Birds are claimed so
/// several launchers spread their missiles across different birds instead of dogpiling one.</summary>
public class Sam : Buildable
{
    public override bool IsTrap => false;

    float range = 55f;
    float reload = 2.6f;
    float next;

    // Birds currently targeted by an in-flight missile (so launchers don't all fire at one bird).
    static readonly HashSet<Bird> Claimed = new HashSet<Bird>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetClaims() => Claimed.Clear();
    public static void Unclaim(Bird b) { if (b != null) Claimed.Remove(b); }

    protected override void Awake()
    {
        BuildCost = 250;   // deliberately pricier than the ЗЕНИТКА (120) — it's the reliable one
        MaxLevel = 3;
        UpgradeCost = 140;
        BuildTime = 2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 280f; range = 80f;  reload = 2.6f; break;
            case 2: MaxHealth = 360f; range = 95f;  reload = 2.0f; break;
            default: MaxHealth = 440f; range = 110f; reload = 1.5f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        float rSq = range * range;
        Vector3 muzzle = transform.position + Vector3.up * 1.6f;

        // Aim at the nearest bird (rotate the launcher so the tube points at it).
        Bird nearest = NearestBird(rSq, false);
        if (nearest != null)
        {
            Vector3 to = nearest.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 5f * Time.deltaTime);
        }

        if (Time.time < next) return;

        // Collect up to 4 DISTINCT air targets in range: ENEMY bombers first (that's what the
        // ПЗРК is FOR), then the nearest birds — so the salvo spreads across different targets.
        var picks = new List<object>();
        foreach (var bmb in Bomber.All)
        {
            if (bmb == null || bmb.samEngaged || !bmb.enemy) continue;
            if ((bmb.transform.position - transform.position).sqrMagnitude > rSq) continue;
            picks.Add(bmb);
            if (picks.Count >= 4) break;
        }
        if (picks.Count < 4)
            foreach (var b in NearestBirds(rSq, 4))
            {
                if (b == null || picks.Contains(b)) continue;
                picks.Add(b);
                if (picks.Count >= 4) break;
            }

        // Nothing airborne to engage — hold fire. (No more friendly-fire on your own airstrike plane:
        // planes don't crash anymore, so downing your own would just waste it.)
        if (picks.Count == 0) return;

        next = Time.time + reload;
        // Full 4-missile salvo, one from each tube — each at a DISTINCT target (round-robin if
        // fewer than 4). Exactly ONE missile of the four is a dud that whiffs and falls to earth.
        Vector3 right = transform.right;
        Vector3[] tubes =
        {
            muzzle + right * -0.4f + Vector3.up * 0.25f,
            muzzle + right *  0.4f + Vector3.up * 0.25f,
            muzzle + right * -0.4f - Vector3.up * 0.15f,
            muzzle + right *  0.4f - Vector3.up * 0.15f,
        };
        int missIdx = Random.Range(0, 4);
        for (int i = 0; i < 4; i++)
        {
            object tgt = picks[i % picks.Count];
            bool willMiss = (i == missIdx);
            if (tgt is Bomber bm) { bm.samEngaged = true; SamMissile.LaunchAtBomber(tubes[i], bm, willMiss); }
            else if (tgt is Bird bd) SamMissile.Launch(tubes[i], bd, willMiss);
        }
        Effects.TurretShot(muzzle);
    }

    static readonly List<Bird> _found = new List<Bird>();
    List<Bird> NearestBirds(float rSq, int max)
    {
        _found.Clear();
        Vector3 p = transform.position;
        foreach (var b in Object.FindObjectsByType<Bird>(FindObjectsSortMode.None))
            if (b != null && (b.transform.position - p).sqrMagnitude <= rSq)
                _found.Add(b);
        _found.Sort((x, y) => (x.transform.position - p).sqrMagnitude.CompareTo((y.transform.position - p).sqrMagnitude));
        if (_found.Count > max) _found.RemoveRange(max, _found.Count - max);
        return _found;
    }

    Bird NearestBird(float rSq, bool unclaimedOnly)
    {
        Bird best = null; float bestSq = rSq;
        foreach (var b in Object.FindObjectsByType<Bird>(FindObjectsSortMode.None))
        {
            if (b == null) continue;
            if (unclaimedOnly && Claimed.Contains(b)) continue;
            float d = (b.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = b; }
        }
        return best;
    }
}

/// <summary>A homing SAM missile: climbs toward its target (a bird, or the airstrike bomber) and
/// detonates on it — downing the bird, or crashing the plane.</summary>
public class SamMissile : MonoBehaviour
{
    Bird target;        // bird payload target
    Bomber bomber;      // OR the airstrike bomber
    Vector3 lastAim;
    float life;
    const float Speed = 60f;

    // Exactly ONE missile per 4-round salvo is a dud: it stops homing, sails past on a fixed
    // heading and gravity arcs it down to crash into the ground (harmless). The launcher decides
    // which one whiffs and passes willMiss=true for it.
    bool miss;
    Vector3 vel;                    // ballistic velocity while missing

    static SamMissile Create(Vector3 from)
    {
        var go = new GameObject("SamMissile");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = from;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.22f, 0.6f, 0.22f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // length along +Z (forward)
        GameBootstrap.SetColor(body, new Color(0.35f, 0.35f, 0.38f));

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(flame.GetComponent<Collider>());
        flame.transform.SetParent(go.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0f, -0.6f);
        flame.transform.localScale = Vector3.one * 0.35f;
        GameBootstrap.SetColor(flame, new Color(1f, 0.7f, 0.25f));

        // Smoke trail streaking behind the missile as it flies.
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.75f;
        tr.startWidth = 0.5f; tr.endWidth = 0.04f;
        tr.minVertexDistance = 0.15f;
        var tmat = new Material(GameBootstrap.LineShader());
        tmat.color = new Color(0.85f, 0.85f, 0.85f);
        tr.sharedMaterial = tmat;
        tr.startColor = new Color(0.92f, 0.92f, 0.92f, 0.7f);
        tr.endColor = new Color(0.7f, 0.7f, 0.7f, 0f);

        return go.AddComponent<SamMissile>();
    }

    public static void Launch(Vector3 from, Bird target, bool willMiss = false)
    {
        var m = Create(from);
        m.target = target;
        m.lastAim = target != null ? target.transform.position : from + Vector3.up * 20f;
        m.SetupMiss(from, willMiss);
    }

    public static void LaunchAtBomber(Vector3 from, Bomber b, bool willMiss = false)
    {
        var m = Create(from);
        m.bomber = b;
        m.lastAim = b != null ? b.transform.position : from + Vector3.up * 40f;
        m.SetupMiss(from, willMiss);
    }

    // If this missile is the designated dud, lock in a fixed heading angled off the target (and
    // biased a little upward) so it flies past instead of homing in.
    void SetupMiss(Vector3 from, bool willMiss)
    {
        if (!willMiss) return;
        miss = true;
        Vector3 aim = lastAim - from;
        aim.y = Mathf.Max(aim.y, 3f);                 // lob slightly up so it sails over the target
        aim = aim.sqrMagnitude > 0.01f ? aim.normalized : Vector3.up;
        float yaw = Random.Range(8f, 18f) * (Random.value < 0.5f ? -1f : 1f);
        var off = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(Random.Range(-10f, 4f), Vector3.right);
        vel = off * aim * Speed;
    }

    void Update()
    {
        life += Time.deltaTime;
        if (miss) { UpdateMiss(); return; }

        if (bomber != null) lastAim = bomber.transform.position;
        else if (target != null) lastAim = target.transform.position;

        Vector3 to = lastAim - transform.position;
        float dist = to.magnitude;
        bool lost = (bomber == null && target == null);
        if (dist < 1.8f || life > 6f || (lost && dist < 3f)) { Detonate(); return; }

        Vector3 dir = dist > 0.001f ? to / dist : transform.forward;
        transform.position += dir * Speed * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(dir);

        Effects.Burst(transform.position, new Color(0.6f, 0.6f, 0.6f), 1); // smoke trail
    }

    // A missed missile: no homing, gravity pulls its fixed-heading flight down until it hits the
    // ground and fizzles (no kill). Never damages the target it whiffed on.
    void UpdateMiss()
    {
        vel.y -= 22f * Time.deltaTime; // gravity arcs it back down
        transform.position += vel * Time.deltaTime;
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel.normalized);
        Effects.Burst(transform.position, new Color(0.6f, 0.6f, 0.6f), 1); // smoke trail

        float ground = GameBootstrap.Hill(transform.position.x, transform.position.z);
        if (transform.position.y <= ground + 0.3f || life > 9f)
        {
            Vector3 p = transform.position; p.y = ground + 0.2f;
            Effects.Burst(p, new Color(1f, 0.6f, 0.2f), 6); // dud ground impact
            Sam.Unclaim(target);
            Destroy(gameObject);
        }
    }

    void Detonate()
    {
        if (bomber != null) bomber.CrashDown();     // shoot the airstrike plane out of the sky
        else if (target != null) target.ShootDown(); // bird explodes out of the sky
        Effects.Burst(transform.position, new Color(1f, 0.6f, 0.2f), 8);
        Sam.Unclaim(target);
        Destroy(gameObject);
    }

    void OnDestroy() { Sam.Unclaim(target); }
}
