using UnityEngine;
using System.Collections.Generic;

/// <summary>Code-only juice: a procedural "ding" sound and a spark burst (no assets).</summary>
public static class Effects
{
    static AudioClip upgradeClip;
    static AudioClip gunClip;
    static AudioClip boomClip;
    static AudioClip turretClip;
    static AudioClip zapClip;
    static AudioClip cannonClip;
    static AudioClip planeClip;
    static AudioClip bigBoomClip;

    // Co-op: when true, we are REPLAYING an effect received over the network — don't re-broadcast.
    public static bool NetSuppress;
    static void Net(char code, Vector3 p)
    {
        if (NetSuppress) return;
        var lan = LanManager.Instance;
        if (lan != null && lan.Active) lan.FxPoint(code, p);
    }

    // ───────────────────────── pooling (perf) ─────────────────────────
    // Tracers and short sounds fire on EVERY bullet — creating/destroying a GameObject
    // (+ a fresh Material, + a temp AudioSource) per shot churned the GC hard during
    // sustained fire. We reuse a small pool of each instead.

    static Material _lineMat;
    // One shared unlit material for all line renderers (tracers, shockwaves). Per-renderer
    // start/end colours still vary; only the material (shader) is shared, so this is safe.
    static Material LineMat()
    {
        if (_lineMat == null) _lineMat = new Material(GameBootstrap.LineShader());
        return _lineMat;
    }

    static readonly Stack<GameObject> _tracerPool = new Stack<GameObject>();
    internal static void ReturnTracer(GameObject go) { go.SetActive(false); _tracerPool.Push(go); }

    static AudioSource[] _audioPool;
    static int _audioIdx;
    // Positional one-shot like AudioSource.PlayClipAtPoint, but reusing a ring of sources
    // instead of spawning (and destroying) a GameObject for every sound.
    static void PlayAt(AudioClip clip, Vector3 pos, float vol)
    {
        if (clip == null) return;
        if (_audioPool == null)
        {
            _audioPool = new AudioSource[16];
            for (int i = 0; i < _audioPool.Length; i++)
            {
                var go = new GameObject("FxAudio");
                Object.DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f; // 3D, matches PlayClipAtPoint
                _audioPool[i] = src;
            }
        }
        var s = _audioPool[_audioIdx];
        _audioIdx = (_audioIdx + 1) % _audioPool.Length;
        s.transform.position = pos;
        s.PlayOneShot(clip, vol);
    }

    public static void Upgrade(Vector3 pos)
    {
        Burst(pos, new Color(1f, 0.85f, 0.3f), 14);
        if (upgradeClip == null) upgradeClip = MakeTone(700f, 1350f, 0.25f);
        PlayAt(upgradeClip, pos, 0.8f);
        Net('U', pos);
    }

    /// <summary>Visible bullet trail in the Game view (not just a debug line).</summary>
    public static void Tracer(Vector3 a, Vector3 b)
    {
        GameObject go = null;
        while (go == null && _tracerPool.Count > 0) go = _tracerPool.Pop(); // skip any destroyed entry
        LineRenderer lr;
        if (go == null)
        {
            go = new GameObject("Tracer");
            Object.DontDestroyOnLoad(go);
            lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = LineMat();
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.startColor = lr.endColor = new Color(1f, 0.9f, 0.4f);
            go.AddComponent<TracerFx>();
        }
        else
        {
            go.SetActive(true);
            lr = go.GetComponent<LineRenderer>();
        }
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        go.GetComponent<TracerFx>().Restart();
        if (!NetSuppress) { var lan = LanManager.Instance; if (lan != null && lan.Active) lan.FxLine(a, b); }
    }

    public static void GunShot(Vector3 pos)
    {
        if (gunClip == null) gunClip = MakeTone(320f, 90f, 0.08f);
        PlayAt(gunClip, pos, 0.5f);
        Net('G', pos);
    }

    public static void TurretShot(Vector3 pos)
    {
        if (turretClip == null) turretClip = MakeTone(520f, 130f, 0.09f);
        PlayAt(turretClip, pos, 0.55f); // punchy auto-turret "pew"
        Net('S', pos);
    }

