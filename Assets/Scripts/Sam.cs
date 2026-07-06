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
            case 1: MaxHealth = 280f; range = 55f; reload = 2.6f; break;
            case 2: MaxHealth = 360f; range = 63f; reload = 2.0f; break;
            default: MaxHealth = 440f; range = 72f; reload = 1.5f; break;
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
        Bird target = NearestBird(rSq, true); // nearest bird not already claimed by a missile
        if (target == null) return;

        next = Time.time + reload;
        Claimed.Add(target);
        SamMissile.Launch(muzzle, target);
        Effects.TurretShot(muzzle);
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

/// <summary>A homing SAM missile: climbs toward its target bird and detonates on it, downing it.</summary>
public class SamMissile : MonoBehaviour
{
    Bird target;
    Vector3 lastAim;
    float life;
    const float Speed = 60f;

    public static void Launch(Vector3 from, Bird target)
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

        var m = go.AddComponent<SamMissile>();
        m.target = target;
        m.lastAim = target != null ? target.transform.position : from + Vector3.up * 20f;
    }

    void Update()
    {
        life += Time.deltaTime;
        if (target != null) lastAim = target.transform.position;

        Vector3 to = lastAim - transform.position;
        float dist = to.magnitude;
        if (dist < 1.6f || life > 4f || (target == null && dist < 3f)) { Detonate(); return; }

        Vector3 dir = dist > 0.001f ? to / dist : transform.forward;
        transform.position += dir * Speed * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(dir);

        Effects.Burst(transform.position, new Color(0.6f, 0.6f, 0.6f), 1); // smoke trail
    }

    void Detonate()
    {
        if (target != null) target.ShootDown(); // bird explodes out of the sky
        Effects.Burst(transform.position, new Color(1f, 0.6f, 0.2f), 8);
        Sam.Unclaim(target);
        Destroy(gameObject);
    }

    void OnDestroy() { Sam.Unclaim(target); }
}
