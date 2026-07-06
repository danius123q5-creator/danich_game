using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// End-game evacuation cutscene (wave 60):
///  1. EVAC   — sky darkens, siren, a chopper descends; the player runs to it through a final crowd.
///  2. LIFTOFF— on boarding the camera detaches from the player and rises high over the base.
///  3. NUKE   — a Tu-22 bomber screams over and drops a nuke: white flash, fireball, mushroom
///             cloud and shockwave level the whole base into PHYSICAL debris (rigidbodies).
///  4. CREDITS— the camera smoothly turns to the departing chopper while the credits roll.
///  5. DONE   — fade out, back to the main menu.
/// </summary>
public class EndgameCinematic : MonoBehaviour
{
    public static bool Active;

    public static void Begin()
    {
        if (Active) return;
        if (GameRoot.IsPvp) return;
        if (LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost) return; // host/SP only
        new GameObject("EndgameCinematic").AddComponent<EndgameCinematic>();
    }

    enum Phase { Evac, Liftoff, Nuke, Credits, Done }
    Phase phase = Phase.Evac;
    float t;                       // seconds in the current phase
    float darkness;               // 0..1 sky-darken amount

    Transform cam;
    PlayerController player;
    Transform heli, rotor, tailRotor;
    Vector3 landingPos, baseCenter, heliVel;
    bool camTaken;
    float fade;                    // 0..1 final fade-to-black
    float creditScroll;
    Color ambient0;

    readonly List<Buildable> baseBuildings = new List<Buildable>();
    Transform bomber, bomb, mushroom;
    Vector3 bomberDir;
    float bomberSpeed, bombVel, detonateAt, flashAmt;
    bool bombDropped, detonated;

    void Awake() { Active = true; }

    void Start()
    {
        Time.timeScale = 1f;
        player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null) player.Disarmed = true; // take all weapons — just run to the chopper
        cam = Camera.main != null ? Camera.main.transform : null;
        ambient0 = RenderSettings.ambientLight;

        baseCenter = ComputeBaseCenter();

        Vector3 fwd = player != null ? Flat(player.transform.forward) : Vector3.forward;
        Vector3 origin = player != null ? player.transform.position : baseCenter;
        landingPos = origin + fwd * 30f;
        landingPos.y = GameBootstrap.Hill(landingPos.x, landingPos.z);

        BuildHeli();
        heli.position = landingPos + Vector3.up * 60f; // starts high, descends

