using UnityEngine;

/// <summary>Developer debug menu. Press F1 in-game to toggle a cheat panel: give resources, wipe
/// the wave, skip prep, jump waves, god-mode, full heal and force day/night. Buttons double as
/// hotkeys (F2..F9). Purely a dev/testing aid — off by default and reset each game.</summary>
public class DebugOverlay : MonoBehaviour
{
    public static bool Shown;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) Shown = !Shown;
        if (!Shown || !GameRoot.IsPlaying) return;

        if (Input.GetKeyDown(KeyCode.F2)) GiveMetal();
        if (Input.GetKeyDown(KeyCode.F3)) GiveOil();
        if (Input.GetKeyDown(KeyCode.F4)) { var gm = GameManager.Instance; if (gm != null) gm.DebugClearWave(); }
        if (Input.GetKeyDown(KeyCode.F5)) { var gm = GameManager.Instance; if (gm != null) gm.DebugSkipPrep(); }
        if (Input.GetKeyDown(KeyCode.F6)) { var gm = GameManager.Instance; if (gm != null) gm.DebugAddWaves(5); }
        if (Input.GetKeyDown(KeyCode.F7)) PlayerController.GodMode = !PlayerController.GodMode;
        if (Input.GetKeyDown(KeyCode.F8)) HealPlayer();
        if (Input.GetKeyDown(KeyCode.F9)) SimEscape.Begin(); // ARG: jump straight to the playable epilogue (DEV TEST — strip before release)
        if (Input.GetKeyDown(KeyCode.F10)) LoadBunker();
    }

    static PlayerController P() => Object.FindFirstObjectByType<PlayerController>();
    static void GiveMetal() { var p = P(); if (p != null) p.AddMetal(1000); }
    static void GiveOil()   { var p = P(); if (p != null) p.AddOil(500); }
    static void HealPlayer(){ var p = P(); if (p != null) p.Heal(9999f); }

    // Import a Hammer/Source map: drop ANY .vmf into a "maps" folder next to the game (or the
    // persistent data folder), then press F10. Loads the first .vmf found, clears the previous
    // import, and teleports to its info_player_start. 2026-07-12.
    static string _bunkerMsg = "";
    static GameObject _vmfRoot;
    static void LoadBunker()
    {
        string path = FindVmf();
        if (path == null)
        {
            _bunkerMsg = Lang.T("не найден .vmf — положи карту в: ", "no .vmf found — put a map in: ")
                       + System.IO.Path.Combine(GameFolder(), "maps");
            return;
        }
        try
        {
            if (_vmfRoot != null) Object.Destroy(_vmfRoot);           // clear the previous import
            _vmfRoot = new GameObject("VmfImport");
            if (GameBootstrap.World != null) _vmfRoot.transform.SetParent(GameBootstrap.World, false);

            var res = VmfImporter.Import(System.IO.File.ReadAllText(path), _vmfRoot.transform);
            var p = P();
            if (p != null && res.hasSpawn) p.transform.position = res.spawn;

            // НЕБО: Source-карты не несут неба (sky-браши пропущены), а эпилог мог обнулить
            // скайбокс → был чёрный экран. Гарантируем процедурное небо + камеру на скайбокс,
            // чтобы над импортированной картой всегда было небо, а не чернота. 2026-07-13.
            if (RenderSettings.skybox == null)
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh != null) RenderSettings.skybox = new Material(sh);
                RenderSettings.fog = false;
                UnityEngine.DynamicGI.UpdateEnvironment();
            }
            if (Camera.main != null && Camera.main.clearFlags == CameraClearFlags.SolidColor)
                Camera.main.clearFlags = CameraClearFlags.Skybox;

            // Поднимаем энтити-рантайм: двери/кнопки/триггеры/счётчики карты оживают.
            VmfRuntime.Ensure(_vmfRoot.transform);
            string nm = System.IO.Path.GetFileName(path);
            _bunkerMsg = Lang.T(nm + ": " + res.brushes + " брашей, " + res.entities + " энтити, " + res.tris + " треуг.",
                                nm + ": " + res.brushes + " brushes, " + res.entities + " entities, " + res.tris + " tris");
        }
        catch (System.Exception e) { _bunkerMsg = Lang.T("ошибка VMF: ", "VMF error: ") + e.Message; }
    }

    // The game's own folder (where the .exe lives) — accessible, unlike persistentDataPath.
    static string GameFolder()
    {
        try { return System.IO.Path.GetDirectoryName(Application.dataPath); } catch { return Application.persistentDataPath; }
    }

    // Find a .vmf to import. Priority: <game>\maps\*.vmf, <game>\*.vmf, persistentDataPath (bunker.vmf
    // then any *.vmf). Returns null if none. Lets the user just drop a Source map next to the game.
    static string FindVmf()
    {
        // maps folder / game folder / persistentData win FIRST — so YOUR dropped map loads, not the
        // old bunker.vmf. (That legacy file was hijacking F10, showing a little cube.) 2026-07-13.
        string[] dirs = { System.IO.Path.Combine(GameFolder(), "maps"), GameFolder(), Application.persistentDataPath };
        foreach (string d in dirs)
        {
            try
            {
                if (!System.IO.Directory.Exists(d)) continue;
                var files = System.IO.Directory.GetFiles(d, "*.vmf");
                if (files.Length > 0) return files[0];
            }
            catch { }
        }
        // legacy bunker.vmf only as a LAST resort
        string legacy = System.IO.Path.Combine(Application.persistentDataPath, "bunker.vmf");
        if (System.IO.File.Exists(legacy)) return legacy;
        return null;
    }

    void OnGUI()
    {
        if (!Shown || !GameRoot.IsPlaying) return;
        UI.Begin();

        float w = 300f, h = 362f, x = 12f, y = 120f;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 24f), Lang.T("DEBUG  (F1 закрыть)", "DEBUG  (F1 close)"), title);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        float by = y + 36f, bh = 26f, bw = w - 20f; float bx = x + 10f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F2] +1000 металл", "[F2] +1000 metal"), btn)) GiveMetal(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F3] +500 нефть", "[F3] +500 oil"), btn)) GiveOil(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F4] убить всех зомби", "[F4] kill all zombies"), btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugClearWave(); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F5] скип подготовки", "[F5] skip prep"), btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugSkipPrep(); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F6] +5 волн", "[F6] +5 waves"), btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugAddWaves(5); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), $"[F7] {Lang.T("бессмертие", "godmode")}: {(PlayerController.GodMode ? Lang.T("ВКЛ", "ON") : Lang.T("выкл", "off"))}", btn)) PlayerController.GodMode = !PlayerController.GodMode; by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F8] полное лечение", "[F8] full heal"), btn)) HealPlayer(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F9] эпилог (АРГ-тест)", "[F9] epilogue (ARG test)"), btn)) SimEscape.Begin(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), Lang.T("[F10] импорт .vmf карты", "[F10] import .vmf map"), btn)) LoadBunker(); by += bh + 4f;
        if (!string.IsNullOrEmpty(_bunkerMsg))
        {
            var info = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            GUI.color = new Color(0.8f, 0.9f, 0.8f);
            GUI.Label(new Rect(bx, by, bw, 40f), _bunkerMsg, info);
            GUI.color = Color.white;
        }
    }
}
