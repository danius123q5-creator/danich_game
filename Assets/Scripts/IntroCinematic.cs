using UnityEngine;

/// <summary>
/// Intro insertion cutscene — plays once at the start of a new game:
///  1. APPROACH — a chopper flies in over the forest carrying the player, descending to a low hover.
///  2. DROP     — the player is set down in a forest clearing; the chopper holds for a beat.
///  3. DEPART   — the chopper climbs and flies off into the distance; the camera watches it leave.
///  4. HANDOVER — the camera snaps back to first person and control is handed to the player.
/// Single-player / host only (LAN joiners spawn straight in, like the evac cutscene).
/// </summary>
public class IntroCinematic : MonoBehaviour
{
    public static bool Active;

    public static void Begin()
    {
        if (Active) return;
        if (LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost) return; // host/SP only
        new GameObject("IntroCinematic").AddComponent<IntroCinematic>();
    }

    enum Phase { Approach, Drop, Depart, Handover }
    Phase phase = Phase.Approach;
    float t;

    PlayerController player;
    CharacterController cc;
    Transform cam;
    Transform heli, rotor, tailRotor;

    Transform playerParent0;       // player's original parent (World), restored on drop
    Vector3 camLocal0;             // camera's original local pose under the player
    Quaternion camRot0;

    Vector3 ground;                // landing point on the terrain
    Vector3 hover;                 // low-hover position above the landing point
    Vector3 departVel;

    void Awake() { Active = true; }

