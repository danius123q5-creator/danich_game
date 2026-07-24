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

    // Where the player should (re)spawn — right beside the starter base. Set when the
    // base is built; until then (or for saves/co-op clients) we fall back to a random spot.
    public static Vector3 BaseSpawn;
    public static bool HasBaseSpawn;

    // 2.3: night variant of the current map — darker ambient, moonlight, dark sky. Decided per
    // game (random unless forced). Explosions cast light, so night waves look dramatic.
    public static bool Night;
    public static bool ForceNight, ForceDay; // debug overrides

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Object.FindFirstObjectByType<GameRoot>() != null) return;

        // Clear whatever the template scene placed (camera, audio listener).
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(c.gameObject);
        foreach (var a in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) Object.Destroy(a);

        // 3.7: the standalone MODEL-VIEWER build (BuildScript.BuildModelViewer bakes this product
        // name) boots straight into the viewer instead of the game — same code, its own .exe.
        if (Application.productName == "ZombieShooterModelViewer")
        {
            new GameObject("ModelViewerApp").AddComponent<ModelViewerApp>();
            return;
        }

        new GameObject("GameRoot").AddComponent<GameRoot>(); // shows the main menu
    }

    /// <summary>Build the whole playable world under a single "World" root.</summary>
    public static void BuildWorld()
    {
        if (World != null) return;
        World = new GameObject("World").transform;
        HasBaseSpawn = false; // a fresh world has no base yet (BuildStarterBase sets it)

        ModRuntime.Load(); // 3.2: load node-graph mods FIRST so their multipliers apply to every build

        var m = Cur;

        // Maps are DAY only (night variants removed by request — they read too dark).
        Night = false;

        var sun = new GameObject("Sun");
        sun.transform.SetParent(World);
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = Night ? 0.28f : m.sunInt;
        light.color = Night ? new Color(0.55f, 0.62f, 0.85f) : m.sun; // cold moonlight at night
        sun.transform.rotation = Quaternion.Euler(Night ? 62f : 50f, Night ? 20f : -30f, 0f);
        RenderSettings.ambientLight = Night
            ? new Color(m.ambient.r * 0.22f + 0.03f, m.ambient.g * 0.24f + 0.04f, m.ambient.b * 0.30f + 0.07f)
            : m.ambient;

        // Per-map fog (off when density is 0) sets the mood: desert haze, snow whiteout, etc.
        // At night, force a light dark-blue haze so the darkness reads as depth, not a flat void.
        bool fog = m.fogDensity > 0f || Night;
        RenderSettings.fog = fog;
        if (fog)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Night ? new Color(0.04f, 0.05f, 0.10f) : m.fog;
            RenderSettings.fogDensity = Night ? Mathf.Max(m.fogDensity, 0.006f) : m.fogDensity;
        }

        // Stylized look: soft shadows + a global post-processing volume (bloom/ACES/grade).
        VisualFx.Apply(World, light);

        // Adaptive procedural soundtrack: calm during prep, driving during waves.
        GameMusic.Spawn(World);

        BuildTerrain();
        if (m.waterPlane != 0) BuildWater();

        for (int i = 0; i < m.trees; i++)
        {
            float ang = i * 2.39996f; // golden-angle scatter
            float r = 12f + (i % 30) * 8f;
            float tx = Mathf.Cos(ang) * r, tz = Mathf.Sin(ang) * r;
            if (m.channel && Mathf.Abs(tx - RiverX) < RiverHalf + 2f) continue;     // keep the trench clear
            if (m.waterPlane == 2 && Hill(tx, tz) < m.water + 0.3f) continue;       // islands: trees on land only
            BuildTree(new Vector3(tx, 0f, tz), i, m.treeStyle);
        }

        ScatterGrass(m);    // wind-swaying CC0 grass tufts around the play area
        ScatterWreckage(m); // apocalyptic debris: car wrecks, rubble, pipes

        var player = new GameObject("Player");
        player.transform.SetParent(World);
        player.transform.position = BaseSpawnPoint(); // 2.3: drop out near the map objectives, in a varied spot (not dead centre)
        player.AddComponent<PlayerController>();

        var gm = new GameObject("GameManager");
        gm.transform.SetParent(World);
        gm.AddComponent<GameManager>();

        var dbg = new GameObject("DebugOverlay");
        dbg.transform.SetParent(World);
        dbg.AddComponent<DebugOverlay>();
    }

    /// <summary>Tear the whole world down (back to the main menu).</summary>
    public static void DestroyWorld()
    {
        if (World != null) Object.Destroy(World.gameObject);
        World = null;
        HasBaseSpawn = false;
    }

    /// <summary>The player's (re)spawn point: beside the starter base when there is one,
    /// otherwise a random standing spot. Keeps respawns from dropping you across the map.</summary>
    public static Vector3 PlayerSpawn() => HasBaseSpawn ? BaseSpawn : RandomSpawnPoint();

    /// <summary>Pre-built starter base near the player's spawn: a dispenser ringed by
    /// a few walls, instantly built (full health). For fresh games only — callers
    /// skip it when continuing a save, for co-op clients (the host streams its own
    /// buildings) and in PvP.</summary>
    public static void BuildStarterBase(Vector3 nearSpawn, PlayerController owner)
    {
        // Remember this spot as the (re)spawn point so the player always returns to the base.
        BaseSpawn = nearSpawn;
        HasBaseSpawn = true;

        GameRoot.BaseLost = false; // fresh base — clear any previous defeat

        Vector3 c = nearSpawn + new Vector3(0f, 0f, 6f); // a few metres off the spawn so the player isn't inside it

        Buildable Place(int type, Vector3 p, float yaw)
        {
            var go = Buildable.Create(type, new Vector3(p.x, Hill(p.x, p.z), p.z), Quaternion.Euler(0f, yaw, 0f), owner);
            var b = go != null ? go.GetComponent<Buildable>() : null;
            if (b != null) b.LoadState(1, 9999f, 0); // instantly built, clamped to full health
            return b;
        }

        var disp = Place(1, c, 0f) as Dispenser;         // dispenser at the centre (heals + metal)
        if (disp != null) disp.Critical = true;          // the base lifeline — lose it and the game ends
        float r = 4f;
        Place(3, c + new Vector3(0f, 0f, r), 0f);        // back wall
        Place(3, c + new Vector3(r, 0f, 0f), 90f);       // right wall
        Place(3, c + new Vector3(-r, 0f, 0f), 90f);      // left wall (front left open as a doorway)
    }

    public const float MapSize = 500f;     // square map side (units)
    public const float HillAmp = 3.5f;
    public const float RiverX = 40f;       // trench centreline (runs along Z, off to one side)
    public const float RiverHalf = 10f;    // half-width of the trench
    public const float WaterLevel = -0.8f; // default water surface height

    /// <summary>Per-map terrain shape, palette and atmosphere. Picked by MapVariant
    /// (an int, also synced over the network so co-op/PvP peers share the same map).</summary>
    public struct MapDef
    {
        public string name;
        public float amp;        // terrain height amplitude
        public float freq;       // Perlin frequency (smaller = broader features)
        public float ridge;      // extra sharp ridged-noise amplitude (0 = smooth rolling)
        public bool channel;     // carve a trench along RiverX (river / canyon)
        public float channelBed; // trench floor height at the centreline
        public int waterPlane;   // 0 none, 1 narrow river strip, 2 full-map sea
        public float water;      // water surface height
        public Color floor;      // ground colour
        public Color waterTint;  // water plane colour
        public int trees;        // scattered prop count
        public int treeStyle;    // 0 forest, 1 snowy conifer, 2 cactus, 3 dead, 4 palm
        public Color ambient;    // RenderSettings.ambientLight
        public Color sun;        // directional light colour
        public float sunInt;     // sun intensity
        public Color fog;        // fog colour
        public float fogDensity; // exponential-squared fog density (0 = no fog)
    }

    // 0..2 are the original Forest / Hills / Arena; 3..7 are the new maps.
    public static readonly MapDef[] Maps =
    {
        new MapDef { name=Lang.T("Лес","Forest"), amp=3.5f, freq=0.025f, ridge=0f, channel=true, channelBed=-2f, waterPlane=1, water=-0.8f,
            floor=new Color(0.22f,0.38f,0.18f), waterTint=new Color(0.2f,0.45f,0.85f,0.6f), trees=220, treeStyle=0,
            ambient=new Color(0.35f,0.37f,0.4f), sun=new Color(1f,0.96f,0.86f), sunInt=1.1f, fog=new Color(0.6f,0.7f,0.6f), fogDensity=0f },

        new MapDef { name=Lang.T("Холмы","Hills"), amp=9f, freq=0.02f, ridge=0f, channel=true, channelBed=-2f, waterPlane=1, water=-0.8f,
            floor=new Color(0.25f,0.42f,0.20f), waterTint=new Color(0.2f,0.45f,0.85f,0.6f), trees=220, treeStyle=0,
            ambient=new Color(0.35f,0.37f,0.4f), sun=new Color(1f,0.96f,0.86f), sunInt=1.1f, fog=new Color(0.6f,0.7f,0.6f), fogDensity=0f },

        new MapDef { name=Lang.T("Арена","Arena"), amp=0.5f, freq=0.03f, ridge=0f, channel=false, channelBed=0f, waterPlane=0, water=-0.8f,
            floor=new Color(0.55f,0.50f,0.32f), waterTint=new Color(0.2f,0.45f,0.85f,0.6f), trees=60, treeStyle=0,
            ambient=new Color(0.4f,0.4f,0.42f), sun=new Color(1f,0.97f,0.9f), sunInt=1.15f, fog=new Color(0.7f,0.7f,0.65f), fogDensity=0f },

        new MapDef { name=Lang.T("Пустыня","Desert"), amp=4f, freq=0.014f, ridge=1.4f, channel=false, channelBed=0f, waterPlane=0, water=-0.8f,
            floor=new Color(0.80f,0.69f,0.42f), waterTint=new Color(0.2f,0.45f,0.85f,0.6f), trees=40, treeStyle=2,
            ambient=new Color(0.52f,0.46f,0.34f), sun=new Color(1f,0.92f,0.72f), sunInt=1.3f, fog=new Color(0.86f,0.78f,0.58f), fogDensity=0.006f },

        new MapDef { name=Lang.T("Снег","Snow"), amp=4f, freq=0.022f, ridge=0.6f, channel=true, channelBed=-1.5f, waterPlane=1, water=-0.8f,
            floor=new Color(0.86f,0.89f,0.93f), waterTint=new Color(0.6f,0.78f,0.85f,0.6f), trees=110, treeStyle=1,
            ambient=new Color(0.55f,0.58f,0.62f), sun=new Color(0.85f,0.9f,1f), sunInt=1.0f, fog=new Color(0.82f,0.86f,0.92f), fogDensity=0.008f },

        new MapDef { name=Lang.T("Каньон","Canyon"), amp=6f, freq=0.02f, ridge=2.5f, channel=true, channelBed=-6f, waterPlane=0, water=-5f,
            floor=new Color(0.56f,0.34f,0.22f), waterTint=new Color(0.2f,0.45f,0.85f,0.6f), trees=28, treeStyle=3,
            ambient=new Color(0.45f,0.38f,0.32f), sun=new Color(1f,0.88f,0.7f), sunInt=1.2f, fog=new Color(0.7f,0.55f,0.42f), fogDensity=0.004f },

        new MapDef { name=Lang.T("Острова","Islands"), amp=3f, freq=0.03f, ridge=0.4f, channel=false, channelBed=0f, waterPlane=2, water=0.9f,
            floor=new Color(0.2f,0.46f,0.24f), waterTint=new Color(0.15f,0.5f,0.7f,0.55f), trees=130, treeStyle=4,
            ambient=new Color(0.42f,0.48f,0.5f), sun=new Color(1f,0.97f,0.85f), sunInt=1.2f, fog=new Color(0.55f,0.72f,0.78f), fogDensity=0.004f },

        new MapDef { name=Lang.T("Горы","Mountains"), amp=12f, freq=0.015f, ridge=3f, channel=true, channelBed=-2f, waterPlane=1, water=-0.8f,
            floor=new Color(0.5f,0.5f,0.53f), waterTint=new Color(0.3f,0.5f,0.75f,0.6f), trees=80, treeStyle=1,
            ambient=new Color(0.45f,0.47f,0.52f), sun=new Color(0.95f,0.96f,1f), sunInt=1.05f, fog=new Color(0.7f,0.74f,0.8f), fogDensity=0.005f },
    };

    // Selectable map index (clamped on read). Networked as an int for co-op/PvP.
    public static int MapVariant = 0;
    public static MapDef Cur => Maps[Mathf.Clamp(MapVariant, 0, Maps.Length - 1)];
    public static int MapCount => Maps.Length;
    public static bool MapHasRiver => Cur.channel; // kept for back-compat (trench presence)

    /// <summary>Terrain height at a world (x,z): Perlin hills plus optional ridged noise
    /// (dunes / canyon walls / mountains), with a carved trench on river/canyon maps.</summary>
    public static float Hill(float x, float z)
    {
        var m = Cur;
        float h = (Mathf.PerlinNoise(x * m.freq + 100f, z * m.freq + 100f) - 0.5f) * 2f * m.amp;
        if (m.ridge > 0f)
        {
            // Ridged noise: |0.5 - noise| inverted then squared → sharp crests/valleys.
            float r = 1f - Mathf.Abs(Mathf.PerlinNoise(x * m.freq * 2f + 500f, z * m.freq * 2f + 500f) - 0.5f) * 2f;
            h += (r * r) * m.ridge;
        }
        if (m.channel)
        {
            float d = Mathf.Abs(x - RiverX);
            if (d < RiverHalf)
            {
                float edge = Mathf.SmoothStep(m.channelBed, h, d / RiverHalf); // bed at centre → terrain at banks
                h = Mathf.Min(h, edge);
            }
        }
        return h;
    }

    /// <summary>Map-centre spawn for the start of a game — the insertion drop lands here.
    /// On island maps the centre may be sea, so spiral outward to the nearest dry land.</summary>
    public static Vector3 CenterSpawnPoint()
    {
        var m = Cur;
        float x = 0f, z = 0f;
        if (m.waterPlane == 2 && Hill(0f, 0f) < m.water + 0.5f)
        {
            for (float r = 8f; r <= 140f; r += 8f)
            {
                for (int a = 0; a < 12; a++)
                {
                    float ang = a * (Mathf.PI / 6f);
                    float cx = Mathf.Cos(ang) * r, cz = Mathf.Sin(ang) * r;
                    if (Hill(cx, cz) > m.water + 0.5f) { x = cx; z = cz; r = 999f; break; }
                }
            }
        }
        return new Vector3(x, Hill(x, z) + 1.5f, z);
    }

    /// <summary>2.3 base drop: a RANDOM standing spot out on the ring where the map objectives
    /// (refineries / mines) sit — so the starter base lands in a different place each game and
    /// near the buildings, instead of dead centre. Avoids the river trench and the sea.</summary>
    public static Vector3 BaseSpawnPoint()
    {
        var m = Cur;
        float half = MapSize * 0.4f;
        for (int t = 0; t < 40; t++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(48f, 66f);   // out near the objective ring, not the centre
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (Mathf.Abs(x) > half || Mathf.Abs(z) > half) continue;
            bool inTrench = m.channel && Mathf.Abs(x - RiverX) < RiverHalf + 3f;
            bool inSea = m.waterPlane == 2 && Hill(x, z) < m.water + 0.5f;
            if (inTrench || inSea) continue;
            return new Vector3(x, Hill(x, z) + 1.5f, z);
        }
        return CenterSpawnPoint(); // fallback: never fail to place the player
    }

    /// <summary>A random standing point on the map (off the edges and out of the river).</summary>
    public static Vector3 RandomSpawnPoint()
    {
        var m = Cur;
        float half = MapSize * 0.4f;
        float x = 0f, z = 0f;
        for (int t = 0; t < 24; t++)
        {
            x = Random.Range(-half, half);
            z = Random.Range(-half, half);
            bool inTrench = m.channel && Mathf.Abs(x - RiverX) < RiverHalf + 1.5f;
            bool inSea = m.waterPlane == 2 && Hill(x, z) < m.water + 0.5f; // islands: stay on dry land
            if (!inTrench && !inSea) break;
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
        SetColor(go, Cur.floor);
    }

    static void BuildWater()
    {
        var m = Cur;
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Water";
        water.transform.SetParent(World);
        Object.Destroy(water.GetComponent<Collider>()); // walk into water freely
        if (m.waterPlane == 2) // full-map sea (islands)
        {
            water.transform.position = new Vector3(0f, m.water, 0f);
            water.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f); // 10u plane base
        }
        else // narrow river strip running along Z at RiverX
        {
            water.transform.position = new Vector3(RiverX, m.water, 0f);
            water.transform.localScale = new Vector3((RiverHalf * 2f) / 10f, 1f, MapSize / 10f);
        }
        MakeGhost(water, m.waterTint); // translucent water tint
    }

    // Scatter prop: shape depends on the map's tree style (forest mix, snowy conifer,
    // desert cactus, dead canyon snag, tropical palm). Seed keeps layouts stable.
    // Kenney "Nature Kit" CC0 grass/flower tufts, scattered with a wind-sway shader.
    static readonly string[] _grassModels =
        { "Grass/grass", "Grass/grass_large", "Grass/grass_leafs", "Grass/grass",
          "Grass/grass_leafs", "Grass/flower_yellowA", "Grass/flower_redA", "Grass/plant_bushSmall" };
    static bool _grassFailed;

    static void ScatterGrass(MapDef m)
    {
        if (_grassFailed) return;
        // Use the grass models' OWN imported materials. The custom wind shader the dad added
        // is stripped out of the URP build (variants → 0) and renders magenta, so we don't
        // apply it. (Trade-off: no wind sway, but build-safe and correctly coloured.)
        var root = new GameObject("Grass").transform;
        root.SetParent(World);

        const int count = 650;
        for (int i = 0; i < count; i++)
        {
            float ang = i * 2.39996f;            // golden-angle scatter
            float r = 5f + (i % 42) * 1.8f;      // out to ~80 m around the base
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (m.channel && Mathf.Abs(x - RiverX) < RiverHalf + 1f) continue; // keep the trench clear
            float y = Hill(x, z);
            if (y < m.water + 0.25f) continue;   // no underwater grass

            var prefab = Resources.Load<GameObject>(_grassModels[(i * 7) % _grassModels.Length]);
            if (prefab == null) { _grassFailed = true; return; }
            var go = Object.Instantiate(prefab, root);
            go.transform.position = new Vector3(x, y, z);
            go.transform.rotation = Quaternion.Euler(0f, (i * 53) % 360, 0f);
            go.transform.localScale = Vector3.one * (3f + (i % 4)); // ~0.75–1.5 m tufts
            foreach (var rr in go.GetComponentsInChildren<Renderer>())
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // perf: grass casts no shadows
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        }
    }

    // ── Apocalyptic wreckage scattered around the map (procedural, decorative) ──
    static void ScatterWreckage(MapDef m)
    {
        var root = new GameObject("Wreckage").transform;
        root.SetParent(World);
        const int clusters = 16;
        for (int i = 0; i < clusters; i++)
        {
            float ang = i * 2.39996f + 0.5f;
            float r = 22f + (i % 8) * 9f; // ring 22..85 m (keeps the base area clear)
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (m.channel && Mathf.Abs(x - RiverX) < RiverHalf + 3f) continue;
            float y = Hill(x, z);
            if (y < m.water + 0.3f) continue;
            var pos = new Vector3(x, y, z);
            float yaw = (i * 67) % 360;
            switch (i % 3)
            {
                case 0: BuildCarWreck(root, pos, yaw); break;
                case 1: BuildRubble(root, pos, yaw, i); break;
                default: BuildPipes(root, pos, yaw); break;
            }
        }
    }

    static void WreckPiece(Transform parent, PrimitiveType t, Vector3 lp, Vector3 ls, Color c, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        Object.Destroy(g.GetComponent<Collider>()); // decorative — never blocks movement/pathing
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = ls;
        SetColor(g, c);
    }

    static void BuildCarWreck(Transform parent, Vector3 pos, float yaw)
    {
        var go = new GameObject("CarWreck").transform;
        go.SetParent(parent);
        go.position = pos;
        go.rotation = Quaternion.Euler(Random.Range(-5f, 5f), yaw, Random.Range(-7f, 7f)); // tilted, abandoned
        Color rust = new Color(0.42f, 0.26f, 0.17f);
        Color burnt = new Color(0.13f, 0.12f, 0.12f);
        WreckPiece(go, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(2.0f, 0.7f, 4.2f), rust);          // chassis
        WreckPiece(go, PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.3f), new Vector3(1.8f, 0.6f, 2.0f), burnt);      // crushed cabin
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(-1.0f, 0.3f, 1.4f), new Vector3(0.5f, 0.18f, 0.5f), burnt, new Vector3(0f, 0f, 90f));
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(1.0f, 0.3f, 1.4f), new Vector3(0.5f, 0.18f, 0.5f), burnt, new Vector3(0f, 0f, 90f));
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(-1.0f, 0.3f, -1.6f), new Vector3(0.5f, 0.18f, 0.5f), burnt, new Vector3(0f, 0f, 90f)); // one wheel missing
    }

    static void BuildRubble(Transform parent, Vector3 pos, float yaw, int seed)
    {
        var go = new GameObject("Rubble").transform;
        go.SetParent(parent);
        go.position = pos;
        go.rotation = Quaternion.Euler(0f, yaw, 0f);
        Color concrete = new Color(0.48f, 0.46f, 0.43f);
        Color dark = new Color(0.30f, 0.29f, 0.27f);
        WreckPiece(go, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(0.4f, 2.4f, 3.0f), concrete, new Vector3(0f, 0f, 14f));   // leaning wall fragment
        WreckPiece(go, PrimitiveType.Cube, new Vector3(1.2f, 0.6f, 1.0f), new Vector3(0.4f, 1.2f, 1.6f), concrete, new Vector3(0f, 30f, -8f));
        for (int k = 0; k < 6; k++)
        {
            float a = seed * 13 + k * 61;
            Vector3 lp = new Vector3(Mathf.Cos(a) * 1.8f, 0.2f, Mathf.Sin(a) * 1.8f);
            WreckPiece(go, PrimitiveType.Cube, lp, Vector3.one * Random.Range(0.3f, 0.7f), (k % 2 == 0) ? dark : concrete,
                new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)));
        }
    }

    static void BuildPipes(Transform parent, Vector3 pos, float yaw)
    {
        var go = new GameObject("Pipes").transform;
        go.SetParent(parent);
        go.position = pos;
        go.rotation = Quaternion.Euler(0f, yaw, 0f);
        Color metal = new Color(0.34f, 0.36f, 0.38f);
        Color rust = new Color(0.40f, 0.27f, 0.18f);
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 1.6f, 0.3f), metal, new Vector3(90f, 0f, 0f));     // pipe lying along Z
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(0.9f, 0.3f, 0.4f), new Vector3(0.28f, 1.3f, 0.28f), rust, new Vector3(90f, 22f, 0f));
        WreckPiece(go, PrimitiveType.Cylinder, new Vector3(-0.7f, 0.5f, -0.4f), new Vector3(0.26f, 1.0f, 0.26f), metal, new Vector3(68f, -15f, 0f));
    }

    // Kenney "Nature Kit" CC0 tree models (vertex-coloured) used for forest/snow maps.
    static readonly string[] _treeModels =
        { "Trees/tree_default", "Trees/tree_oak", "Trees/tree_detailed",
          "Trees/tree_pineRoundA", "Trees/tree_pineDefaultA", "Trees/tree_fat" };
    static bool _treeModelsFailed;

    static void BuildTree(Vector3 pos, int seed, int style)
    {
        var root = new GameObject("Tree");
        root.transform.SetParent(World);
        root.transform.position = new Vector3(pos.x, Hill(pos.x, pos.z), pos.z);
        root.transform.rotation = Quaternion.Euler(0f, (seed * 57) % 360, 0f);

        float js = 0.85f + ((seed * 17) % 35) * 0.01f; // 0.85..1.19 size jitter

        // Forest/snow maps: use the nicer Kenney models; cactus/dead/palm keep the procedural shapes.
        if ((style == 0 || style == 1) && TrySpawnModelTree(root, seed, js)) return;

        switch (style)
        {
            case 1: BuildConifer(root, seed, js); break;  // snow / mountains
            case 2: BuildCactus(root, seed, js); break;   // desert
            case 3: BuildDeadTree(root, seed, js); break; // canyon
            case 4: BuildPalm(root, seed, js); break;     // tropical islands
            default: BuildForestTree(root, seed, js); break;
        }
    }

    // Instantiate a Kenney CC0 tree model using its OWN imported materials (trunk + foliage
    // are separate URP-Lit materials baked from the FBX — build-safe, never magenta). The
    // earlier code replaced them with a custom vertex-colour shader that URP strips out of
    // the build → pink. Returns false (→ procedural fallback) only if a model is missing.
    static bool TrySpawnModelTree(GameObject root, int seed, float js)
    {
        if (_treeModelsFailed) return false;
        var prefab = Resources.Load<GameObject>(_treeModels[(seed * 13) % _treeModels.Length]);
        if (prefab == null) { _treeModelsFailed = true; return false; }

        var go = Object.Instantiate(prefab, root.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * (2.6f * js); // Kenney trees ~1u → scale to game size
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c); // decorative only
        return true;
    }

    // Original five forest shapes: round deciduous, conifer, slim tall, bush, autumn.
    static void BuildForestTree(GameObject root, int seed, float js)
    {
        int kind = seed % 5;
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

    // A visual cube limb (branch / cactus arm / palm frond) — no collider.
    static void TreeLimb(GameObject root, Vector3 localPos, Vector3 scale, Vector3 euler, Color c)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = localPos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        SetColor(g, c);
    }

    // Snowy conifer: brown trunk, dark-green stacked tiers with snow-capped top tiers.
    static void BuildConifer(GameObject root, int seed, float js)
    {
        TreeTrunk(root, 1.8f * js, 0.3f * js, new Color(0.3f, 0.22f, 0.13f));
        Color pine = new Color(0.14f, 0.32f, 0.2f);
        Color snow = new Color(0.9f, 0.93f, 0.96f);
        for (int i = 0; i < 5; i++)
        {
            Color c = i >= 3 ? snow : pine; // upper (smaller) tiers wear snow
            TreeLeaf(root, new Vector3(0f, (3.0f + i * 0.85f) * js, 0f), (2.3f - i * 0.42f) * js, c, 0.8f);
        }
    }

    // Desert cactus: a thick green column (keeps a collider) with an optional raised arm.
    static void BuildCactus(GameObject root, int seed, float js)
    {
        Color green = new Color(0.24f, 0.44f, 0.26f);
        TreeTrunk(root, 1.5f * js, 0.4f * js, green); // column
        if (seed % 3 != 0)
        {
            int dir = (seed % 2 == 0) ? 1 : -1;
            TreeLimb(root, new Vector3(0.5f * js * dir, 1.5f * js, 0f), new Vector3(0.3f * js, 0.28f * js, 0.28f * js), Vector3.zero, green); // out
            TreeLimb(root, new Vector3(0.66f * js * dir, 2.1f * js, 0f), new Vector3(0.28f * js, 1.0f * js, 0.28f * js), Vector3.zero, green); // up
        }
    }

    // Canyon snag: a bare leaning trunk with a couple of dead branches, no foliage.
    static void BuildDeadTree(GameObject root, int seed, float js)
    {
        Color dead = new Color(0.34f, 0.27f, 0.2f);
        TreeTrunk(root, 1.8f * js, 0.26f * js, dead);
        TreeLimb(root, new Vector3(0.45f * js, 3.0f * js, 0f), new Vector3(0.12f, 1.2f * js, 0.12f), new Vector3(0f, 0f, 52f), dead);
        TreeLimb(root, new Vector3(-0.4f * js, 3.4f * js, 0.2f), new Vector3(0.1f, 1.0f * js, 0.1f), new Vector3(18f, 0f, -48f), dead);
    }

    // Tropical palm: tall slim trunk topped with big flat green fronds radiating out.
    static void BuildPalm(GameObject root, int seed, float js)
    {
        Color trunk = new Color(0.45f, 0.36f, 0.22f);
        Color frond = new Color(0.2f, 0.5f, 0.22f);
        TreeTrunk(root, 3.0f * js, 0.22f * js, trunk);
        for (int i = 0; i < 6; i++)
            TreeLimb(root, new Vector3(0f, 6.0f * js, 0f), new Vector3(0.18f, 0.08f, 2.4f * js), new Vector3(32f, i * 60f, 0f), frond);
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
        // A soft specular sheen so surfaces catch the sun (less flat, more "juicy"/stylized).
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
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
            // Swap to a fresh UNLIT material forced into alpha-blend mode. URP/Lit frequently refuses
            // to go transparent when only its properties are poked at runtime (stays opaque) — a plain
            // unlit transparent material is reliable and is all a placement ghost needs.
            var m = new Material(LineShader());
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
            r.material = m;
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