    /// <summary>Electric crackle for the Tesla coil.</summary>
    public static void Zap(Vector3 pos)
    {
        if (zapClip == null) zapClip = MakeTone(1100f, 320f, 0.10f);
        PlayAt(zapClip, pos, 0.5f);
        Net('Z', pos);
    }

    /// <summary>Loud, deep BOOM for the artillery cannon (separate from the shell impact).</summary>
    public static void CannonFire(Vector3 pos)
    {
        // Long, low, near-full-amplitude tone → a proper window-rattling boom.
        if (cannonClip == null) cannonClip = MakeTone(120f, 32f, 0.5f, 0.95f);
        PlayAt(cannonClip, pos, 1f);
        Burst(pos, new Color(1f, 0.7f, 0.25f), 16); // muzzle flash/smoke
        Net('C', pos);
    }

    /// <summary>Rocket/death blast: fireball + smoke puffs + spark burst + boom.</summary>
    public static void Explosion(Vector3 pos)
    {
        Fireball(pos, 3.2f);
        for (int i = 0; i < 4; i++) // smoke puffs
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = pos + Vector3.up * 0.4f + new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
            g.transform.localScale = Vector3.one * Random.Range(0.6f, 1.1f);
            GameBootstrap.SetColor(g, new Color(0.2f, 0.2f, 0.2f));
            g.AddComponent<SmokeFx>().vel = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(1.4f, 2.6f), Random.Range(-0.5f, 0.5f));
        }
        Burst(pos, new Color(1f, 0.5f, 0.15f), 22);
        if (boomClip == null) boomClip = MakeTone(180f, 40f, 0.3f);
        PlayAt(boomClip, pos, 0.7f);
        Net('X', pos);
    }

    /// <summary>Soil kick-up + soft thud when digging with the shovel.</summary>
    public static void Dirt(Vector3 pos)
    {
        Burst(pos, new Color(0.4f, 0.3f, 0.18f), 8);
        if (turretClip == null) turretClip = MakeTone(520f, 130f, 0.09f);
        PlayAt(turretClip, pos, 0.2f); // quiet, reuse a short thud tone
        Net('I', pos);
    }

    /// <summary>Stylized "dematerialize" death: a puff of glowing (emissive) sparks that
    /// rise and shrink. The emission makes them bloom brightly, so a kill reads as the
    /// zombie vaporising into light. Purely cosmetic — safe to call then destroy the zombie.</summary>
    public static void Vaporize(Vector3 pos, Color tint)
    {
        for (int i = 0; i < 16; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.6f, 0.8f), Random.Range(-0.4f, 0.4f));
            g.transform.localScale = Vector3.one * 0.15f;
            GameBootstrap.SetColor(g, tint);
            SetEmission(g, tint, 2.4f); // bright glow → blooms
            g.AddComponent<VaporizeSpark>().vel = new Vector3(Random.Range(-0.7f, 0.7f), Random.Range(2.5f, 4.8f), Random.Range(-0.7f, 0.7f));
        }
    }

    /// <summary>Make a primitive glow (emissive) so the bloom post-process picks it up.</summary>
    static void SetEmission(GameObject go, Color c, float intensity)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        var m = r.material;
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
    }

    public static void Burst(Vector3 pos, Color c, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = pos;
            g.transform.localScale = Vector3.one * 0.16f;
            GameBootstrap.SetColor(g, c);
            var fx = g.AddComponent<SparkFx>();
            fx.vel = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(2.5f, 5.5f), Random.Range(-2.5f, 2.5f));
        }
    }

    // ───────────────────────── AIR STRIKE ─────────────────────────

    /// <summary>Spawn a bomber that flies over <paramref name="center"/> and drops a bomb on each
    /// point in sequence. Each bomb falls from the sky and detonates with a big blast on impact;
    /// <paramref name="onImpact"/> runs at the moment of detonation (apply damage there for sync).</summary>
    public static void AirStrikeRun(Vector3 center, List<Vector3> points, float radius, System.Action<Vector3> onImpact)
    {
        var go = new GameObject("Bomber");
        go.AddComponent<Bomber>().Init(center, points, radius, onImpact);
        if (planeClip == null) planeClip = MakeTone(230f, 150f, 1.6f, 0.5f); // engine drone
        PlayAt(planeClip, center + Vector3.up * 30f, 1f);
    }

    /// <summary>Big ground detonation: fireball + smoke plume + shockwave ring + sparks + deep boom.</summary>
    public static void AirBlast(Vector3 pos, float radius)
    {
        Fireball(pos, radius);
        SmokePlume(pos, radius);
        Shockwave(pos, radius);
        Burst(pos, new Color(1f, 0.55f, 0.15f), 38);
        Burst(pos, new Color(0.35f, 0.28f, 0.18f), 14); // debris
        if (bigBoomClip == null) bigBoomClip = MakeTone(95f, 26f, 0.65f, 0.95f);
        PlayAt(bigBoomClip, pos, 1f);
        if (!NetSuppress) { var lan = LanManager.Instance; if (lan != null && lan.Active) lan.FxAirBlast(pos, radius); }
    }

    static void Fireball(Vector3 pos, float radius)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.position = pos + Vector3.up * 0.6f;
        g.transform.localScale = Vector3.one * (radius * 0.35f);
        GameBootstrap.SetColor(g, new Color(1f, 0.65f, 0.18f));
        g.AddComponent<FireballFx>().maxScale = radius * 1.5f;
    }

    static void SmokePlume(Vector3 pos, float radius)
    {
        int n = 10;
        for (int i = 0; i < n; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = pos + Vector3.up * 0.5f + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            g.transform.localScale = Vector3.one * Random.Range(0.8f, 1.7f);
            GameBootstrap.SetColor(g, new Color(0.18f, 0.18f, 0.18f));
            g.AddComponent<SmokeFx>().vel = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1.6f, 3.2f), Random.Range(-0.6f, 0.6f));
        }
    }

    static void Shockwave(Vector3 pos, float radius)
    {
        var go = new GameObject("Shockwave");
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = LineMat();
        lr.loop = true;
        lr.useWorldSpace = true;
        int seg = 40;
        lr.positionCount = seg;
        lr.startWidth = lr.endWidth = 0.35f;
        lr.startColor = lr.endColor = new Color(1f, 0.8f, 0.4f);
        var sw = go.AddComponent<ShockwaveFx>();
        sw.lr = lr; sw.center = pos + Vector3.up * 0.12f; sw.seg = seg; sw.maxR = radius * 1.4f;
    }

    static AudioClip MakeTone(float f0, float f1, float dur, float amp = 0.4f)
    {
        const int rate = 44100;
        int n = Mathf.Max(1, (int)(rate * dur));
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float f = Mathf.Lerp(f0, f1, t / dur);
            data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-4f * t / dur) * amp;
        }
        var clip = AudioClip.Create("tone", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

/// <summary>One spark: arcs up with gravity, shrinks, then removes itself.</summary>
public class SparkFx : MonoBehaviour
{
    public Vector3 vel;
    float life;

    void Update()
    {
        life += Time.deltaTime;
        vel.y -= 9.8f * Time.deltaTime;
        transform.position += vel * Time.deltaTime;
        transform.localScale = Vector3.one * Mathf.Lerp(0.16f, 0f, life / 0.5f);
        if (life >= 0.5f) Destroy(gameObject);
    }
}

/// <summary>A glowing death spark: rises, slows and shrinks into nothing.</summary>
public class VaporizeSpark : MonoBehaviour
{
    public Vector3 vel;
    float life;
    void Update()
    {
        life += Time.deltaTime;
        vel *= (1f - 1.6f * Time.deltaTime);
        transform.position += vel * Time.deltaTime;
        transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0f, life / 0.5f);
        if (life >= 0.5f) Destroy(gameObject);
    }
}