        SpawnFinalCrowd(origin, fwd);
    }

    Vector3 ComputeBaseCenter()
    {
        var all = Buildable.All;
        if (all.Count == 0) return player != null ? player.transform.position : Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var b in all) sum += b.transform.position;
        return sum / all.Count;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

    void SpawnFinalCrowd(Vector3 origin, Vector3 fwd)
    {
        Vector3 mid = origin + fwd * 15f;
        for (int i = 0; i < 28; i++)
        {
            Vector2 r = Random.insideUnitCircle * 16f;
            Vector3 p = mid + new Vector3(r.x, 0f, r.y);
            p.y = GameBootstrap.Hill(p.x, p.z) + 1f;
            Zombie.Create(p, Random.value < 0.3f ? Zombie.Kind.Tank : Zombie.Kind.Normal);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        SpinRotors(dt);

        switch (phase)
        {
            case Phase.Evac: Evac(dt); break;
            case Phase.Liftoff: Liftoff(dt); break;
            case Phase.Nuke: Nuke(dt); break;
            case Phase.Credits: Credits(dt); break;
            case Phase.Done: DonePhase(dt); break;
        }

        // After the nuke nothing should be left alive — sweep any straggler every frame.
        if (detonated) WipeZombies();

        // Sky-darken ramps in during evac and stays dark.
        if (phase == Phase.Evac) darkness = Mathf.Min(1f, darkness + dt / 6f);
        RenderSettings.ambientLight = Color.Lerp(ambient0, new Color(0.05f, 0.05f, 0.08f), darkness);
    }

    // --- Phase 1: descend + run to chopper ---
    void Evac(float dt)
    {
        // Descend to the landing pad (skids ~1.6 above the ground point).
        Vector3 land = landingPos + Vector3.up * 1.6f;
        heli.position = Vector3.MoveTowards(heli.position, land, 12f * dt);

        bool landed = (heli.position - land).sqrMagnitude < 1f;
        bool reached = player != null &&
                       Vector3.Distance(Horizon(player.transform.position), Horizon(heli.position)) < 6f;

        // Board when the player reaches the landed chopper, or after a safety timeout.
        if ((landed && reached) || t > 45f || (player != null && player.IsDead && t > 6f))
        {
            TakeCamera();
            if (player != null) player.enabled = false; // stop control + HUD
            phase = Phase.Liftoff; t = 0f;
        }
    }

    static Vector3 Horizon(Vector3 v) { v.y = 0f; return v; }

    void TakeCamera()
    {
        if (camTaken || cam == null) return;
        cam.SetParent(null, true);
        camTaken = true;
    }

    // --- Phase 2: camera rises high over the base; chopper lifts off ---
    void Liftoff(float dt)
    {
        heli.position += Vector3.up * 6f * dt; // climb

        Vector3 want = baseCenter + new Vector3(0f, 55f, -42f);
        DriveCamera(want, baseCenter, 1.6f * dt);

        if (t > 4f)
        {
            // snapshot the base to level, then send the bomber in
            baseBuildings.Clear();
            baseBuildings.AddRange(Buildable.All);
            BuildBomber();
            phase = Phase.Nuke; t = 0f;
        }
    }

    // --- Phase 3: a Tu-22 flies over and drops a nuke that levels the base ---
    void Nuke(float dt)
    {
        // Camera holds the high overview, then pulls back/up as the mushroom cloud grows.
        float back = detonated ? Mathf.Min(t - detonateAt, 7f) : 0f;
        Vector3 want = baseCenter + new Vector3(Mathf.Sin(t * 0.25f) * 5f, 55f + back * 6f, -42f - back * 8f);
        Vector3 look = baseCenter + Vector3.up * back * 4f;
        DriveCamera(want, look, 1.6f * dt);

        // Fly the bomber across the sky.
        if (bomber != null) bomber.position += bomberDir * bomberSpeed * dt;

        // Release the bomb once the bomber is roughly over the base.
        if (!bombDropped && bomber != null && Vector3.Dot(bomber.position - baseCenter, bomberDir) > -6f)
        {
            bombDropped = true;
            bomb = BuildBomb(bomber.position);
            bombVel = 6f;
        }

        // Bomb falls, accelerating, then detonates on the deck.
        if (bomb != null && !detonated)
        {
            bombVel += 34f * dt;
            bomb.position += Vector3.down * bombVel * dt;
            bomb.Rotate(220f * dt, 0f, 0f, Space.Self);
            if (bomb.position.y <= baseCenter.y + 1.5f) Detonate();
        }

        flashAmt = Mathf.MoveTowards(flashAmt, 0f, dt * 1.1f); // white flash fades

        // Mushroom cloud billows up and out.
        if (mushroom != null)
        {
            mushroom.position += Vector3.up * 7f * dt;
            mushroom.localScale = Vector3.Lerp(mushroom.localScale, Vector3.one * 3.2f, 0.7f * dt);
        }

        if (detonated && t - detonateAt > 8f) { phase = Phase.Credits; t = 0f; } // leave time for the station to crash
        if (t > 16f && !detonated) Detonate(); // safety: never stall the cutscene
    }

    // The flash + fireball + shockwave + mushroom that flattens the base and kills everything.
    void Detonate()
    {
        if (detonated) return;
        detonated = true;
        detonateAt = t;
        flashAmt = 1f;
        if (bomb != null) Destroy(bomb.gameObject);

        // Wipe every zombie FIRST — before any building/orbital work below, so even if
        // something there throws, the nuke still cleared the map. A per-frame sweep in
        // Update() mops up any stragglers after this.
        WipeZombies();

        // expanding fireball
        var fb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(fb.GetComponent<Collider>());
        if (GameBootstrap.World != null) fb.transform.SetParent(GameBootstrap.World);
        fb.transform.position = baseCenter + Vector3.up * 3f;
        fb.transform.localScale = Vector3.one * 5f;
        GameBootstrap.SetColor(fb, new Color(1f, 0.85f, 0.45f));
        fb.AddComponent<NukeFx>();

        Effects.AirBlast(baseCenter + Vector3.up * 1f, 44f); // huge ground shockwave
        BuildMushroom(baseCenter);

        // any orbital station plummets out of the sky a beat after the nuke and explodes
        foreach (var b in baseBuildings)
        {
            var os = b != null ? b.GetComponent<OrbitalStation>() : null;
            if (os != null) os.Crash(1.8f);
        }

        // level the base into rubble
        for (int i = baseBuildings.Count - 1; i >= 0; i--)
            if (baseBuildings[i] != null) Shred(baseBuildings[i], baseCenter, 22f);
    }

    // Kill every zombie currently on the map. Backward index walk: TakeDamage Destroys the
    // object (deferred), so the list isn't mutated mid-loop, but this stays safe regardless.
    static void WipeZombies()
    {
        for (int i = Zombie.All.Count - 1; i >= 0; i--)
            if (Zombie.All[i] != null) Zombie.All[i].TakeDamage(99999f);
    }

    // ---- Tu-22 bomber ----
    void BuildBomber()
    {
        bomber = new GameObject("Tu22").transform;
        if (GameBootstrap.World != null) bomber.SetParent(GameBootstrap.World);
        Color metal = new Color(0.55f, 0.57f, 0.6f);
        Color dark = new Color(0.3f, 0.32f, 0.35f);
        Color glass = new Color(0.25f, 0.35f, 0.45f);

        // long pointed fuselage (capsule laid along +Z = nose direction)
        Prim(bomber, PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), new Vector3(1.3f, 7f, 1.3f), metal, new Vector3(90f, 0f, 0f));
        Prim(bomber, PrimitiveType.Cylinder, new Vector3(0f, 0f, 7.2f), new Vector3(0.55f, 1.4f, 0.55f), metal, new Vector3(90f, 0f, 0f)); // nose
        Prim(bomber, PrimitiveType.Sphere, new Vector3(0f, 0.4f, 5.2f), new Vector3(0.9f, 0.7f, 1.5f), glass);                            // cockpit
        // swept wings
        Prim(bomber, PrimitiveType.Cube, new Vector3(4.8f, -0.1f, -1.5f), new Vector3(9f, 0.25f, 2.6f), metal, new Vector3(0f, 30f, 0f));
        Prim(bomber, PrimitiveType.Cube, new Vector3(-4.8f, -0.1f, -1.5f), new Vector3(9f, 0.25f, 2.6f), metal, new Vector3(0f, -30f, 0f));
        // twin jet engines at the tail root
        Prim(bomber, PrimitiveType.Cylinder, new Vector3(0.95f, 0.3f, -5.6f), new Vector3(0.7f, 1.7f, 0.7f), dark, new Vector3(90f, 0f, 0f));
        Prim(bomber, PrimitiveType.Cylinder, new Vector3(-0.95f, 0.3f, -5.6f), new Vector3(0.7f, 1.7f, 0.7f), dark, new Vector3(90f, 0f, 0f));
        // tall swept tail fin + horizontal stabilisers
        Prim(bomber, PrimitiveType.Cube, new Vector3(0f, 1.8f, -6.4f), new Vector3(0.3f, 3.4f, 2.2f), metal, new Vector3(-28f, 0f, 0f));
        Prim(bomber, PrimitiveType.Cube, new Vector3(2.2f, 0.3f, -6.4f), new Vector3(4f, 0.25f, 1.4f), metal, new Vector3(0f, 24f, 0f));
        Prim(bomber, PrimitiveType.Cube, new Vector3(-2.2f, 0.3f, -6.4f), new Vector3(4f, 0.25f, 1.4f), metal, new Vector3(0f, -24f, 0f));

        bomber.localScale = Vector3.one * 1.7f;
        bomberDir = new Vector3(1f, 0f, 0.18f).normalized;
        bomberSpeed = 65f;
        bomber.position = baseCenter + Vector3.up * 56f - bomberDir * 135f; // fly in from one side, high up
        bomber.rotation = Quaternion.LookRotation(bomberDir);
    }

    Transform BuildBomb(Vector3 at)
    {
        var b = new GameObject("Nuke").transform;
        if (GameBootstrap.World != null) b.SetParent(GameBootstrap.World);
        b.position = at;
        Prim(b, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.6f, 1.0f, 0.6f), new Color(0.2f, 0.22f, 0.24f), new Vector3(90f, 0f, 0f));
        Prim(b, PrimitiveType.Cube, new Vector3(0f, 0f, -0.7f), new Vector3(0.5f, 0.5f, 0.4f), new Color(0.5f, 0.5f, 0.52f), new Vector3(45f, 0f, 0f)); // tail fins
        return b;
    }

    void BuildMushroom(Vector3 at)
    {
        mushroom = new GameObject("Mushroom").transform;
        if (GameBootstrap.World != null) mushroom.SetParent(GameBootstrap.World);
        mushroom.position = at + Vector3.up * 2f;
        Color smoke = new Color(0.26f, 0.23f, 0.21f);
        Color hot = new Color(0.85f, 0.45f, 0.2f);
        Prim(mushroom, PrimitiveType.Cylinder, new Vector3(0f, 6f, 0f), new Vector3(3f, 6f, 3f), smoke);           // stem
        for (int i = 0; i < 7; i++)                                                                                  // cap cluster
        {
            Vector2 r = Random.insideUnitCircle * 4.5f;
            Prim(mushroom, PrimitiveType.Sphere, new Vector3(r.x, 12f + Random.Range(-1f, 1.5f), r.y),
                 Vector3.one * Random.Range(4f, 6.5f), i < 2 ? hot : smoke);
        }
        mushroom.localScale = Vector3.one * 0.6f;
    }

    // Turn a building into a cluster of physics-driven rubble chunks, then remove it.
    // 'speed' is the peak scatter velocity (m/s) — kept low for a weighty, cinematic spread.
    static void Shred(Buildable b, Vector3 blast, float speed)
    {
        Vector3 p = b.transform.position;
        int chunks = Random.Range(5, 9);
        for (int i = 0; i < chunks; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (GameBootstrap.World != null) c.transform.SetParent(GameBootstrap.World);
            c.transform.position = p + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.2f, 2f), Random.Range(-0.6f, 0.6f));
            float s = Random.Range(0.25f, 0.7f);
            c.transform.localScale = new Vector3(s, s * Random.Range(0.5f, 1.3f), s);
            c.transform.rotation = Random.rotationUniform;
            float g = Random.Range(0.4f, 0.6f);
            GameBootstrap.SetColor(c, new Color(g, g, g + 0.03f));
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 1f;
            // Strong, energetic scatter: each chunk gets its OWN launch velocity (independent
            // of distance from the blast), biased outward + upward so they really fly apart
            // and arc down, instead of just dropping where they spawned.
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y);                          // never launch downward
            Vector3 outward = c.transform.position - blast; outward.y = 0f;
            if (outward.sqrMagnitude > 0.01f) dir += outward.normalized; // push away from centre
            rb.linearVelocity = dir.normalized * Random.Range(speed * 0.7f, speed * 1.2f) + Vector3.up * Random.Range(3f, 6f);
            rb.angularVelocity = Random.insideUnitSphere * Random.Range(4f, 9f);
            Object.Destroy(c, 16f);
        }
        Object.Destroy(b.gameObject);
    }

    // --- Phase 4: camera turns to the departing chopper; credits roll ---
    void Credits(float dt)
    {
        // chopper flies away into the distance, gaining height
        if (heliVel == Vector3.zero) heliVel = (Flat(heli.position - baseCenter) * 18f) + Vector3.up * 7f;
        heli.position += heliVel * dt;

        if (cam != null) DriveCamera(cam.position, heli.position, 1.5f * dt); // keep position, swing to face the heli
        creditScroll += dt;
        if (t > 8f) { phase = Phase.Done; t = 0f; }
    }

    // --- Phase 5: fade to black, then return to the main menu ---
    bool finished;
    void DonePhase(float dt)
    {
        fade = Mathf.Min(1f, fade + dt / 2f);
        if (t > 4f && !finished)
        {
            finished = true;
            RenderSettings.ambientLight = ambient0;
            Active = false;
            // Back to the main menu (reliable — Application.Quit doesn't fire in the editor and
            // could leave the player stuck staring at THE END). The cutscene object cleans itself up.
            if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu();
            Destroy(gameObject);
        }
    }

    void DriveCamera(Vector3 wantPos, Vector3 lookAt, float k)
    {
        if (cam == null) return;
        cam.position = Vector3.Lerp(cam.position, wantPos, k);
        Vector3 dir = lookAt - cam.position;
        if (dir.sqrMagnitude > 0.001f)
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(dir), k);
    }

    // ---- helicopter model ----
    void BuildHeli()
    {
        heli = new GameObject("EvacHeli").transform;
        if (GameBootstrap.World != null) heli.SetParent(GameBootstrap.World);
        Color body = new Color(0.18f, 0.22f, 0.18f);
        Color dark = new Color(0.12f, 0.13f, 0.12f);

        Prim(heli, PrimitiveType.Capsule, new Vector3(0f, 0f, 0.2f), new Vector3(1.6f, 1.3f, 1.6f), body, new Vector3(90f, 0f, 0f)); // fuselage
        Prim(heli, PrimitiveType.Sphere, new Vector3(0f, 0.1f, 1.3f), new Vector3(1.3f, 1.1f, 1.2f), new Color(0.3f, 0.5f, 0.6f)); // cockpit glass
        Prim(heli, PrimitiveType.Cube, new Vector3(0f, 0.2f, -2.4f), new Vector3(0.3f, 0.3f, 3f), body);                            // tail boom
        Prim(heli, PrimitiveType.Cube, new Vector3(0f, 0.6f, -3.7f), new Vector3(0.2f, 0.7f, 0.4f), body);                          // tail fin
        Prim(heli, PrimitiveType.Cube, new Vector3(-1.0f, -0.9f, 0.2f), new Vector3(0.12f, 0.12f, 2.4f), dark);                     // left skid
        Prim(heli, PrimitiveType.Cube, new Vector3(1.0f, -0.9f, 0.2f), new Vector3(0.12f, 0.12f, 2.4f), dark);                      // right skid

        // main rotor (spins around Y)
        rotor = new GameObject("Rotor").transform;
        rotor.SetParent(heli, false);
        rotor.localPosition = new Vector3(0f, 1.0f, 0.2f);
        Prim(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.18f, 0.1f, 7f), dark);
        Prim(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(7f, 0.1f, 0.18f), dark);

        // tail rotor (spins around X)
        tailRotor = new GameObject("TailRotor").transform;
        tailRotor.SetParent(heli, false);
        tailRotor.localPosition = new Vector3(0.2f, 0.6f, -3.9f);
        Prim(tailRotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 1.6f, 0.12f), dark);
    }

    void SpinRotors(float dt)
    {
        if (rotor != null) rotor.Rotate(0f, 1600f * dt, 0f, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(1600f * dt, 0f, 0f, Space.Self);
    }

    static void Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
    }

    // ---- on-screen text ----
    void OnGUI()
    {
        UI.Begin();
        float cx = UI.W * 0.5f, cy = UI.H * 0.5f;

        // darkening overlay during nuke/credits/done
        float overlay = 0f;
        if (phase == Phase.Nuke) overlay = 0.2f;
        else if (phase == Phase.Credits) overlay = 0.55f;
        else if (phase == Phase.Done) overlay = Mathf.Lerp(0.55f, 1f, fade);
        if (overlay > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, overlay);
            GUI.DrawTexture(new Rect(0, 0, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // blinding white flash of the detonation
        if (flashAmt > 0.001f)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(flashAmt));
            GUI.DrawTexture(new Rect(0, 0, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        if (phase == Phase.Nuke && !detonated)
        {
            var w = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.3f, 0.2f);
            GUI.Label(new Rect(0, 40, UI.W, 56), "ЯДЕРНЫЙ УДАР", w);
            GUI.color = Color.white;
        }

        if (phase == Phase.Evac)
        {
            var b = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.4f, 0.3f);
            GUI.Label(new Rect(0, 40, UI.W, 50), "ЭВАКУАЦИЯ — добегите до вертолёта!", b);
            GUI.color = Color.white;
        }

        if (phase == Phase.Credits || phase == Phase.Done)
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float y = cy + 120f - creditScroll * 40f;
            GUI.Label(new Rect(0, y, UI.W, 70), "ZOMBIE DEFENSE", title);
            GUI.Label(new Rect(0, y + 75, UI.W, 40), "made by danich", sub);
            GUI.Label(new Rect(0, y + 145, UI.W, 40), "спасибо за игру!", sub);
        }

        if (phase == Phase.Done && fade > 0.8f)
        {
            var end = new GUIStyle(GUI.skin.label) { fontSize = 60, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, cy - 40, UI.W, 80), "THE END", end);
        }
    }
}

/// <summary>The nuke fireball: expands fast, then vanishes.</summary>
public class NukeFx : MonoBehaviour
{
    float t;
    void Update()
    {
        t += Time.deltaTime;
        transform.localScale += Vector3.one * 26f * Time.deltaTime;
        if (t > 1.4f) Destroy(gameObject);
    }
}