    void Start()
    {
        Time.timeScale = 1f;
        player = Object.FindFirstObjectByType<PlayerController>();
        cam = Camera.main != null ? Camera.main.transform : null;

        // Landing point = wherever the player spawned (already on the terrain surface).
        ground = player != null ? player.transform.position : GameBootstrap.RandomSpawnPoint();
        ground.y = GameBootstrap.Hill(ground.x, ground.z) + 1.1f;
        hover = ground + Vector3.up * 4.5f;

        // Fly in from one side, high up.
        Vector3 fromDir = new Vector3(0.7f, 0f, -0.7f);
        Vector3 approachStart = hover + fromDir * 120f + Vector3.up * 45f;

        BuildHeli();
        heli.position = approachStart;
        heli.rotation = Quaternion.LookRotation(Flat(hover - approachStart));

        // Freeze the player and ride it in slung under the chopper.
        if (player != null)
        {
            player.enabled = false;                 // stop control + HUD for the cutscene
            cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;     // don't let the controller fight the ride
            playerParent0 = player.transform.parent;
            player.transform.SetParent(heli, true);
            player.transform.position = heli.position + Vector3.down * 1.0f; // hangs just under the skids
        }

        // Detach the camera so we can drive it cinematically, and open on the chase shot.
        if (cam != null)
        {
            camLocal0 = cam.localPosition;
            camRot0 = cam.localRotation;
            cam.SetParent(null, true);
            Vector3 want = heli.position + heli.rotation * new Vector3(6f, 4f, -12f);
            cam.position = want;
            cam.rotation = Quaternion.LookRotation((heli.position + heli.forward * 8f) - want);
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

    void Update()
    {
        // Bail out cleanly if the world was torn down mid-cutscene (quit to menu).
        if (heli == null) { Active = false; Destroy(gameObject); return; }

        float dt = Time.deltaTime;
        t += dt;
        SpinRotors(dt);

        switch (phase)
        {
            case Phase.Approach: Approach(dt); break;
            case Phase.Drop: Drop(dt); break;
            case Phase.Depart: Depart(dt); break;
            case Phase.Handover: Handover(); break;
        }
    }

    // --- Phase 1: chopper flies in and descends to the low hover ---
    void Approach(float dt)
    {
        heli.position = Vector3.MoveTowards(heli.position, hover, 28f * dt);
        Vector3 face = Flat(hover - heli.position);
        if (face.sqrMagnitude > 0.001f)
            heli.rotation = Quaternion.Slerp(heli.rotation, Quaternion.LookRotation(face), 2f * dt);

        // Chase cam: behind/above the chopper, looking toward the landing zone.
        Vector3 want = heli.position + heli.rotation * new Vector3(6f, 4f, -12f);
        DriveCamera(want, heli.position + heli.forward * 8f, 3f * dt);

        if ((heli.position - hover).sqrMagnitude < 1.5f || t > 8f) { phase = Phase.Drop; t = 0f; }
    }

    // --- Phase 2: set the player down; the chopper holds for a beat ---
    void Drop(float dt)
    {
        heli.position = Vector3.MoveTowards(heli.position, hover, 6f * dt); // settle into the hover

        // Drop the player onto the ground at the very start of this phase.
        if (player != null && player.transform.parent == heli)
        {
            player.transform.SetParent(playerParent0, true);
            player.transform.position = ground;
        }

        // Swing to a ground-level angle by the player, looking up at the hovering chopper.
        Vector3 want = ground + new Vector3(-4f, 1.8f, -4f);
        DriveCamera(want, hover, 3f * dt);

        if (t > 2.5f) { phase = Phase.Depart; t = 0f; }
    }

    // --- Phase 3: chopper climbs and flies away into the distance ---
    void Depart(float dt)
    {
        if (departVel == Vector3.zero)
            departVel = (Flat(heli.position - ground) * 22f) + Vector3.up * 9f;
        heli.position += departVel * dt;
        Vector3 face = Flat(departVel);
        if (face.sqrMagnitude > 0.001f)
            heli.rotation = Quaternion.Slerp(heli.rotation, Quaternion.LookRotation(face), 2f * dt);

        // Stay by the player and follow the chopper as it shrinks over the trees.
        Vector3 want = ground + new Vector3(-2f, 2.0f, -3f);
        DriveCamera(want, heli.position, 2.5f * dt);

        if (t > 4.5f) phase = Phase.Handover;
    }

    // --- Phase 4: restore first-person control and clean up ---
    void Handover()
    {
        if (player != null)
        {
            if (cc != null) cc.enabled = true;
            player.enabled = true;
        }
        if (cam != null && player != null)
        {
            cam.SetParent(player.transform, false);
            cam.localPosition = camLocal0;
            cam.localRotation = camRot0;
        }
        if (heli != null) Destroy(heli.gameObject);
        Active = false;
        Destroy(gameObject);
    }

    void DriveCamera(Vector3 wantPos, Vector3 lookAt, float k)
    {
        if (cam == null) return;
        cam.position = Vector3.Lerp(cam.position, wantPos, k);
        Vector3 dir = lookAt - cam.position;
        if (dir.sqrMagnitude > 0.001f)
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(dir), k);
    }

    // ---- helicopter model (same build as the evac chopper) ----
    void BuildHeli()
    {
        heli = new GameObject("InsertionHeli").transform;
        if (GameBootstrap.World != null) heli.SetParent(GameBootstrap.World);
        Color body = new Color(0.18f, 0.22f, 0.18f);
        Color dark = new Color(0.12f, 0.13f, 0.12f);

        Prim(heli, PrimitiveType.Capsule, new Vector3(0f, 0f, 0.2f), new Vector3(1.6f, 1.3f, 1.6f), body, new Vector3(90f, 0f, 0f)); // fuselage
        Prim(heli, PrimitiveType.Sphere, new Vector3(0f, 0.1f, 1.3f), new Vector3(1.3f, 1.1f, 1.2f), new Color(0.3f, 0.5f, 0.6f)); // cockpit glass
        Prim(heli, PrimitiveType.Cube, new Vector3(0f, 0.2f, -2.4f), new Vector3(0.3f, 0.3f, 3f), body);                            // tail boom
        Prim(heli, PrimitiveType.Cube, new Vector3(0f, 0.6f, -3.7f), new Vector3(0.2f, 0.7f, 0.4f), body);                          // tail fin
        Prim(heli, PrimitiveType.Cube, new Vector3(-1.0f, -0.9f, 0.2f), new Vector3(0.12f, 0.12f, 2.4f), dark);                     // left skid
        Prim(heli, PrimitiveType.Cube, new Vector3(1.0f, -0.9f, 0.2f), new Vector3(0.12f, 0.12f, 2.4f), dark);                      // right skid

        rotor = new GameObject("Rotor").transform;
        rotor.SetParent(heli, false);
        rotor.localPosition = new Vector3(0f, 1.0f, 0.2f);
        Prim(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.18f, 0.1f, 7f), dark);
        Prim(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(7f, 0.1f, 0.18f), dark);

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

    // ---- on-screen subtitle ----
    void OnGUI()
    {
        if (phase == Phase.Handover) return;
        UI.Begin();
        var style = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.85f, 0.95f, 0.85f);
        GUI.Label(new Rect(0f, UI.H - 92f, UI.W, 40f), Lang.T("Высадка в зону карантина…", "Deploying into the quarantine zone…"), style);
        GUI.color = Color.white;
    }
}