/// <summary>A bullet trail that returns itself to the pool after a couple of frames.</summary>
public class TracerFx : MonoBehaviour
{
    float life;
    public void Restart() { life = 0f; }
    void Update()
    {
        life += Time.deltaTime;
        if (life >= 0.05f) Effects.ReturnTracer(gameObject);
    }
}

/// <summary>Expanding fireball that swells then darkens and vanishes.</summary>
public class FireballFx : MonoBehaviour
{
    public float maxScale = 6f;
    float life, start;
    void Start() { start = transform.localScale.x; }
    void Update()
    {
        life += Time.deltaTime;
        float t = life / 0.5f;
        transform.localScale = Vector3.one * Mathf.Lerp(start, maxScale, Mathf.SmoothStep(0f, 1f, t));
        GameBootstrap.SetColor(gameObject, Color.Lerp(new Color(1f, 0.7f, 0.22f), new Color(0.25f, 0.1f, 0.05f), t));
        if (life >= 0.5f) Destroy(gameObject);
    }
}

/// <summary>Dark smoke puff that rises, slows and grows before fading out.</summary>
public class SmokeFx : MonoBehaviour
{
    public Vector3 vel;
    float life;
    void Update()
    {
        life += Time.deltaTime;
        transform.position += vel * Time.deltaTime;
        vel *= (1f - 0.6f * Time.deltaTime);
        transform.localScale += Vector3.one * (0.9f * Time.deltaTime);
        if (life >= 1.7f) Destroy(gameObject);
    }
}

