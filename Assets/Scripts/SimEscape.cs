using UnityEngine;

/// <summary>
/// PLAYABLE post-credits epilogue (ARG). Runs after the intercept text stinger
/// (ExperimentEpilogue). The twist: the whole zombie campaign was a SIMULATION and
/// the player is the test subject. Here you wake inside the simulation cube, it
/// powers down, you walk out through the lab looking for the exit, and step out
/// into the real world — a barren planet in deep space.
///
/// Fully self-contained and ISOLATED from the game: it tears down the (nuked) game
/// world, builds its own primitive environment + its own lightweight FPS walker
/// (EpilogueWalker), and parks GameRoot in the neutral GState.Epilogue so no menu
/// or game system fights it. Returns to the main menu when done. No prefabs/assets
/// (except an optional Resources/space_sky image for the deep-space backdrop —
/// falls back to a dark void if absent). 2026-07-11.
/// Stages: 1 cube+control · 2 lab walk · 3 deep-space reveal.
/// </summary>
public class SimEscape : MonoBehaviour
{
    public static bool Active;

    public static void Begin()
    {
        if (Active) return;
        if (GameRoot.IsPvp) return;
        if (LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost) return;
        new GameObject("SimEscape").AddComponent<SimEscape>();
    }

    enum Phase { BootFade, CubeOpen, Lab, ExitOpen, Space, Done }
    Phase phase = Phase.BootFade;
    float pt;                       // seconds in current phase
    float fade = 1f;                // 1 = black
    string subRu = "", subEn = "";
    Texture2D spaceSky;

    Transform root, cubeDoor, exitDoor, spaceFill;
    EpilogueWalker walker;

    const float CubeZ = 3.2f;       // cube front wall / door plane
    const float ExitZ = 36f;        // exit door plane
    Vector3 exitTrigger;
    bool sawCube0, sawLog, bigText, finished;
    Vector3 _finaleSpawn; bool _hasFinaleSpawn; float _finaleYaw;   // спавн+угол импортнутой карты-лабы в финале

    void Awake() { Active = true; }

    void Start()
    {
        // Clean slate: tear down the nuked map + any surviving cameras/listeners
        // (the endgame cutscene detached the player camera, so it survives DestroyWorld).
        GameBootstrap.DestroyWorld();
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
        foreach (var a in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) Destroy(a);

        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.08f);
        // КОСМОС: подложка на процедурном звёздном поле (LineShader переживает стрижку шейдеров,
        // в отличие от Skybox/Panoramic который выстригался → «ничего не менялось»). 2026-07-13.
        spaceSky = MakeStarfield();

        root = new GameObject("EpilogueWorld").transform;

        // ИМПОРТ КАРТЫ-ЛАБЫ В ФИНАЛ: грузим первый .vmf из папки maps рядом с игрой (та же,
        // что F10) в мир эпилога + оживляем энтити рантаймом. Аддитивно, поверх сцены. 2026-07-13.
        try
        {
            string mp = FinaleMapPath();
            if (mp != null)
            {
                var mapRoot = new GameObject("FinaleMap").transform;
                mapRoot.SetParent(root, false);
                var mres = VmfImporter.Import(System.IO.File.ReadAllText(mp), mapRoot);
                VmfRuntime.Ensure(mapRoot);
                // ТЕЛЕПОРТ игрока НА спавн лабы, чтобы финал был В карте (а не в старой сцене).
                if (mres.hasSpawn) { _finaleSpawn = mres.spawn; _hasFinaleSpawn = true; _finaleYaw = mres.spawnYaw; }
            }
        }
        catch (System.Exception _me) { Debug.LogWarning("Финал: импорт VMF не удался: " + _me.Message); }
        // Импортнулась КАРТА-ЛАБА → строим ТОЛЬКО её (старую процедурную сцену финала — куб/пол/
        // старую лабу/космо-декор — НЕ строим). Иначе (нет карты) — обычный процедурный финал. 2026-07-13.
        if (!_hasFinaleSpawn)
        {
            BuildFloor();
            BuildCube();
            BuildLab();
            BuildSpace();
        }
        SpawnWalker();

