using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Top-level state machine: Main Menu → Playing → Paused. Owns the menu camera,
/// builds/tears down the world, and saves/loads progress (wave/metal/score) via
/// PlayerPrefs. Persists for the whole session (never destroyed).
/// </summary>
public class GameRoot : MonoBehaviour
{
    public static GameRoot Instance;
    public enum GState { Menu, Playing, Paused }
    public GState State { get; private set; } = GState.Menu;
    public static bool IsPlaying => Instance != null && Instance.State == GState.Playing;

    // Selected game mode (set from the Modes screen, read by gameplay systems).
    public enum Mode { Offline, Coop, Pvp }
    public static Mode CurrentMode = Mode.Offline;
    public static bool IsPvp => CurrentMode == Mode.Pvp;
    public static int PvpTeam = 0; // 0 = Team A, 1 = Team B (PvP friendly-fire / colours)
    public static bool Hardcore = false; // die = restart from wave 1, pricier builds, 170 metal cap

    static readonly string[] MapNames = { "Лес", "Холмы", "Арена" };
    static readonly string[] TeamNames = { "Команда A", "Команда Б" };

    Camera menuCam;
    LanManager lan;
    string joinIp = "127.0.0.1";
    bool inModes; // showing the Modes sub-screen instead of the main menu

    bool splashActive = true;
    float splashStart;

    void Awake()
    {
        Instance = this;
        lan = gameObject.AddComponent<LanManager>(); // persists with GameRoot
    }
    void Start() { splashStart = Time.unscaledTime; EnterMenu(); }

    // Autosave on window close (Alt+F4 / editor stop) mid-game — but never in hardcore.
    void OnApplicationQuit() { if (State != GState.Menu && !Hardcore) Save(); }

    void Update()
    {
        if (splashActive)
        {
            float e = Time.unscaledTime - splashStart;
            if (e > 2.5f || (e > 0.4f && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))) splashActive = false;
            return; // hold on the intro until it's dismissed
        }

