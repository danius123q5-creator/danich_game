using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// End-game evacuation cutscene (wave 60):
///  1. EVAC   — sky darkens, siren, a chopper descends; the player runs to it through a final crowd.
///  2. LIFTOFF— on boarding the camera detaches from the player and rises high over the base.
///  3. BOMBARD— from up high, rockets rain and shred the whole base into PHYSICAL debris (rigidbodies).
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

    enum Phase { Evac, Liftoff, Bombard, Credits, Done }
    Phase phase = Phase.Evac;
    float t;                       // seconds in the current phase
    float darkness;               // 0..1 sky-darken amount

    Transform cam;
    PlayerController player;
    Transform heli, rotor, tailRotor;
    Vector3 landingPos, baseCenter, heliVel;
    bool camTaken, boarded;
    float fade;                    // 0..1 final fade-to-black
    float creditScroll;
    Color ambient0;

    readonly List<Buildable> baseBuildings = new List<Buildable>();
    float rocketTimer;
    bool finalBlast;

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
        var all = Object.FindObjectsByType<Buildable>(FindObjectsSortMode.None);
        if (all.Length == 0) return player != null ? player.transform.position : Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var b in all) sum += b.transform.position;
        return sum / all.Length;
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
            case Phase.Bombard: Bombard(dt); break;
            case Phase.Credits: Credits(dt); break;
            case Phase.Done: DonePhase(dt); break;
        }

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
        bool reached = player != null && (Flat(player.transform.position - heli.position).sqrMagnitude >= 0f) &&
                       Vector3.Distance(Horizon(player.transform.position), Horizon(heli.position)) < 6f;

        // Board when the player reaches the landed chopper, or after a safety timeout.
        if ((landed && reached) || t > 45f || (player != null && player.IsDead && t > 6f))
        {
            boarded = true;
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
            // snapshot the base to shred, then start the bombardment
            baseBuildings.Clear();
            baseBuildings.AddRange(Object.FindObjectsByType<Buildable>(FindObjectsSortMode.None));
            phase = Phase.Bombard; t = 0f; rocketTimer = 0f; finalBlast = false;
        }
    }

    // --- Phase 3: rockets rain, base shreds into physical debris ---
    void Bombard(float dt)
    {
        // hold the high overview, slight drift for life
        Vector3 want = baseCenter + new Vector3(Mathf.Sin(t * 0.4f) * 6f, 55f, -42f);
        DriveCamera(want, baseCenter, 2f * dt);

        rocketTimer -= dt;
        if (t < 4f && rocketTimer <= 0f)
        {
            rocketTimer = 0.22f;
            // pick an impact point on a remaining building (or random base spot)
            Vector3 hit = baseCenter + new Vector3(Random.Range(-18f, 18f), 0f, Random.Range(-18f, 18f));
            var b = NextStanding();
            if (b != null) hit = b.transform.position;
            FireRocket(hit, 5f, 8f);
        }

        if (!finalBlast && t >= 4f)
        {
            finalBlast = true;
            // one giant particle blast (fireball + shockwave + smoke plume) that levels the base
            Effects.AirBlast(baseCenter + Vector3.up * 1f, 16f);
            for (int i = baseBuildings.Count - 1; i >= 0; i--)
                if (baseBuildings[i] != null) Shred(baseBuildings[i], baseCenter, 8f);
            foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
                z.TakeDamage(99999f);
        }

        if (t > 5.5f) { phase = Phase.Credits; t = 0f; }
    }

    Buildable NextStanding()
    {
        for (int i = 0; i < baseBuildings.Count; i++)
            if (baseBuildings[i] != null) return baseBuildings[i];
        return null;
    }

    void FireRocket(Vector3 impact, float force, float radius)
    {
        Vector3 sky = impact + new Vector3(Random.Range(-6f, 6f), 45f, Random.Range(-14f, -6f));
        Effects.Tracer(sky, impact);            // incoming streak
        Effects.Explosion(impact + Vector3.up * 0.4f);
        float rSq = radius * radius;
        for (int i = baseBuildings.Count - 1; i >= 0; i--)
        {
            var b = baseBuildings[i];
            if (b == null) continue;
            if ((b.transform.position - impact).sqrMagnitude <= rSq) Shred(b, impact, force);
        }
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
            // VelocityChange => the value is the velocity added (mass-independent), so it's
            // easy to keep slow. Gentle outward + slight upward toss, lazy tumble.
            rb.AddExplosionForce(speed, blast, 18f, 1.5f, ForceMode.VelocityChange);
            rb.angularVelocity = Random.insideUnitSphere * Random.Range(1.5f, 3.5f);
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

        DriveCamera(cam.position, heli.position, 1.5f * dt); // keep position, swing to face the heli
        creditScroll += dt;
        if (t > 8f) { phase = Phase.Done; t = 0f; }
    }

    // --- Phase 5: fade to black, then quit the game ---
    void DonePhase(float dt)
    {
        fade = Mathf.Min(1f, fade + dt / 2f);
        if (t > 4f)
        {
            RenderSettings.ambientLight = ambient0;
            Active = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;

        // darkening overlay during bombard/credits/done
        float overlay = 0f;
        if (phase == Phase.Bombard) overlay = 0.25f;
        else if (phase == Phase.Credits) overlay = 0.55f;
        else if (phase == Phase.Done) overlay = Mathf.Lerp(0.55f, 1f, fade);
        if (overlay > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, overlay);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        if (phase == Phase.Evac)
        {
            var b = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.4f, 0.3f);
            GUI.Label(new Rect(0, 40, Screen.width, 50), "ЭВАКУАЦИЯ — добегите до вертолёта!", b);
            GUI.color = Color.white;
        }

        if (phase == Phase.Credits || phase == Phase.Done)
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float y = cy + 120f - creditScroll * 40f;
            GUI.Label(new Rect(0, y, Screen.width, 70), "ZOMBIE DEFENSE", title);
            GUI.Label(new Rect(0, y + 75, Screen.width, 40), "made by danich", sub);
            GUI.Label(new Rect(0, y + 145, Screen.width, 40), "спасибо за игру!", sub);
        }

        if (phase == Phase.Done && fade > 0.8f)
        {
            var end = new GUIStyle(GUI.skin.label) { fontSize = 60, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, cy - 40, Screen.width, 80), "THE END", end);
        }
    }
}
