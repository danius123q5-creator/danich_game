using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A crow that flies across the sky over the player and drops a zombie on them.
/// The dropping bird runs on the host/offline; the released zombie syncs to co-op
/// clients like any other host zombie, and clients also get a cosmetic fly-over.
/// </summary>
public class Bird : MonoBehaviour
{
    const float FlyHeight = 32f;
    const float MaxTravel = 280f;

    // Bird species — each carries a different payload and flies differently, for variety.
    public enum Kind { Crow, Raven, Swift, Vulture }
    Kind kind = Kind.Crow;

    Vector3 dir;
    float speed = 24f;
    float traveled;
    Transform wingL, wingR;
    PlayerController target;          // null on a cosmetic (client) bird → never drops
    bool dropped, cosmetic;
    float lastDistSq = float.MaxValue;
    readonly HashSet<int> engagedBy = new HashSet<int>(); // AA emplacements that already rolled for this bird

    /// <summary>Host/offline: a bird that heads over the player and drops a zombie.</summary>
    public static void SpawnOver(PlayerController player)
    {
        if (player == null) return;
        float ang = Random.value * Mathf.PI * 2f;
        var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        Vector3 start = player.transform.position + Vector3.up * FlyHeight - dir * 140f;
        var b = Make(start, dir, PickKind());
        b.target = player;
        if (LanManager.Instance != null && LanManager.Instance.Active && LanManager.Instance.IsHost)
            LanManager.Instance.SendBird(start, dir); // co-op: let clients see the fly-over too
    }

    // Which species flies this time — tougher variants unlock as the waves climb.
    static Kind PickKind()
    {
        int w = GameManager.Instance != null ? GameManager.Instance.WaveNumber : 1;
        float r = Random.value;
        if (w >= 8 && r < 0.15f) return Kind.Vulture; // heavy: drops TWO — from wave 8
        if (w >= 5 && r < 0.35f) return Kind.Raven;   // tanky: drops a Tank — from wave 5
        if (w >= 3 && r < 0.60f) return Kind.Swift;   // fast: drops a Runner — from wave 3
        return Kind.Crow;
    }

    /// <summary>Client: a cosmetic fly-over mirroring the host's bird (no drop).</summary>
    public static void SpawnCosmetic(Vector3 start, Vector3 dir)
    {
        Make(start, dir, Kind.Crow).cosmetic = true;
    }

    static Bird Make(Vector3 start, Vector3 dir, Kind kind)
    {
        var root = new GameObject("Bird");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = start;
        root.transform.rotation = Quaternion.LookRotation(dir);
        var b = root.AddComponent<Bird>();
        b.dir = dir;
        b.kind = kind;
        b.speed = kind == Kind.Swift ? 40f : kind == Kind.Raven ? 18f : kind == Kind.Vulture ? 20f : 24f;
        b.BuildModel();
        return b;
    }

    void BuildModel()
    {
        // Per-species look: colour + overall size + wingspan.
        Color c; float sz, span;
        switch (kind)
        {
            case Kind.Raven:   c = new Color(0.05f, 0.05f, 0.07f); sz = 1.5f;  span = 1.7f; break; // big & black
            case Kind.Swift:   c = new Color(0.35f, 0.45f, 0.6f);  sz = 0.7f;  span = 0.9f; break; // small & blue-grey
            case Kind.Vulture: c = new Color(0.32f, 0.24f, 0.16f); sz = 1.6f;  span = 1.8f; break; // big & brown
            default:           c = new Color(0.12f, 0.12f, 0.15f); sz = 1.0f;  span = 1.3f; break; // crow
        }
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, nose forward
        body.transform.localScale = new Vector3(0.6f * sz, 1.3f * sz, 0.6f * sz);
        GameBootstrap.SetColor(body, c);

        wingL = MakeWing(-span, c, sz, span);
        wingR = MakeWing(span, c, sz, span);
    }

    Transform MakeWing(float x, Color c, float sz, float span)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(w.GetComponent<Collider>());
        w.transform.SetParent(transform, false);
        w.transform.localScale = new Vector3(1.85f * span, 0.12f * sz, 0.9f * sz);
        w.transform.localPosition = new Vector3(x, 0f, 0f);
        GameBootstrap.SetColor(w, c);
        return w.transform;
    }

    void Update()
    {
        transform.position += dir * speed * Time.deltaTime;
        traveled += speed * Time.deltaTime;

        // Flap.
        float flap = Mathf.Sin(Time.time * 16f) * 38f;
        if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
        if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);

        // Release the zombie at the closest approach to the player (when we start pulling away).
        if (!cosmetic && !dropped && target != null)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 t = target.transform.position; t.y = 0f;
            float dsq = (a - t).sqrMagnitude;
            if (traveled > 25f && dsq > lastDistSq) { dropped = true; DropZombie(); }
            lastDistSq = dsq;
        }

        if (traveled >= MaxTravel) Destroy(gameObject);
    }

    void DropZombie()
    {
        Vector3 p = transform.position;
        switch (kind)
        {
            case Kind.Raven: Zombie.Create(p, Zombie.Kind.Tank); break;                 // tanky payload
            case Kind.Swift: Zombie.Create(p, Zombie.Kind.Runner); break;               // fast rusher
            case Kind.Vulture:                                                            // drops TWO
                Zombie.Create(p, Zombie.Kind.Normal);
                Zombie.Create(p + new Vector3(1.5f, 0f, 0f), Zombie.Kind.Grenadier);
                break;
            default: Zombie.Create(p, Zombie.Kind.Normal); break;
        }
        Effects.Burst(p, new Color(0.1f, 0.1f, 0.12f), 10); // feather puff
    }

    /// <summary>AntiAir: each emplacement gets its OWN one-time roll for this bird
    /// (so several AA on the base stack their 50% chances). Returns true the first time
    /// this particular AA engages — and only while the bird can still be shot down.</summary>
    public bool TryEngage(int aaId)
    {
        if (dropped || cosmetic) return false; // already dropping its zombie / client-side copy
        return engagedBy.Add(aaId);            // true only the first time this AA engages
    }

    /// <summary>AntiAir hit: blow the bird out of the sky before it can drop its zombie.</summary>
    public void ShootDown()
    {
        dropped = true; // cancel the drop
        Effects.Explosion(transform.position);
        Destroy(gameObject);
    }
}