        if (State == GState.Playing && Input.GetKeyDown(KeyCode.Escape))
        {
            State = GState.Paused;
            Time.timeScale = 0f;
            FreeCursor(true);
        }
        else if (State == GState.Paused && Input.GetKeyDown(KeyCode.Escape))
        {
            Resume();
        }
    }

    // ---- transitions ----
    void EnterMenu()
    {
        State = GState.Menu;
        Time.timeScale = 1f;
        FreeCursor(true);
        if (menuCam == null)
        {
            var go = new GameObject("MenuCamera");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 3f, -8f);
            menuCam = go.AddComponent<Camera>();
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
            go.AddComponent<AudioListener>();
        }
        menuCam.gameObject.SetActive(true);
    }

    void StartGame(bool continueProgress)
    {
        if (menuCam != null) menuCam.gameObject.SetActive(false);
        GameBootstrap.BuildWorld();
        if (continueProgress)
        {
            ApplySave();
            var p = Object.FindFirstObjectByType<PlayerController>();
            if (p != null) LoadBuildings(p);
        }
        State = GState.Playing;
        Time.timeScale = 1f;
        FreeCursor(false);

        // Fresh start: ride in on the insertion chopper. (Continue resumes mid-run, so skip it.)
        if (!continueProgress) IntroCinematic.Begin();
    }

    void Resume()
    {
        State = GState.Playing;
        Time.timeScale = 1f;
        FreeCursor(false);
    }

    void QuitToMenu()
    {
        GameBootstrap.DestroyWorld();
        EnterMenu();
    }

    /// <summary>Public entry for the death screen's "Выйти" button.</summary>
    public void ExitToMenu()
    {
        if (lan != null) lan.Shutdown();
        Time.timeScale = 1f;
        QuitToMenu();
    }

    /// <summary>Hardcore death: throw away the run and start fresh from wave 1.</summary>
    public void RestartRun()
    {
        GameBootstrap.DestroyWorld();
        GameBootstrap.BuildWorld(); // fresh world: wave 0 → wave 1, new player, reset metal
        State = GState.Playing;
        Time.timeScale = 1f;
        FreeCursor(false);
    }

    void FreeCursor(bool free)
    {
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    static void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- save / load (PlayerPrefs) ----
    public static bool HasSave => PlayerPrefs.GetInt("save_exists", 0) == 1;

    public void Save()
    {
        var gm = Object.FindFirstObjectByType<GameManager>();
        var p = Object.FindFirstObjectByType<PlayerController>();
        if (gm == null || p == null) return;
        PlayerPrefs.SetInt("save_exists", 1);
        PlayerPrefs.SetInt("save_wave", gm.WaveNumber);
        PlayerPrefs.SetInt("save_metal", p.Metal);
        PlayerPrefs.SetInt("save_score", p.Score);
        SaveBuildings();
        PlayerPrefs.Save();
    }

    // Serialize every placed building as "type,x,y,z,yaw,level,health,funding" joined by '|'.
    static void SaveBuildings()
    {
        var ci = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        foreach (var b in Object.FindObjectsByType<Buildable>(FindObjectsSortMode.None))
        {
            var pos = b.transform.position;
            float yaw = b.transform.eulerAngles.y;
            parts.Add(string.Join(",", new[]
            {
                b.Type.ToString(ci),
                pos.x.ToString("0.##", ci), pos.y.ToString("0.##", ci), pos.z.ToString("0.##", ci),
                yaw.ToString("0.#", ci),
                b.Level.ToString(ci),
                Mathf.RoundToInt(b.Health).ToString(ci),
                b.FundingPaid.ToString(ci),
            }));
        }
        PlayerPrefs.SetString("save_builds", string.Join("|", parts));
    }

    static void LoadBuildings(PlayerController owner)
    {
        string data = PlayerPrefs.GetString("save_builds", "");
        if (string.IsNullOrEmpty(data)) return;
        var ci = CultureInfo.InvariantCulture;
        foreach (var entry in data.Split('|'))
        {
            if (string.IsNullOrEmpty(entry)) continue;
            var f = entry.Split(',');
            if (f.Length < 8) continue;
            if (!int.TryParse(f[0], NumberStyles.Integer, ci, out int type)) continue;
            float x = float.Parse(f[1], NumberStyles.Float, ci);
            float y = float.Parse(f[2], NumberStyles.Float, ci);
            float z = float.Parse(f[3], NumberStyles.Float, ci);
            float yaw = float.Parse(f[4], NumberStyles.Float, ci);
            int level = int.Parse(f[5], NumberStyles.Integer, ci);
            float health = float.Parse(f[6], NumberStyles.Float, ci);
            int funding = int.Parse(f[7], NumberStyles.Integer, ci);

            // Create adds a small +0.02 ground offset; subtract it so position is stable across reloads.
            var go = Buildable.Create(type, new Vector3(x, y - 0.02f, z), Quaternion.Euler(0f, yaw, 0f), owner);
            var b = go != null ? go.GetComponent<Buildable>() : null;
            if (b != null) b.LoadState(level, health, funding);
        }
    }

    void ApplySave()
    {
        var gm = Object.FindFirstObjectByType<GameManager>();
        var p = Object.FindFirstObjectByType<PlayerController>();
        if (gm != null) gm.SetWave(PlayerPrefs.GetInt("save_wave", 0));
        if (p != null)
        {
            p.Metal = PlayerPrefs.GetInt("save_metal", 250);
            p.Score = PlayerPrefs.GetInt("save_score", 0);
        }
    }

    // ---- UI ----
    void OnGUI()
    {
        if (splashActive) { DrawSplash(); return; }
        if (State == GState.Menu) { if (inModes) DrawModesMenu(); else DrawMainMenu(); }
        else if (State == GState.Paused) DrawPauseMenu();
    }

    void DrawSplash()
    {
        GUI.color = new Color(0.08f, 0.10f, 0.14f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        float cy = Screen.height * 0.5f;
        var big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, cy - 60f, Screen.width, 120f), "made by danich", big);
    }

    void DrawMainMenu()
    {
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.6f, 0.9f, 0.5f);
        GUI.Label(new Rect(cx - 320f, cy - 190f, 640f, 70f), "ОБОРОНА ОТ ЗОМБИ", title);
        GUI.color = Color.white;

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        float bw = 280f, bh = 54f, x = cx - bw * 0.5f;

        if (HasSave)
        {
            int w = PlayerPrefs.GetInt("save_wave", 0) + 1;
            if (GUI.Button(new Rect(x, cy - 80f, bw, bh), $"Продолжить  (волна {w})", btn)) { CurrentMode = Mode.Offline; Hardcore = false; StartGame(true); }
        }
        if (GUI.Button(new Rect(x, cy - 16f, bw, bh), "Новая игра", btn)) { CurrentMode = Mode.Offline; Hardcore = false; PlayerPrefs.DeleteKey("save_exists"); PlayerPrefs.DeleteKey("save_builds"); StartGame(false); }
        if (GUI.Button(new Rect(x, cy + 48f, bw, bh), "Режимы", btn)) inModes = true;
        if (GUI.Button(new Rect(x, cy + 112f, bw, bh), "Выход", btn)) QuitApp();

        var credit = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.7f, 0.8f, 0.7f);
        GUI.Label(new Rect(0f, Screen.height - 28f, Screen.width, 22f), "made by danich", credit);
        GUI.color = Color.white;
    }

    // ---- Modes screen: pick offline / online co-op / PvP, choose a map, host or join ----
    void DrawModesMenu()
    {
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.6f, 0.9f, 0.5f);
        GUI.Label(new Rect(cx - 320f, cy - 250f, 640f, 64f), "РЕЖИМЫ", title);
        GUI.color = Color.white;

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
        var small = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        var lab = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        var fld = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        float bw = 320f, x = cx - bw * 0.5f;

        // ---- Map selector (host's choice; joiners auto-adopt the host's map) ----
        GUI.Label(new Rect(x, cy - 202f, bw, 22f), "Карта (хост)", lab);
        float mw = bw / 3f;
        for (int i = 0; i < MapNames.Length; i++)
        {
            bool sel = GameBootstrap.MapVariant == i;
            GUI.color = sel ? new Color(0.55f, 0.85f, 0.5f) : Color.white;
            if (GUI.Button(new Rect(x + i * mw, cy - 178f, mw - 4f, 38f), MapNames[i], small)) GameBootstrap.MapVariant = i;
        }
        GUI.color = Color.white;

        // ---- Team selector (PvP friendly-fire / colours) ----
        GUI.Label(new Rect(x, cy - 130f, bw, 22f), "Команда (PvP)", lab);
        float tw = bw / 2f;
        for (int i = 0; i < TeamNames.Length; i++)
        {
            GUI.color = PvpTeam == i ? new Color(0.5f, 0.7f, 1f) : Color.white;
            if (GUI.Button(new Rect(x + i * tw, cy - 106f, tw - 4f, 38f), TeamNames[i], small)) PvpTeam = i;
        }
        GUI.color = Color.white;

        // ---- 1) Offline: normal + hardcore (single-player) ----
        float hw = bw / 2f;
        if (GUI.Button(new Rect(x, cy - 58f, hw - 4f, 46f), "Оффлайн", btn))
        {
            CurrentMode = Mode.Offline; Hardcore = false; StartGame(false);
        }
        GUI.backgroundColor = new Color(0.85f, 0.4f, 0.3f);
        if (GUI.Button(new Rect(x + hw, cy - 58f, hw - 4f, 46f), "ХАРДКОР", btn))
        {
            CurrentMode = Mode.Offline; Hardcore = true; StartGame(false);
        }
        GUI.backgroundColor = Color.white;

        // ---- 2) Online co-op — host or join over LAN ----
        if (GUI.Button(new Rect(x, cy - 6f, bw, 42f), "Кооп — Хост (LAN)", small))
        {
            CurrentMode = Mode.Coop; Hardcore = false; lan.StartHost(); StartGame(false);
        }

        // ---- 3) PvP (players vs players) — host or join ----
        if (GUI.Button(new Rect(x, cy + 42f, bw, 42f), "PvP — Хост (LAN)", small))
        {
            CurrentMode = Mode.Pvp; Hardcore = false; lan.StartHost(); StartGame(false);
        }

        // Shared join row (IP) — joins whatever the host is running.
        joinIp = GUI.TextField(new Rect(x, cy + 90f, bw - 150f, 42f), joinIp, fld);
        if (GUI.Button(new Rect(x + bw - 144f, cy + 90f, 70f, 42f), "Кооп", small))
        {
            CurrentMode = Mode.Coop; Hardcore = false; if (lan.StartClient(joinIp)) StartGame(false);
        }
        if (GUI.Button(new Rect(x + bw - 70f, cy + 90f, 70f, 42f), "PvP", small))
        {
            CurrentMode = Mode.Pvp; Hardcore = false; if (lan.StartClient(joinIp)) StartGame(false);
        }

        var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUI.color = new Color(0.8f, 0.85f, 0.9f);
        GUI.Label(new Rect(cx - 320f, cy + 138f, 640f, 86f),
            $"LAN: хост сообщает свой IP, остальные вбивают его и жмут Join (UDP порт {LanManager.Port}). " +
            "Кооп: общие зомби, волны и постройки. PvP: стреляй по чужой команде (по своим урона нет). " +
            "Карту диктует хост — присоединившиеся подхватывают её автоматически.", hint);
        GUI.color = Color.white;

        if (GUI.Button(new Rect(x, cy + 230f, bw, 42f), "Назад", small)) inModes = false;
    }

    void DrawPauseMenu()
    {
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(cx - 200f, cy - 170f, 400f, 60f), "ПАУЗА", title);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        float bw = 280f, bh = 54f, x = cx - bw * 0.5f;
        if (GUI.Button(new Rect(x, cy - 70f, bw, bh), "Продолжить", btn)) Resume();
        if (GUI.Button(new Rect(x, cy - 6f, bw, bh), "В главное меню", btn)) ExitToMenu();
        if (GUI.Button(new Rect(x, cy + 58f, bw, bh), "Выйти из игры", btn)) QuitApp();
    }
}
