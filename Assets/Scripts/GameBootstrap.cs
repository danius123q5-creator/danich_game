using UnityEngine;

/// <summary>
/// Builds the whole game from code when you press Play — no scene setup needed.
/// Open the project, press Play, and the floor/player/spawner all appear.
/// </summary>
public static class GameBootstrap
{
    // Root of all in-game objects, so the whole world can be torn down at once
    // (Main Menu / Quit to Menu). Null while in the menu.
    public static Transform World;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Object.FindFirstObjectByType<GameRoot>() != null) return;

        // Clear whatever the template scene placed (camera, audio listener).
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(c.gameObject);
        foreach (var a in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) Object.Destroy(a);

        new GameObject("GameRoot").AddComponent<GameRoot>(); // shows the main menu
    }

    /// <summary>Build the whole playable world under a single "World" root.</summary>
    public static void BuildWorld()
    {
        if (World != null) return;
        World = new GameObject("World").transform;

        var sun = new GameObject("Sun");
        sun.transform.SetParent(World);
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.4f);

        BuildTerrain();
        if (MapHasRiver) BuildRiver();

        int treeCount = MapVariant == 2 ? 60 : 220; // arena map is open for PvP
        for (int i = 0; i < treeCount; i++)
        {
            float ang = i * 2.39996f; // golden-angle scatter
            float r = 12f + (i % 30) * 8f;
            float tx = Mathf.Cos(ang) * r, tz = Mathf.Sin(ang) * r;
            if (MapHasRiver && Mathf.Abs(tx - RiverX) < RiverHalf + 2f) continue; // keep the river clear
            BuildTree(new Vector3(tx, 0f, tz), i);
        }

        var player = new GameObject("Player");
        player.transform.SetParent(World);
        player.transform.position = RandomSpawnPoint();
        player.AddComponent<PlayerController>();

        var gm = new GameObject("GameManager");
        gm.transform.SetParent(World);
        gm.AddComponent<GameManager>();
    }

    /// <summary>Tear the whole world down (back to the main menu).</summary>
    public static void DestroyWorld()
    {
        if (World != null) Object.Destroy(World.gameObject);
        World = null;
    }

    public const float MapSize = 500f;     // square map side (units)
    public const float HillAmp = 3.5f;
    public const float RiverX = 40f;       // river centreline (runs along Z, off to one side)
    public const float RiverHalf = 10f;    // half-width of the channel
    public const float RiverBed = -2.0f;   // riverbed depth at the centre
    public const float WaterLevel = -0.8f; // water surface height

    // Selectable map: 0 = Forest (default), 1 = Hills (tall), 2 = Arena (flat, no river).
    public static int MapVariant = 0;
    static float MapAmp => MapVariant == 1 ? 9f : (MapVariant == 2 ? 0.5f : HillAmp);
    public static bool MapHasRiver => MapVariant != 2;

    /// <summary>Terrain height at a world (x,z) — Perlin hills, with a carved river on river maps.</summary>
    public static float Hill(float x, float z)
    {
        float h = (Mathf.PerlinNoise(x * 0.025f + 100f, z * 0.025f + 100f) - 0.5f) * 2f * MapAmp;
        if (MapHasRiver)
        {
            float d = Mathf.Abs(x - RiverX);
            if (d < RiverHalf)
            {
                float edge = Mathf.SmoothStep(RiverBed, h, d / RiverHalf); // bed at centre → terrain at banks
                h = Mathf.Min(h, edge);
            }
        }
        return h;
    }

    /// <summary>A random standing point on the map (off the edges and out of the river).</summary>
    public static Vector3 RandomSpawnPoint()
    {
        float half = MapSize * 0.4f;
        float x = 0f, z = 0f;
        for (int t = 0; t < 12; t++)
        {
            x = Random.Range(-half, half);
            z = Random.Range(-half, half);
            if (Mathf.Abs(x - RiverX) > RiverHalf + 1.5f) break; // not in the river
        }
        return new Vector3(x, Hill(x, z) + 1.5f, z);
    }

    // Live terrain mesh handles, kept so the shovel can deform it at runtime.
    static Mesh terrainMesh;
    static Vector3[] terrainVerts;
    static MeshCollider terrainCol;
    static int terrainVside;
    static float terrainHalf, terrainStep;

    /// <summary>Dig: lower terrain vertices within <paramref name="radius"/> of a point by
    /// up to <paramref name="depth"/> (smooth falloff), then refresh the mesh + collider.
    /// Repeated digging carves trenches/tunnels. Clamped to a floor so you can't fall out.</summary>
    public static void Dig(Vector3 p, float radius, float depth)
    {
        if (terrainMesh == null) return;
        float rSq = radius * radius;
        int gx = Mathf.RoundToInt((p.x + terrainHalf) / terrainStep);
        int gz = Mathf.RoundToInt((p.z + terrainHalf) / terrainStep);
        int span = Mathf.CeilToInt(radius / terrainStep) + 1;
        bool changed = false;
        for (int z = gz - span; z <= gz + span; z++)
            for (int x = gx - span; x <= gx + span; x++)
            {
                if (x < 0 || z < 0 || x >= terrainVside || z >= terrainVside) continue;
                int i = z * terrainVside + x;
                Vector3 v = terrainVerts[i];
                float dx = v.x - p.x, dz = v.z - p.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > rSq) continue;
                float fall = 1f - Mathf.Sqrt(d2) / radius;   // 1 at centre → 0 at edge
                float ny = v.y - depth * fall;
                terrainVerts[i].y = Mathf.Max(ny, -8f);       // floor so you can't dig through the world
                changed = true;
            }
        if (!changed) return;
        terrainMesh.vertices = terrainVerts;
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();
        terrainCol.sharedMesh = null;     // force the physics mesh to rebuild
        terrainCol.sharedMesh = terrainMesh;
    }

    static void BuildTerrain()
    {
        const int n = 160;            // cells per side
        const float size = MapSize;   // world size (units)
        const float half = size * 0.5f;
        const float step = size / n;

        var go = new GameObject("Terrain");
        go.transform.SetParent(World);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mc = go.AddComponent<MeshCollider>();

        int vside = n + 1;
        var verts = new Vector3[vside * vside];
        var uvs = new Vector2[verts.Length];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float wx = -half + x * step;
                float wz = -half + z * step;
                int i = z * vside + x;
                verts[i] = new Vector3(wx, Hill(wx, wz), wz);
                uvs[i] = new Vector2((float)x / n, (float)z / n);
            }

        var tris = new int[n * n * 6];
        int t = 0;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * vside + x;
                tris[t++] = i;
                tris[t++] = i + vside;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + vside;
                tris[t++] = i + vside + 1;
            }

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        // Keep handles for runtime digging (shovel).
        terrainMesh = mesh; terrainVerts = verts; terrainCol = mc;
        terrainVside = vside; terrainHalf = half; terrainStep = step;

        mr.material = new Material(StdShader()); // build-safe (no Shader.Find that can strip to null)
        Color floor = MapVariant == 2 ? new Color(0.55f, 0.50f, 0.32f)  // arena / dry ground
                    : MapVariant == 1 ? new Color(0.25f, 0.42f, 0.20f)  // hills
                    : new Color(0.22f, 0.38f, 0.18f);                   // forest floor
        SetColor(go, floor);
    }

    static void BuildRiver()
    {
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "River";
        water.transform.SetParent(World);
        Object.Destroy(water.GetComponent<Collider>()); // walk into water freely
        water.transform.position = new Vector3(RiverX, WaterLevel, 0f);
        water.transform.localScale = new Vector3((RiverHalf * 2f) / 10f, 1f, MapSize / 10f); // 10u plane base
        MakeGhost(water, new Color(0.2f, 0.45f, 0.85f, 0.6f)); // translucent blue
    }

    // Five tree shapes, picked by seed so the forest is varied but stable per layout:
    // round deciduous, tall conifer, slim tall, wide bush, and autumn-coloured.
    static void BuildTree(Vector3 pos, int seed)
    {
        var root = new GameObject("Tree");
        root.transform.SetParent(World);
        root.transform.position = new Vector3(pos.x, Hill(pos.x, pos.z), pos.z);
        root.transform.rotation = Quaternion.Euler(0f, (seed * 57) % 360, 0f);

        int kind = seed % 5;
        float js = 0.85f + ((seed * 17) % 35) * 0.01f; // 0.85..1.19 size jitter
        Color bark = new Color(0.35f, 0.25f, 0.15f);

        switch (kind)
        {
            case 0: // round deciduous
                TreeTrunk(root, 1.4f * js, 0.4f * js, bark);
                for (int i = 0; i < 3; i++)
                    TreeLeaf(root, new Vector3(0f, (3.0f + i * 0.7f) * js, 0f), (2.4f - i * 0.5f) * js, new Color(0.16f, 0.4f, 0.14f));
                break;

            case 1: // tall dark conifer (pine) — stacked shrinking, slightly flattened tiers
                TreeTrunk(root, 1.8f * js, 0.32f * js, new Color(0.3f, 0.22f, 0.13f));
                for (int i = 0; i < 5; i++)
                    TreeLeaf(root, new Vector3(0f, (3.0f + i * 0.85f) * js, 0f), (2.3f - i * 0.42f) * js, new Color(0.10f, 0.32f, 0.16f), 0.8f);
                break;

            case 2: // slim tall trunk, small high canopy
                TreeTrunk(root, 2.4f * js, 0.28f * js, bark);
                TreeLeaf(root, new Vector3(0f, 5.2f * js, 0f), 1.9f * js, new Color(0.2f, 0.45f, 0.18f));
                TreeLeaf(root, new Vector3(0f, 6.1f * js, 0f), 1.3f * js, new Color(0.2f, 0.45f, 0.18f));
                break;

            case 3: // short trunk, wide bushy canopy
                TreeTrunk(root, 1.0f * js, 0.45f * js, bark);
                TreeLeaf(root, new Vector3(-0.7f * js, 2.4f * js, 0f), 2.0f * js, new Color(0.22f, 0.5f, 0.2f));
                TreeLeaf(root, new Vector3(0.7f * js, 2.4f * js, 0f), 2.0f * js, new Color(0.22f, 0.5f, 0.2f));
                TreeLeaf(root, new Vector3(0f, 3.3f * js, 0f), 2.4f * js, new Color(0.22f, 0.5f, 0.2f));
                break;

            default: // autumn — orange / russet foliage
                TreeTrunk(root, 1.5f * js, 0.4f * js, new Color(0.32f, 0.22f, 0.14f));
                Color fall = (seed % 2 == 0) ? new Color(0.82f, 0.52f, 0.16f) : new Color(0.72f, 0.33f, 0.12f);
                for (int i = 0; i < 3; i++)
                    TreeLeaf(root, new Vector3(0f, (3.0f + i * 0.7f) * js, 0f), (2.3f - i * 0.5f) * js, fall);
                break;
        }
    }

    static void TreeTrunk(GameObject root, float height, float radius, Color c)
    {
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // keeps its collider as an obstacle
        trunk.transform.SetParent(root.transform, false);
        trunk.transform.localPosition = new Vector3(0f, height, 0f);
        trunk.transform.localScale = new Vector3(radius, height, radius);
        SetColor(trunk, c);
    }

    static void TreeLeaf(GameObject root, Vector3 localPos, float scale, Color c, float flatY = 1f)
    {
        var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(leaf.GetComponent<Collider>()); // foliage is visual only
        leaf.transform.SetParent(root.transform, false);
        leaf.transform.localPosition = localPos;
        leaf.transform.localScale = new Vector3(scale, scale * flatY, scale);
        SetColor(leaf, c);
    }

    /// <summary>Tint a primitive's material (works in both Built-in and URP).</summary>
    public static void SetColor(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        var m = r.material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP/HDRP Lit
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);         // Built-in
        m.color = c;
    }

    /// <summary>A simple unlit shader that exists in whichever render pipeline is active.</summary>
    public static Shader LineShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color")
            ?? StdShader();
    }

    // The shader from a primitive's default material — ALWAYS included in a build.
    // (Shader.Find returns null in builds for shaders nothing references, which
    // makes new Material(...) invalid → grey/missing rendering. This never is null.)
    static Shader _std;
    public static Shader StdShader()
    {
        if (_std == null)
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _std = probe.GetComponent<Renderer>().sharedMaterial.shader;
            Object.Destroy(probe);
        }
        return _std;
    }

    /// <summary>Make a model translucent (placement ghost) and set its tint. Works best-effort in URP and Built-in.</summary>
    public static void MakeGhost(GameObject go, Color c)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col); // ghost must not collide
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var m = r.material;
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);            // URP: Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);                // URP: Alpha blend
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);                  // Built-in Standard: Transparent
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        SetGhostColor(go, c);
    }

    /// <summary>Update only the ghost tint (call each frame: green = ok, red = can't afford).</summary>
    public static void SetGhostColor(GameObject go, Color c)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
        }
    }
}