        exitTrigger = new Vector3(0f, 1f, ExitZ - 2.4f);

        if (GameRoot.Instance != null) GameRoot.Instance.EnterEpilogue();
        SetSub("// СБОЙ ИЗОЛЯЦИИ · ПРОБУЖДЕНИЕ СУБЪЕКТА //", "// CONTAINMENT FAILURE · SUBJECT WAKE //");

        // Финал В КАРТЕ-ЛАБЕ: если импортнулась со спавном — ставим игрока туда (перекрывает
        // дефолтную позицию сцены-эпилога, чтобы F9 грузил именно лабу, а не старый куб). 2026-07-13.
        if (_hasFinaleSpawn)
        {
            transform.position = _finaleSpawn;
            transform.rotation = Quaternion.Euler(0f, _finaleYaw, 0f); // лицом на лабу (угол спавна)
            if (walker != null) { walker.transform.position = _finaleSpawn; walker.transform.rotation = Quaternion.Euler(0f, _finaleYaw, 0f); }
            // СВЕТ лабе: у карты нет своих ламп → в тёмном космо-финале была бы чёрной. Поднимаем
            // ambient + добавляем направленный свет, чтобы текстуры/цвета лабы были видны (как в игре).
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.50f);
            RenderSettings.fog = false;
            var lgo = new GameObject("FinaleLabSun");
            var lt = lgo.AddComponent<Light>();
            lt.type = LightType.Directional; lt.color = new Color(1f, 0.97f, 0.9f); lt.intensity = 1.15f;
            lt.shadows = LightShadows.Soft;
            lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            // ТРИГГЕР-КОНЕЦ: игрок вошёл в trigger с именем titry/exit/выход (ставишь в Hammer «на
            // улице») → показываем титры «ПРОДОЛЖЕНИЕ СЛЕДУЕТ» → возврат в меню. 2026-07-13.
            VmfRuntime.OnEndZone = () => { if (phase != Phase.Space && phase != Phase.Done) Go(Phase.Space); };
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        pt += dt;

        // Universal skip — never trap the player in the epilogue.
        if (Input.GetKeyDown(KeyCode.Escape) && phase != Phase.Done) Go(Phase.Done);

