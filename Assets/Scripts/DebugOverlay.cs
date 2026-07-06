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
        if (Input.GetKeyDown(KeyCode.F10)) LoadBunker();
    }

    static PlayerController P() => Object.FindFirstObjectByType<PlayerController>();
    static void GiveMetal() { var p = P(); if (p != null) p.AddMetal(1000); }
    static void GiveOil()   { var p = P(); if (p != null) p.AddOil(500); }
    static void HealPlayer(){ var p = P(); if (p != null) p.Heal(9999f); }

    // Import a Hammer bunker: drop "bunker.vmf" into the persistent data folder, then press F10.
    static string _bunkerMsg = "";
    static void LoadBunker()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "bunker.vmf");
        if (!System.IO.File.Exists(path)) { _bunkerMsg = "нет файла: " + path; return; }
        try
        {
            var res = VmfImporter.Import(System.IO.File.ReadAllText(path), GameBootstrap.World);
            var p = P();
            if (p != null && res.hasSpawn) p.transform.position = res.spawn;
            _bunkerMsg = $"бункер: {res.brushes} брашей, {res.tris} треуг.";
        }
        catch (System.Exception e) { _bunkerMsg = "ошибка VMF: " + e.Message; }
    }

    void OnGUI()
    {
        if (!Shown || !GameRoot.IsPlaying) return;
        UI.Begin();

        float w = 300f, h = 330f, x = 12f, y = 120f;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 24f), "DEBUG  (F1 закрыть)", title);

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        float by = y + 36f, bh = 26f, bw = w - 20f; float bx = x + 10f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F2] +1000 металл", btn)) GiveMetal(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F3] +500 нефть", btn)) GiveOil(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F4] убить всех зомби", btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugClearWave(); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F5] скип подготовки", btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugSkipPrep(); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F6] +5 волн", btn)) { var gm = GameManager.Instance; if (gm != null) gm.DebugAddWaves(5); } by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), $"[F7] бессмертие: {(PlayerController.GodMode ? "ВКЛ" : "выкл")}", btn)) PlayerController.GodMode = !PlayerController.GodMode; by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F8] полное лечение", btn)) HealPlayer(); by += bh + 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "[F10] загрузить bunker.vmf", btn)) LoadBunker(); by += bh + 4f;
        if (!string.IsNullOrEmpty(_bunkerMsg))
        {
            var info = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            GUI.color = new Color(0.8f, 0.9f, 0.8f);
            GUI.Label(new Rect(bx, by, bw, 40f), _bunkerMsg, info);
            GUI.color = Color.white;
        }
    }
}