/// <summary>Ground shockwave: a ring that expands outward and thins to nothing.</summary>
public class ShockwaveFx : MonoBehaviour
{
    public LineRenderer lr;
    public Vector3 center;
    public int seg;
    public float maxR;
    float life;
    void Update()
    {
        life += Time.deltaTime;
        float t = life / 0.45f;
        float r = Mathf.Lerp(0.5f, maxR, t);
        for (int i = 0; i < seg; i++)
        {
            float a = i * (Mathf.PI * 2f / seg);
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        lr.startWidth = lr.endWidth = Mathf.Lerp(0.35f, 0f, t);
        if (life >= 0.45f) Destroy(gameObject);
    }
}

/// <summary>The bomber: a large Tu-95 "Bear" built from primitives that flies straight over the
/// target and releases a falling bomb on each point with a short stagger (carpet-bombing run).</summary>
public class Bomber : MonoBehaviour
{
    Vector3 dir, pos;
    List<Vector3> points;
    System.Action<Vector3> onImpact;
    readonly List<Transform> props = new List<Transform>();
    float radius, life, nextDrop = 0.45f, speed = 75f;
    int dropped;

    public void Init(Vector3 center, List<Vector3> pts, float r, System.Action<Vector3> cb)
    {
        points = pts; onImpact = cb; radius = r;
        dir = new Vector3(1f, 0f, 0.22f).normalized;
        pos = center - dir * 120f + Vector3.up * 56f; // bigger plane: higher & further out so the run reads

        BuildTu95();
        transform.localScale = Vector3.one * 1.7f; // scale the whole airframe up (model only — flight/bombs unaffected)

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    // Tu-95 silhouette from primitives. Local +Z = nose (LookRotation aligns it to the flight dir):
    // long fuselage, swept mid-wings, four turboprop nacelles with spinning props, swept tail.
    void BuildTu95()
    {
        Color metal = new Color(0.62f, 0.64f, 0.67f);
        Color metalDark = new Color(0.42f, 0.44f, 0.48f);
        Color glass = new Color(0.22f, 0.32f, 0.42f);
        Color propC = new Color(0.10f, 0.10f, 0.12f);

        // fuselage (capsule laid along Z) + cockpit glass
        Prim(PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 8.5f, 1.6f), metal, new Vector3(90f, 0f, 0f));
        Prim(PrimitiveType.Sphere, new Vector3(0f, 0.45f, 6.9f), new Vector3(1.3f, 1.0f, 1.7f), glass);

        // swept mid-mounted wings
        Prim(PrimitiveType.Cube, new Vector3(5.6f, -0.1f, -0.3f), new Vector3(10f, 0.35f, 2.6f), metal, new Vector3(0f, 22f, 0f));
        Prim(PrimitiveType.Cube, new Vector3(-5.6f, -0.1f, -0.3f), new Vector3(10f, 0.35f, 2.6f), metal, new Vector3(0f, -22f, 0f));

        // four turboprop engines (inner + outer per wing), each with a spinning prop disc
        float[] ex = { 3.2f, 6.4f, -3.2f, -6.4f };
        foreach (float x in ex)
        {
            float z = 1.4f - Mathf.Abs(x) * 0.3f; // sit the nacelle along the wing's swept leading edge
            Prim(PrimitiveType.Cylinder, new Vector3(x, -0.5f, z), new Vector3(0.75f, 1.7f, 0.75f), metalDark, new Vector3(90f, 0f, 0f));
            MakeProp(new Vector3(x, -0.5f, z + 2.0f), propC);
        }

        // swept tail: tall vertical fin + horizontal stabilisers
        Prim(PrimitiveType.Cube, new Vector3(0f, 1.7f, -6.6f), new Vector3(0.35f, 3.4f, 2.4f), metal, new Vector3(-25f, 0f, 0f));
        Prim(PrimitiveType.Cube, new Vector3(2.2f, 0.3f, -6.8f), new Vector3(4.0f, 0.3f, 1.6f), metal, new Vector3(0f, 24f, 0f));
        Prim(PrimitiveType.Cube, new Vector3(-2.2f, 0.3f, -6.8f), new Vector3(4.0f, 0.3f, 1.6f), metal, new Vector3(0f, -24f, 0f));
    }

