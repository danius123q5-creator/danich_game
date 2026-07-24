using UnityEngine;

/// <summary>3.7: an in-game MODEL VIEWER launched from the main menu. Every buildable is assembled
/// from primitives by Models.BuildVisual(type, level); this puts one on a turntable, orbits the menu
/// camera around it, and lets you cycle through every build type and level to inspect the geometry
/// without starting a match. Fully self-contained — reuses the menu camera and restores it on exit.</summary>
public class ModelViewer : MonoBehaviour
{
    public const int MaxType = 46;   // build types 0..46 all have a Models.BuildVisual (keep in sync)

    Camera cam;
    int type, level = 1;
    GameObject model;
    Transform stage;
    Light key;
    float yaw;
    Vector3 center = Vector3.up;
    float dist = 8f;

    // saved menu-camera state, restored when the viewer closes
    CameraClearFlags savedFlags; Color savedBg; Vector3 savedPos; Quaternion savedRot;

    public void Init(Camera menuCam)
    {
        cam = menuCam;
        if (cam != null)
        {
            savedFlags = cam.clearFlags; savedBg = cam.backgroundColor;
            savedPos = cam.transform.position; savedRot = cam.transform.rotation;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.16f, 0.20f); // neutral studio backdrop
        }

        // a soft key light so the primitive shapes read with depth
        var lgo = new GameObject("ViewerLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.rotation = Quaternion.Euler(38f, 40f, 0f);
        key = lgo.AddComponent<Light>();
        key.type = LightType.Directional; key.intensity = 1.2f; key.color = new Color(1f, 0.96f, 0.9f);

        // a round turntable pedestal
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.localScale = new Vector3(6f, 0.1f, 6f);
        disc.transform.position = new Vector3(0f, -0.05f, 0f);
        GameBootstrap.SetColor(disc, new Color(0.24f, 0.26f, 0.30f));
        stage = new GameObject("ViewerStage").transform;
        stage.SetParent(transform, false);

        Rebuild();
    }

    void Rebuild()
    {
        if (model != null) Destroy(model);
        type = Mathf.Clamp(type, 0, MaxType);
        model = Models.BuildVisual(type, level);
        model.transform.SetParent(stage, false);
        model.transform.localPosition = Vector3.zero;

        // frame the camera to the model's actual bounds (some are 20 m towers, some tiny mines)
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            center = b.center;
            dist = Mathf.Max(3.5f, b.size.magnitude * 0.95f);
        }
        else { center = Vector3.up; dist = 8f; }
    }

    public string ModelName => model != null ? model.name.Replace("Model", "") : "?";

    void Update()
    {
        yaw += 22f * Time.unscaledDeltaTime; // slow auto-spin (orbit the camera, model stays put)
        if (Input.GetKey(KeyCode.LeftArrow)) yaw -= 90f * Time.unscaledDeltaTime;  // manual drag with arrows
        if (Input.GetKey(KeyCode.RightArrow)) yaw += 90f * Time.unscaledDeltaTime;
        if (cam == null) return;
        Quaternion rot = Quaternion.Euler(16f, yaw, 0f);
        Vector3 pos = center + rot * new Vector3(0f, dist * 0.28f, -dist);
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(center - pos);
    }

    /// <summary>Draws the viewer overlay. Returns true when the player asked to go BACK to the menu.</summary>
    public bool DrawGUI()
    {
        float cx = UI.W * 0.5f;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.6f, 0.9f, 0.5f);
        GUI.Label(new Rect(cx - 320f, 26f, 640f, 44f), Lang.T("ПРОСМОТР МОДЕЛЕЙ", "MODEL VIEWER"), title);
        GUI.color = Color.white;

        var name = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(cx - 320f, 74f, 640f, 34f), $"[{type}]  {ModelName}   ·   {Lang.T("уровень", "level")} {level}", name);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        var small = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };

        // ── type row: ◀  ТИП  ▶ ──
        float rowY = UI.H - 150f;
        GUI.Label(new Rect(cx - 260f, rowY - 26f, 520f, 22f),
            Lang.T("Тип постройки", "Build type"), new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        if (GUI.Button(new Rect(cx - 260f, rowY, 90f, 46f), "◀", btn)) { type = (type + MaxType) % (MaxType + 1); level = 1; Rebuild(); }
        if (GUI.Button(new Rect(cx + 170f, rowY, 90f, 46f), "▶", btn)) { type = (type + 1) % (MaxType + 1); level = 1; Rebuild(); }

        // ── level row: ◀  УР.  ▶ ──
        float ly = UI.H - 90f;
        if (GUI.Button(new Rect(cx - 160f, ly, 70f, 40f), "◀ ур.", small)) { level = Mathf.Max(1, level - 1); Rebuild(); }
        if (GUI.Button(new Rect(cx + 90f, ly, 70f, 40f), "ур. ▶", small)) { level = Mathf.Min(3, level + 1); Rebuild(); }

        // ── BACK ──
        bool back = GUI.Button(new Rect(cx - 70f, UI.H - 44f, 140f, 38f), Lang.T("Назад", "Back"), small);
        if (Input.GetKeyDown(KeyCode.Escape)) back = true;

        var hint = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.8f, 0.85f, 0.9f);
        GUI.Label(new Rect(cx - 320f, UI.H - 176f, 640f, 20f),
            Lang.T("◀▶ листать типы · крутить ←→ стрелками · Esc — назад", "◀▶ cycle types · rotate with ←→ · Esc to go back"), hint);
        GUI.color = Color.white;
        return back;
    }

    public void Cleanup()
    {
        if (cam != null)
        {
            cam.clearFlags = savedFlags; cam.backgroundColor = savedBg;
            cam.transform.position = savedPos; cam.transform.rotation = savedRot;
        }
        Destroy(gameObject);
    }
}
