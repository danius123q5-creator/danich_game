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
        var b = Make(start, dir);
        b.target = player;
        if (LanManager.Instance != null && LanManager.Instance.Active && LanManager.Instance.IsHost)
            LanManager.Instance.SendBird(start, dir); // co-op: let clients see the fly-over too
    }

    /// <summary>Client: a cosmetic fly-over mirroring the host's bird (no drop).</summary>
    public static void SpawnCosmetic(Vector3 start, Vector3 dir)
    {
        Make(start, dir).cosmetic = true;
    }

    static Bird Make(Vector3 start, Vector3 dir)
    {
        var root = new GameObject("Bird");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = start;
        root.transform.rotation = Quaternion.LookRotation(dir);
        var b = root.AddComponent<Bird>();
        b.dir = dir;
        b.BuildModel();
        return b;
    }

    void BuildModel()
    {
        Color c = new Color(0.12f, 0.12f, 0.15f);
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, nose forward
        body.transform.localScale = new Vector3(0.6f, 1.3f, 0.6f);
        GameBootstrap.SetColor(body, c);

        wingL = MakeWing(-1.3f, c);
        wingR = MakeWing(1.3f, c);
    }

    Transform MakeWing(float x, Color c)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(w.GetComponent<Collider>());
        w.transform.SetParent(transform, false);
        w.transform.localScale = new Vector3(2.4f, 0.12f, 0.9f);
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
        Zombie.Create(transform.position, Zombie.Kind.Normal); // falls under its own gravity onto the player
        Effects.Burst(transform.position, new Color(0.1f, 0.1f, 0.12f), 10); // feather puff
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
