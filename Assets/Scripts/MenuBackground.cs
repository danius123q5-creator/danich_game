using System.Collections.Generic;
using UnityEngine;

/// <summary>3.1.1: a live "background map" behind the main menu (Source-engine style). Builds a REAL
/// rolling-terrain battlefield (the game's own Hill() heightfield) with a defended base off to one
/// side while a horde of zombies sieges it from every direction and the sentries spit tracers. Purely
/// cosmetic (visual-only Models.* builders — no gameplay logic). Self-destructs the moment the game
/// leaves the menu, so it never leaks into an actual match.</summary>
public class MenuBackground : MonoBehaviour
{
    public Camera cam;

    static readonly Vector3 Base = new Vector3(20f, 0f, 8f); // the base sits off to the right
    readonly List<Walker> walkers = new List<Walker>();
    readonly List<Transform> sentries = new List<Transform>();
    float t, nextShot;

    class Walker { public Transform tr; public float speed; public float phase; }

    void Start() { BuildScene(); GameMusic.SpawnMenu(transform); }

    float G(float x, float z) => GameBootstrap.Hill(x, z);

    void BuildScene()
    {
        // Warm key light + soft haze so the vista reads with real depth (the menu builds no world).
        var lgo = new GameObject("MenuLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.rotation = Quaternion.Euler(46f, 32f, 0f);
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.15f;
        light.color = new Color(1f, 0.94f, 0.84f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.52f, 0.60f, 0.66f);
        RenderSettings.fogDensity = 0.010f;

        BuildTerrain();

        // A proper base: dispenser core, a ring of walls (tall ones at the back), a battery of sentries.
        Place(Models.BuildDispenser(3), Base, 0f);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            Vector3 wp = Base + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5.0f;
            var wall = (i % 3 == 0) ? Models.BuildWallTall(1) : Models.BuildWall(2);
            Place(wall, wp, a * Mathf.Rad2Deg + 90f);
        }
        for (int i = 0; i < 5; i++)
        {
            float a = (i / 5f + 0.1f) * Mathf.PI * 2f;
            Vector3 sp = Base + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.4f;
            sentries.Add(Place(Models.BuildSentry(3), sp, 0f).transform);
        }

        // Scatter some trees across the field (well clear of the base) for a real-map look.
        for (int i = 0; i < 16; i++)
        {
            float a = i * 2.39996f;                       // golden-angle scatter
            float r = 18f + (i % 6) * 11f;
            Vector3 p = new Vector3(Mathf.Cos(a) * r + 6f, 0f, Mathf.Sin(a) * r + 4f);
            if ((p - Base).sqrMagnitude < 12f * 12f) continue; // keep the base clearing open
            BuildTree(p);
        }

        for (int i = 0; i < 30; i++) SpawnWalker();
    }

    // A real rolling-terrain patch built from the game's Hill() heightfield — this is the "real map".
    void BuildTerrain()
    {
        const int n = 90;          // cells per side
        const float size = 220f;   // patch size (units)
        const float half = size * 0.5f;
        const float step = size / n;
        int vside = n + 1;
        var verts = new Vector3[vside * vside];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float wx = -half + x * step;
                float wz = -half + z * step;
                verts[z * vside + x] = new Vector3(wx, G(wx, wz), wz);
            }
        var tris = new int[n * n * 6];
        int ti = 0;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * vside + x;
                tris[ti++] = i; tris[ti++] = i + vside; tris[ti++] = i + 1;
                tris[ti++] = i + 1; tris[ti++] = i + vside; tris[ti++] = i + vside + 1;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject("MenuTerrain");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = new Material(GameBootstrap.StdShader());
        GameBootstrap.SetColor(go, new Color(0.30f, 0.36f, 0.22f)); // grassy ground
    }

    void BuildTree(Vector3 pos)
    {
        float gy = G(pos.x, pos.z);
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(trunk.GetComponent<Collider>());
        trunk.transform.SetParent(transform, false);
        trunk.transform.position = new Vector3(pos.x, gy + 1.6f, pos.z);
        trunk.transform.localScale = new Vector3(0.4f, 1.6f, 0.4f);
        GameBootstrap.SetColor(trunk, new Color(0.30f, 0.22f, 0.14f));
        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(canopy.GetComponent<Collider>());
        canopy.transform.SetParent(transform, false);
        canopy.transform.position = new Vector3(pos.x, gy + 3.9f, pos.z);
        canopy.transform.localScale = new Vector3(3.2f, 3.4f, 3.2f);
        GameBootstrap.SetColor(canopy, new Color(0.16f, 0.30f, 0.16f));
    }

    GameObject Place(GameObject go, Vector3 pos, float yaw)
    {
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(pos.x, G(pos.x, pos.z), pos.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    void SpawnWalker()
    {
        int kind = Random.value < 0.72f ? 0 : Random.Range(1, 5);
        var z = Models.BuildZombieVisual(kind);
        z.transform.SetParent(transform, false);
        Reset(new Walker { tr = z.transform, speed = Random.Range(1.4f, 2.8f), phase = Random.value * 6.28f }, true);
    }

    void Reset(Walker w, bool add = false)
    {
        float a = Random.value * Mathf.PI * 2f;
        float r = Random.Range(30f, 72f);
        Vector3 p = Base + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
        w.tr.position = new Vector3(p.x, G(p.x, p.z), p.z);
        w.speed = Random.Range(1.4f, 2.8f);
        if (add) walkers.Add(w);
    }

    void Update()
    {
        // Vanish the instant we're no longer on the menu (game started / paused into a match).
        if (!GameRoot.InMenu) { Destroy(gameObject); return; }

        float dt = Time.unscaledDeltaTime;
        t += dt;

        // Slow cinematic vista sweep over the battlefield.
        if (cam != null)
        {
            float sway = Mathf.Sin(t * 0.10f) * 7f;
            Vector3 eye = new Vector3(sway, 16f, -30f);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((new Vector3(6f, 1.5f, 9f)) - eye);
        }

        // Zombies march on the base from every side; when they reach it they respawn on the outer ring.
        for (int i = 0; i < walkers.Count; i++)
        {
            var w = walkers[i];
            if (w.tr == null) { Reset(w); continue; }
            Vector3 to = Base - w.tr.position; to.y = 0f;
            float d = to.magnitude;
            if (d < 5.5f) { Reset(w); continue; }
            Vector3 dir = to / d;
            float bob = Mathf.Abs(Mathf.Sin(t * 5f + w.phase)) * 0.16f;
            Vector3 np = w.tr.position + dir * w.speed * dt;
            w.tr.position = new Vector3(np.x, G(np.x, np.z) + bob, np.z);
            w.tr.rotation = Quaternion.Slerp(w.tr.rotation, Quaternion.LookRotation(dir), 8f * dt);
        }

        // Sentries occasionally spit a tracer at the nearest attacker (visual only, no sound).
        if (t >= nextShot && sentries.Count > 0 && walkers.Count > 0)
        {
            nextShot = t + Random.Range(0.08f, 0.24f);
            var s = sentries[Random.Range(0, sentries.Count)];
            Transform tgt = null; float best = 999f;
            foreach (var w in walkers)
            {
                if (w.tr == null) continue;
                float d = (w.tr.position - Base).sqrMagnitude;
                if (d < best) { best = d; tgt = w.tr; }
            }
            if (s != null && tgt != null)
                Effects.Tracer(s.position + Vector3.up * 1.1f, tgt.position + Vector3.up * 0.9f);
        }
    }
}