        switch (phase)
        {
            case Phase.BootFade:
                fade = Mathf.MoveTowards(fade, 0f, dt / 2.5f);
                if (pt > 3.2f)
                {
                    SetSub("Найди выход.", "Find the way out.");
                    Go(Phase.CubeOpen);
                }
                break;

            case Phase.CubeOpen:
                // The front wall of the cube retracts up into the ceiling.
                if (cubeDoor != null)
                {
                    Vector3 p = cubeDoor.localPosition;
                    p.y = Mathf.MoveTowards(p.y, 7f, 3.2f * dt);
                    cubeDoor.localPosition = p;
                }
                if (walker != null && pt > 0.6f) walker.CanMove = true; // hand over control as it lifts
                if (pt > 2.4f) { ClearSub(); Go(Phase.Lab); }
                break;

            case Phase.Lab:
                LabAmbient();
                if (PlanarDist(Feet(), exitTrigger) < 3f) Go(Phase.ExitOpen);
                break;

            case Phase.ExitOpen:
                // Tint the sky deep-space (not pure black) so looking away from the vista still reads as space.
                if (walker != null && walker.Cam != null)
                    walker.Cam.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
                if (exitDoor != null)
                {
                    Vector3 p = exitDoor.localPosition;
                    p.y = Mathf.MoveTowards(p.y, 7f, 3.0f * dt);
                    exitDoor.localPosition = p;
                }
                if (spaceFill != null)
                {
                    var l = spaceFill.GetComponent<Light>();
                    if (l != null) l.intensity = Mathf.MoveTowards(l.intensity, 1.1f, dt * 0.6f);
                }
                if (pt > 2.2f || Feet().z > ExitZ + 0.5f) Go(Phase.Space);
                break;

            case Phase.Space:
                if (pt > 1.5f && string.IsNullOrEmpty(subRu)) SetSub("Глубокий космос.", "Deep space.");
                if (pt > 5f) bigText = true;
                if (pt > 11f) Go(Phase.Done);
                break;

            case Phase.Done:
                fade = Mathf.MoveTowards(fade, 1f, dt / 2f);
                if (fade >= 1f && !finished)
                {
                    finished = true;
                    Active = false;
                    if (root != null) Destroy(root.gameObject);
                    if (GameRoot.Instance != null) GameRoot.Instance.ReturnToMenuFromEpilogue();
                    Destroy(gameObject);
                }
                break;
        }
    }

    void Go(Phase p) { phase = p; pt = 0f; }

    // Environmental one-liners as the player passes set-dressing in the lab.
    void LabAmbient()
    {
        float z = Feet().z;
        if (!sawCube0 && z > 10f && z < 16f) { sawCube0 = true; SetSub("КАМЕРА 0-Г · СУБЪЕКТ ПРЕКРАЩЁН", "CELL 0-G · SUBJECT TERMINATED"); }
        else if (!sawLog && z > 22f && z < 30f) { sawLog = true; SetSub("ЖУРНАЛ: волна 12 — предел. Ты дошёл дальше.", "LOG: wave 12 — the limit. You went further."); }
        else if ((sawLog && z > 31f) || (sawCube0 && z > 16f && z < 22f)) { /* let last subtitle linger, cleared on exit */ }
    }

    // ---------- build ----------
    Transform Box(Vector3 pos, Vector3 scale, Color c, bool collide)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (!collide) Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
        return g.transform;
    }

    void PointLight(Vector3 pos, Color c, float range, float intensity)
    {
        var g = new GameObject("L");
        g.transform.SetParent(root, false);
        g.transform.localPosition = pos;
        var l = g.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
    }

    void BuildFloor()
    {
        // One long floor slab (top at y=0) spanning cube + corridor.
        Box(new Vector3(0f, -0.25f, 15.5f), new Vector3(8.2f, 0.5f, 46f), new Color(0.09f, 0.10f, 0.11f), true);
    }

    void BuildCube()
    {
        Color dark = new Color(0.10f, 0.11f, 0.13f);
        Color panel = new Color(0.13f, 0.15f, 0.18f);
        Box(new Vector3(0f, 1.5f, -CubeZ), new Vector3(7f, 3f, 0.4f), dark, true);       // back
        Box(new Vector3(-CubeZ, 1.5f, 0f), new Vector3(0.4f, 3f, 7f), dark, true);        // left
        Box(new Vector3(CubeZ, 1.5f, 0f), new Vector3(0.4f, 3f, 7f), dark, true);         // right
        Box(new Vector3(0f, 3.1f, 0f), new Vector3(7.2f, 0.4f, 7.2f), dark, true);        // ceiling
        cubeDoor = Box(new Vector3(0f, 1.5f, CubeZ), new Vector3(7f, 3f, 0.35f), panel, true); // FRONT — retracts
        // a cold interior glow strip
        var strip = Box(new Vector3(0f, 2.95f, 0f), new Vector3(3.2f, 0.08f, 0.5f), new Color(0.5f, 0.7f, 0.95f), false);
        GameBootstrap.SetColor(strip.gameObject, new Color(0.5f, 0.7f, 0.95f));
        PointLight(new Vector3(0f, 2.6f, 0f), new Color(0.45f, 0.6f, 0.85f), 8f, 0.9f);
    }

    void BuildLab()
    {
        Color dark = new Color(0.11f, 0.12f, 0.14f);
        Color bright = new Color(0.85f, 0.9f, 1f);
        float midZ = (CubeZ + ExitZ) * 0.5f, len = ExitZ - CubeZ + 0.4f;
        Box(new Vector3(-3.7f, 1.7f, midZ), new Vector3(0.4f, 3.6f, len), dark, true);    // left wall
        Box(new Vector3(3.7f, 1.7f, midZ), new Vector3(0.4f, 3.6f, len), dark, true);     // right wall
        Box(new Vector3(0f, 3.4f, midZ), new Vector3(7.8f, 0.4f, len), dark, true);       // ceiling

        for (float z = CubeZ + 3f; z < ExitZ; z += 7.5f)
        {
            var lp = Box(new Vector3(0f, 3.12f, z), new Vector3(1.4f, 0.1f, 3f), bright, false); // light strip
            GameBootstrap.SetColor(lp.gameObject, bright);
            PointLight(new Vector3(0f, 2.9f, z), new Color(0.8f, 0.85f, 1f), 9f, 1.0f);
        }

        // Dead sim cubes (other subjects) recessed by the walls — set dressing.
        DeadCube(new Vector3(2.9f, 1f, 12f));
        DeadCube(new Vector3(-2.9f, 1f, 26f));
        // A console.
        Box(new Vector3(-2.9f, 0.6f, 20f), new Vector3(0.6f, 1.2f, 1.4f), new Color(0.14f, 0.15f, 0.17f), true);
        var scr = Box(new Vector3(-2.55f, 1.1f, 20f), new Vector3(0.05f, 0.5f, 0.9f), new Color(0.3f, 0.9f, 0.6f), false);
        GameBootstrap.SetColor(scr.gameObject, new Color(0.3f, 0.9f, 0.6f));
    }

    void DeadCube(Vector3 pos)
    {
        Box(pos, new Vector3(1.7f, 2.1f, 1.7f), new Color(0.08f, 0.09f, 0.10f), true);
        float sign = pos.x > 0 ? -1f : 1f; // glass faces the corridor
        var glass = Box(pos + new Vector3(sign * 0.9f, 0f, 0f), new Vector3(0.05f, 1.6f, 1.3f), new Color(0.2f, 0.35f, 0.4f), false);
        GameBootstrap.SetColor(glass.gameObject, new Color(0.2f, 0.35f, 0.4f));
        PointLight(pos + new Vector3(sign * 0.6f, 0.4f, 0f), new Color(0.25f, 0.5f, 0.55f), 3.5f, 0.5f);
    }

    void BuildSpace()
    {
        Color panel = new Color(0.13f, 0.15f, 0.18f);
        exitDoor = Box(new Vector3(0f, 1.7f, ExitZ), new Vector3(7.4f, 3.4f, 0.35f), panel, true); // EXIT — retracts

        // Barren planet ground stretching far out beyond the exit (covers the distant BFG).
        Box(new Vector3(0f, -0.35f, 150f), new Vector3(300f, 0.6f, 300f), new Color(0.06f, 0.055f, 0.05f), true);
        BuildLandscape(); // boulders + horizon ridges so the planet isn't a flat plane

        // Deep-space backdrop: a HUGE unlit textured quad far out, filling the sky, behind the BFG.
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(root, false);
        q.transform.localPosition = new Vector3(0f, 80f, 340f);
        q.transform.localScale = new Vector3(640f, 380f, 1f);
        q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face -Z (toward the corridor)
        // LineShader() is the game's proven unlit path (survives shader-stripping in builds).
        var mat = new Material(GameBootstrap.LineShader());
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // double-sided → immune to quad-facing / culling
        if (spaceSky != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", spaceSky);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", spaceSky);
            mat.mainTexture = spaceSky;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            // Also make it EMISSIVE — so even if the shader resolves to Lit (no light reaches a
            // backdrop at z=340 in the dark space scene), the sky glows its texture bright. This
            // is why the sky was black before. 2026-07-13.
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", spaceSky);
        }
        else
        {
            Color voidc = new Color(0.02f, 0.03f, 0.06f); // fallback: dark void until the user adds space_sky
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", voidc);
            mat.color = voidc;
        }
        q.GetComponent<Renderer>().material = mat;

        // Cold fill light from outside; ramps up as the exit opens.
        var g = new GameObject("SpaceFill");
        g.transform.SetParent(root, false);
        g.transform.localPosition = new Vector3(0f, 12f, ExitZ + 6f);
        var sl = g.AddComponent<Light>();
        sl.type = LightType.Point; sl.color = new Color(0.55f, 0.6f, 0.8f); sl.range = 90f; sl.intensity = 0f;
        spaceFill = g.transform;

        BuildBFG(); // DLC tease: a colossal cannon on the horizon
    }

    // Barren alien-planet relief: scattered boulders across the near surface + a ring of taller
    // ridges/mesas on the horizon, so the ground reads as a real landscape, not a flat slab.
    // Dark rocky tones, solid colliders, a clear-ish path kept in front of the exit. 2026-07-13.
    // Путь к карте для финала — первый .vmf из maps рядом с игрой (как F10).
    static string FinaleMapPath()
    {
        try
        {
            string gameDir = System.IO.Path.GetDirectoryName(Application.dataPath);
            string[] dirs = { System.IO.Path.Combine(gameDir, "maps"), gameDir, Application.persistentDataPath };
            foreach (string d in dirs)
            {
                if (!System.IO.Directory.Exists(d)) continue;
                var f = System.IO.Directory.GetFiles(d, "*.vmf");
                if (f.Length > 0) return f[0];
            }
        }
        catch { }
        return null;
    }

    // Процедурное звёздное поле — тёмный космос + звёзды (тайлится, для космического скайбокса).
    static Texture2D _starTex;
    public static Texture2D MakeStarfield()
    {
        if (_starTex != null) return _starTex;
        int W = 1024, H = 512;
        var t = new Texture2D(W, H, TextureFormat.RGB24, true);
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear;
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(3, 4, 10, 255); // тёмный космос
        var rnd = new System.Random(20260713);
        for (int i = 0; i < 2600; i++)
        {
            int x = rnd.Next(W), y = rnd.Next(H);
            int b = 120 + rnd.Next(135);
            byte bb = (byte)b;
            px[y * W + x] = new Color32(bb, bb, (byte)Mathf.Min(255, b + 15), 255);
            if (rnd.Next(70) == 0) // редкие яркие с ореолом
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = (x + dx + W) % W, ny = (y + dy + H) % H;
                        px[ny * W + nx] = new Color32(205, 210, 235, 255);
                    }
        }
        t.SetPixels32(px); t.Apply();
        _starTex = t;
        return t;
    }

    void BuildLandscape()
    {
        Color r1 = new Color(0.075f, 0.068f, 0.06f);
        Color r2 = new Color(0.055f, 0.052f, 0.055f);

        for (int i = 0; i < 70; i++)
        {
            float x = Random.Range(-130f, 130f);
            float z = Random.Range(46f, 250f);
            if (Mathf.Abs(x) < 6f && z < 72f) continue; // keep the walk-out path clear
            float s = Random.Range(1.4f, 7.5f);
            var b = Box(new Vector3(x, Random.Range(-0.3f, s * 0.3f), z),
                        new Vector3(s, s * Random.Range(0.5f, 1.2f), s * Random.Range(0.7f, 1.3f)),
                        Random.value < 0.5f ? r1 : r2, true);
            b.localEulerAngles = new Vector3(Random.Range(-14f, 14f), Random.Range(0f, 360f), Random.Range(-14f, 14f));
        }

        // Distant ridges/mesas for a broken horizon silhouette against the sky.
        for (int i = 0; i < 10; i++)
        {
            float x = Random.Range(-170f, 170f);
            float z = Random.Range(150f, 300f);
            float w = Random.Range(22f, 55f), h = Random.Range(9f, 30f);
            var r = Box(new Vector3(x, h * 0.4f, z), new Vector3(w, h, Random.Range(12f, 34f)), r2, true);
            r.localEulerAngles = new Vector3(0f, Random.Range(0f, 360f), Random.Range(-7f, 7f));
        }
    }

    // BFG-10000 silhouette (DOOM tribute) — a colossal cannon on the far horizon,
    // dark against the space backdrop with a green energy core glowing in the muzzle.
    // Purely a "what IS that?" tease for the next chapter/DLC — not usable. 2026-07-11.
    void BuildBFG()
    {
        Color sil = new Color(0.03f, 0.035f, 0.05f);   // near-black silhouette
        Color glow = new Color(0.35f, 0.95f, 0.45f);   // BFG green
        Vector3 at = new Vector3(-110f, 0f, 210f);     // FAR off to the left horizon (was too close)

        Prim(PrimitiveType.Cube, at + new Vector3(0f, 3f, 0f), new Vector3(38f, 6f, 30f), Vector3.zero, sil);    // foundation
        Prim(PrimitiveType.Cube, at + new Vector3(0f, 17f, 0f), new Vector3(16f, 28f, 16f), Vector3.zero, sil);  // main body
        Prim(PrimitiveType.Cube, at + new Vector3(-9.5f, 12f, 0f), new Vector3(4f, 22f, 12f), Vector3.zero, sil);// buttress L
        Prim(PrimitiveType.Cube, at + new Vector3(9.5f, 12f, 0f), new Vector3(4f, 22f, 12f), Vector3.zero, sil); // buttress R
        Prim(PrimitiveType.Cube, at + new Vector3(0f, 30f, 0f), new Vector3(11f, 8f, 11f), Vector3.zero, sil);   // turret head

        // Angled barrel on a pivot (tilts up toward the sky).
        var pivot = new GameObject("BFG_Barrel").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = at + new Vector3(0f, 30f, 3f);
        pivot.localRotation = Quaternion.Euler(-34f, 0f, 0f);
        PrimUnder(pivot, PrimitiveType.Cylinder, new Vector3(0f, 11f, 0f), new Vector3(6f, 12f, 6f), sil, false);      // barrel
        PrimUnder(pivot, PrimitiveType.Cylinder, new Vector3(0f, 22f, 0f), new Vector3(7.6f, 1.8f, 7.6f), sil, false); // muzzle ring
        PrimUnder(pivot, PrimitiveType.Sphere, new Vector3(0f, 23.5f, 0f), new Vector3(4.6f, 4.6f, 4.6f), glow, true); // energy core (unlit)

        var lg = new GameObject("BFGGlow");
        lg.transform.SetParent(pivot, false);
        lg.transform.localPosition = new Vector3(0f, 23.5f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = glow; l.range = 70f; l.intensity = 2.4f;
    }

    Transform Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Color c)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root, false);
        g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
        return g.transform;
    }

    void PrimUnder(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, bool unlit)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        if (unlit)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? GameBootstrap.StdShader());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.color = c;
            g.GetComponent<Renderer>().material = mat;
        }
        else GameBootstrap.SetColor(g, c);
    }

    void SpawnWalker()
    {
        var g = new GameObject("EpilogueWalker");
        g.transform.SetParent(root, false);
        g.transform.localPosition = new Vector3(0f, 0.2f, 0f); // inside the cube
        g.transform.localRotation = Quaternion.identity;       // facing +Z (the exit)
        walker = g.AddComponent<EpilogueWalker>();
    }

    Vector3 Feet() { return walker != null ? walker.transform.position : Vector3.zero; }
    static float PlanarDist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    void SetSub(string ru, string en) { subRu = ru; subEn = en; }
    void ClearSub() { subRu = ""; subEn = ""; }

    void OnGUI()
    {
        UI.Begin();
        float w = UI.W, h = UI.H;

        if (!string.IsNullOrEmpty(subRu))
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.color = new Color(0.85f, 0.92f, 0.85f, Mathf.Clamp01(1f - fade * 0.4f));
            GUI.Label(new Rect(w * 0.1f, h - 150f, w * 0.8f, 80f), Lang.T(subRu, subEn), st);
            GUI.color = Color.white;
        }

        if (bigText)
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.9f, 0.92f, 0.95f, 0.9f);
            GUI.Label(new Rect(0, h * 0.5f - 40f, w, 90f), Lang.T("ПРОДОЛЖЕНИЕ СЛЕДУЕТ", "TO BE CONTINUED"), big);
            GUI.color = Color.white;
        }

        if (fade > 0.001f)
        {
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}

