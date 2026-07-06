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

        // ENEMY raiders are the priority — engage them reliably (that's what the ПЗРК is FOR).
        foreach (var bmb in Bomber.All)
        {
            if (bmb == null || bmb.samEngaged || !bmb.enemy) continue;
            if ((bmb.transform.position - transform.position).sqrMagnitude > rSq) continue;
            bmb.samEngaged = true;
            next = Time.time + reload;
            SamMissile.LaunchAtBomber(muzzle, bmb);
            Effects.TurretShot(muzzle);
            return;
        }

        // Your OWN airstrike plane: only a 10% slip — but its crash wrecks EVERYTHING in a huge radius.
        foreach (var bmb in Bomber.All)
        {
            if (bmb == null || bmb.samEngaged || bmb.enemy) continue;
            if ((bmb.transform.position - transform.position).sqrMagnitude > rSq) continue;
            if (Random.value < 0.10f)
            {
                bmb.samEngaged = true;
                next = Time.time + reload;
                SamMissile.LaunchAtBomber(muzzle, bmb);
                Effects.TurretShot(muzzle);
                return;
            }
        }

        var targets = NearestBirds(rSq, 4); // nearest birds (distinct, up to 4)
        if (targets.Count == 0) return;

        next = Time.time + reload;
        // ALWAYS a full 4-missile salvo, one from each tube. Spread across the nearest birds
        // (round-robin), doubling up if there are fewer than 4 — so you always see all 4 fly.
        Vector3 right = transform.right;
        Vector3[] tubes =
        {
            muzzle + right * -0.4f + Vector3.up * 0.25f,
            muzzle + right *  0.4f + Vector3.up * 0.25f,
            muzzle + right * -0.4f - Vector3.up * 0.15f,
            muzzle + right *  0.4f - Vector3.up * 0.15f,
        };
        for (int i = 0; i < 4; i++)
            SamMissile.Launch(tubes[i], targets[i % targets.Count]);
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

        return go.AddComponent<SamMissile>();
    }

    public static void Launch(Vector3 from, Bird target)
    {
        var m = Create(from);
        m.target = target;
        m.lastAim = target != null ? target.transform.position : from + Vector3.up * 20f;
    }

    public static void LaunchAtBomber(Vector3 from, Bomber b)
    {
        var m = Create(from);
        m.bomber = b;
        m.lastAim = b != null ? b.transform.position : from + Vector3.up * 40f;
    }

    void Update()
    {
        life += Time.deltaTime;
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