    // A propeller disc facing forward (+Z): two crossed blades for a 4-blade look, spun about Z.
    void MakeProp(Vector3 localPos, Color c)
    {
        var hub = new GameObject("Prop").transform;
        hub.SetParent(transform, false);
        hub.localPosition = localPos;

        var b1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b1.GetComponent<Collider>());
        b1.transform.SetParent(hub, false);
        b1.transform.localScale = new Vector3(0.22f, 2.9f, 0.12f);
        GameBootstrap.SetColor(b1, c);

        var b2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b2.GetComponent<Collider>());
        b2.transform.SetParent(hub, false);
        b2.transform.localScale = new Vector3(2.9f, 0.22f, 0.12f);
        GameBootstrap.SetColor(b2, c);

        props.Add(hub);
    }

    void Prim(PrimitiveType type, Vector3 localPos, Vector3 scale, Color c, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = localPos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
    }

    void Update()
    {
        life += Time.deltaTime;
        pos += dir * speed * Time.deltaTime;
        transform.position = pos;

        float spin = 2200f * Time.deltaTime; // turboprop blur
        for (int i = 0; i < props.Count; i++)
            if (props[i] != null) props[i].Rotate(0f, 0f, spin, Space.Self);

        if (dropped < points.Count && life >= nextDrop)
        {
            nextDrop += 0.13f;
            var target = points[dropped++];
            var bgo = new GameObject("Bomb");
            bgo.AddComponent<FallingBomb>().Init(new Vector3(target.x, pos.y, target.z), target, radius, onImpact);
        }

        if (dropped >= points.Count && life > 4.5f) Destroy(gameObject);
    }
}

/// <summary>A bomb falling from the bomber: accelerates downward, then detonates (AirBlast +
/// damage callback) on reaching the ground point.</summary>
public class FallingBomb : MonoBehaviour
{
    Vector3 from, to;
    float radius, t, dur;
    System.Action<Vector3> onImpact;

    public void Init(Vector3 f, Vector3 target, float r, System.Action<Vector3> cb)
    {
        from = f; to = target + Vector3.up * 0.4f; radius = r; onImpact = cb;
        dur = Mathf.Max(0.35f, (from.y - to.y) / 60f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(0.3f, 0.55f, 0.3f);
        GameBootstrap.SetColor(body, new Color(0.14f, 0.14f, 0.16f));

        transform.position = from;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / dur);
        transform.position = Vector3.Lerp(from, to, k * k); // ease-in = gravity feel
        if (k >= 1f)
        {
            Effects.AirBlast(to, radius);
            onImpact?.Invoke(to);
            Destroy(gameObject);
        }
    }
}