/// <summary>
/// Minimal first-person walker for the epilogue — deliberately independent of the
/// game's PlayerController (no weapons/HUD/GameManager coupling). CharacterController
/// + a child camera + mouse-look + WASD + gravity. SimEscape toggles CanMove.
/// Matches the game's input (Input.GetAxis "Mouse X/Y", sensitivity 2.2).
/// </summary>
public class EpilogueWalker : MonoBehaviour
{
    public bool CanMove;
    public Camera Cam;      // exposed so the epilogue can tint the sky in the space scene

    CharacterController cc;
    Transform cam;
    float pitch, vSpeed;
    const float Speed = 3.2f, Sens = 2.2f, Gravity = 20f;

    void Awake()
    {
        cc = gameObject.AddComponent<CharacterController>();
        cc.height = 1.7f; cc.radius = 0.34f; cc.center = new Vector3(0f, 0.85f, 0f);

        var camGO = new GameObject("EpilogueCamera");
        camGO.tag = "MainCamera";
        cam = camGO.transform;
        cam.SetParent(transform, false);
        cam.localPosition = new Vector3(0f, 1.55f, 0f);
        Cam = camGO.AddComponent<Camera>();
        // НЕБО ФИНАЛА: настоящий панорамный скайбокс из space_sky (не чёрная заливка). 2026-07-13.
        Cam.clearFlags = CameraClearFlags.Skybox;
        Cam.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
        // КОСМИЧЕСКОЕ небо финала — процедурное звёздное поле (тёмный космос + звёзды), тайлится,
        // звёзды однородны → «сбоку» не выглядит. Панорамный скайбокс. 2026-07-13.
        // Небо финала — ТЁМНЫЙ КОСМОС (процедурный, всегда в билде; панорама-звёзды выстригалась →
        // оставался синий). Звёзды по кругу даёт звёздный СКАЙДОМ (ниже) + квад-подложка. 2026-07-13.
        var _finSky = Shader.Find("Skybox/Procedural");
        if (_finSky != null)
        {
            var sm = new Material(_finSky);
            if (sm.HasProperty("_SkyTint")) sm.SetColor("_SkyTint", new Color(0.02f, 0.03f, 0.06f));
            if (sm.HasProperty("_GroundColor")) sm.SetColor("_GroundColor", new Color(0.01f, 0.01f, 0.03f));
            if (sm.HasProperty("_AtmosphereThickness")) sm.SetFloat("_AtmosphereThickness", 0.35f);
            if (sm.HasProperty("_Exposure")) sm.SetFloat("_Exposure", 0.55f);
            RenderSettings.skybox = sm;
        }
        // Звёздный СКАЙДОМ вокруг игрока (перевёрнутая сфера, LineShader — не стрижётся) —
        // звёзды ВО ВСЕ СТОРОНЫ, не только квад. Эмиссивный, светится в темноте. 2026-07-13.
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(dome.GetComponent<Collider>());
        dome.name = "StarDome";
        dome.transform.SetParent(cam, false);
        dome.transform.localPosition = Vector3.zero;
        dome.transform.localScale = new Vector3(-600f, 600f, 600f); // отриц. X = нормали внутрь
        var dmat = new Material(GameBootstrap.LineShader());
        var star = SimEscape.MakeStarfield();
        if (dmat.HasProperty("_BaseMap")) dmat.SetTexture("_BaseMap", star);
        if (dmat.HasProperty("_MainTex")) dmat.SetTexture("_MainTex", star);
        dmat.mainTexture = star;
        if (dmat.HasProperty("_Cull")) dmat.SetFloat("_Cull", 0f);
        dmat.EnableKeyword("_EMISSION");
        if (dmat.HasProperty("_EmissionColor")) dmat.SetColor("_EmissionColor", Color.white);
        if (dmat.HasProperty("_EmissionMap")) dmat.SetTexture("_EmissionMap", star);
        dome.GetComponent<Renderer>().material = dmat;
        dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Cam.nearClipPlane = 0.04f;
        Cam.farClipPlane = 900f;
        camGO.AddComponent<AudioListener>();
    }

    void Update()
    {
        if (!CanMove) return;

        float mx = Input.GetAxis("Mouse X") * Sens;
        float my = Input.GetAxis("Mouse Y") * Sens;
        transform.Rotate(0f, mx, 0f, Space.Self);
        pitch = Mathf.Clamp(pitch - my, -85f, 85f);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        float hx = Input.GetAxisRaw("Horizontal");
        float vz = Input.GetAxisRaw("Vertical");
        Vector3 wish = transform.right * hx + transform.forward * vz;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        if (cc.isGrounded && vSpeed < 0f) vSpeed = -2f;
        vSpeed -= Gravity * Time.deltaTime;
        cc.Move((wish * Speed + Vector3.up * vSpeed) * Time.deltaTime);
    }
}
