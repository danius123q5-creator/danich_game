using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// .gdf ("Game Danich Format") save files — one text file per slot under persistentDataPath/saves,
/// each with a companion .png thumbnail (grabbed when the player presses ESC). The format is plain,
/// human-readable key/value lines plus the machine data needed to fully rebuild the game. Loading a
/// slot copies its values into the same PlayerPrefs keys the existing Continue restore already reads,
/// so all the building/refinery/mine reconstruction is reused as-is.
/// </summary>
public static class SaveSystem
{
    public const int MaxSlots = 6;
    const string Magic = "GDF";

    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static int CurrentSlot = 0; // slot the running game autosaves into

    /// <summary>The base data folder for the game — beside the LAUNCHER when it dropped a savepath.txt
    /// pointer, else the game's own install dir (fallback: persistentDataPath). Both the "saves" and
    /// "mods" folders live under here. Public so ModRuntime can find the mods folder too.</summary>
    public static string BaseDir
    {
        get
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return Application.persistentDataPath; // browser: no real filesystem

            string gameDir = null;
            try { gameDir = Directory.GetParent(Application.dataPath)?.FullName; } catch { }
            string baseDir = gameDir;
            try
            {
                if (!string.IsNullOrEmpty(gameDir))
                {
                    string ptr = Path.Combine(gameDir, "savepath.txt"); // launcher points saves beside itself
                    if (File.Exists(ptr))
                    {
                        string lp = File.ReadAllText(ptr).Trim();
                        if (!string.IsNullOrEmpty(lp) && Directory.Exists(lp)) baseDir = lp;
                    }
                }
            }
            catch { }
            return string.IsNullOrEmpty(baseDir) ? Application.persistentDataPath : baseDir;
        }
    }

    static string Dir
    {
        get
        {
            string d = Path.Combine(BaseDir, "saves");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            MigrateOldSaves(d);
            return d;
        }
    }

    // Copy any pre-3.1.1 saves from the old AppData\LocalLow location into the new game-dir folder,
    // once, so players keep their runs after the move. Never overwrites a save already in the new dir.
    static bool migrated;
    static void MigrateOldSaves(string newDir)
    {
        if (migrated) return;
        migrated = true;
        try
        {
            string old = Path.Combine(Application.persistentDataPath, "saves");
            if (!Directory.Exists(old)) return;
            if (string.Equals(Path.GetFullPath(old).TrimEnd('\\', '/'),
                              Path.GetFullPath(newDir).TrimEnd('\\', '/'),
                              StringComparison.OrdinalIgnoreCase)) return; // same folder — nothing to do
            foreach (var f in Directory.GetFiles(old))
            {
                string dst = Path.Combine(newDir, Path.GetFileName(f));
                if (!File.Exists(dst)) File.Copy(f, dst);
            }
        }
        catch (Exception e) { Debug.LogWarning($"[gdf] migrate old saves: {e.Message}"); }
    }

    public static string GdfPath(int slot) => Path.Combine(Dir, $"slot{slot}.gdf");
    public static string PngPath(int slot) => Path.Combine(Dir, $"slot{slot}.png");

    /// <summary>Metadata read from a slot's header, for the load menu.</summary>
    public class SlotInfo
    {
        public int slot;
        public int wave;
        public bool infinite, night;
        public string time = "";
        public bool exists;
    }

    /// <summary>Pick the slot a fresh game should use: the first empty one, else slot 0 (overwrite).</summary>
    public static int NextFreeSlot()
    {
        for (int i = 0; i < MaxSlots; i++)
            if (!File.Exists(GdfPath(i))) return i;
        return 0;
    }

    public static List<SlotInfo> ListSlots()
    {
        var list = new List<SlotInfo>();
        for (int i = 0; i < MaxSlots; i++) list.Add(ReadHeader(i));
        return list;
    }

    public static SlotInfo ReadHeader(int slot)
    {
        var info = new SlotInfo { slot = slot };
        try
        {
            string path = GdfPath(slot);
            if (!File.Exists(path)) return info;
            foreach (var line in File.ReadAllLines(path))
            {
                var kv = Split(line);
                switch (kv.Item1)
                {
                    case "wave": int.TryParse(kv.Item2, NumberStyles.Integer, CI, out info.wave); break;
                    case "infinite": info.infinite = kv.Item2 == "1"; break;
                    case "night": info.night = kv.Item2 == "1"; break;
                    case "time": info.time = kv.Item2; break;
                }
            }
            info.exists = true;
        }
        catch (Exception e) { Debug.LogWarning($"[gdf] read header {slot}: {e.Message}"); }
        return info;
    }

    /// <summary>Write the current game to a slot. PlayerPrefs must already hold the save_* values
    /// (call GameRoot.Save() first). Live-only extras (hp/deaths/kills/pos/night/landscape) are read
    /// straight from the scene. Optionally writes a PNG thumbnail (raw encoded bytes).</summary>
    public static void WriteSlot(int slot, byte[] pngBytes)
    {
        try
        {
            var p = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();

            int wave = PlayerPrefs.GetInt("save_wave", 0);
            int metal = PlayerPrefs.GetInt("save_metal", 0);
            int oil = PlayerPrefs.GetInt("save_oil", 0);
            bool infinite = PlayerPrefs.GetInt("save_infinite", 0) == 1;
            string builds = PlayerPrefs.GetString("save_builds", "");
            string refs = PlayerPrefs.GetString("save_refineries", "");
            string mines = PlayerPrefs.GetString("save_mines", "");

            int hp = p != null ? Mathf.RoundToInt(p.Health) : 0;
            int deaths = p != null ? p.Deaths : 0;
            int kills = p != null ? p.Score : 0;
            Vector3 pos = p != null ? p.transform.position : Vector3.zero;
            int zpw = gm != null ? gm.ZombiesLeft : 0;

            // Critical base dispenser state (its HP/level + where it stands, so continue can re-mark it).
            Dispenser disp = null;
            foreach (var d in Dispenser.All) if (d != null && d.Critical) { disp = d; break; }
            if (disp == null) foreach (var d in Dispenser.All) if (d != null) { disp = d; break; }
            int dispHp = disp != null ? Mathf.RoundToInt(disp.Health) : -1;
            int dispLvl = disp != null ? disp.Level : 0;
            Vector3 dispPos = disp != null ? disp.transform.position : Vector3.zero;
            PlayerPrefs.SetString("save_disp_pos", disp != null ? $"{dispPos.x.ToString("0.##", CI)},{dispPos.z.ToString("0.##", CI)}" : "");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Magic} {GameVersion.Current}");
            sb.AppendLine($"slot {slot}");
            sb.AppendLine($"time {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"wave {wave}");
            sb.AppendLine($"zpw {zpw}");
            sb.AppendLine($"landscape {(GameManager.LandscapeChanged ? 1 : 0)}");
            sb.AppendLine($"night {(GameBootstrap.Night ? 1 : 0)}");
            sb.AppendLine($"infinite {(infinite ? 1 : 0)}");
            sb.AppendLine($"p1_hp {hp}");
            sb.AppendLine($"p1_metal {metal}");
            sb.AppendLine($"p1_oil {oil}");
            sb.AppendLine($"p1_deaths {deaths}");
            sb.AppendLine($"p1_kills {kills}");
            sb.AppendLine($"p1_pos {pos.x.ToString("0.##", CI)},{pos.z.ToString("0.##", CI)}");
            // Base dispenser (lifeline) state.
            sb.AppendLine($"dispenser_hp {dispHp}");
            sb.AppendLine($"dispenser_lvl {dispLvl}");
            sb.AppendLine($"dispenser_alive {(disp != null ? 1 : 0)}");
            sb.AppendLine($"dispenser_pos {dispPos.x.ToString("0.##", CI)},{dispPos.z.ToString("0.##", CI)}");
            // machine data (own lines so the readable fields above stay clean)
            sb.AppendLine($"builds={builds}");
            sb.AppendLine($"refineries={refs}");
            sb.AppendLine($"mines={mines}");

            File.WriteAllText(GdfPath(slot), sb.ToString());
            if (pngBytes != null && pngBytes.Length > 0) File.WriteAllBytes(PngPath(slot), pngBytes);
        }
        catch (Exception e) { Debug.LogError($"[gdf] write slot {slot}: {e.Message}"); }
    }

    /// <summary>Copy a slot's values into the PlayerPrefs keys the Continue restore reads, so the
    /// existing StartGame(load=true) path rebuilds the game. Returns false if the file is missing.</summary>
    public static bool ApplySlotToPrefs(int slot)
    {
        try
        {
            string path = GdfPath(slot);
            if (!File.Exists(path)) return false;
            int wave = 0, metal = 250, oil = 500; bool infinite = false;
            string builds = "", refs = "", mines = "", dispPos = "";
            foreach (var line in File.ReadAllLines(path))
            {
                var kv = Split(line);
                switch (kv.Item1)
                {
                    case "wave": int.TryParse(kv.Item2, NumberStyles.Integer, CI, out wave); break;
                    case "p1_metal": int.TryParse(kv.Item2, NumberStyles.Integer, CI, out metal); break;
                    case "p1_oil": int.TryParse(kv.Item2, NumberStyles.Integer, CI, out oil); break;
                    case "infinite": infinite = kv.Item2 == "1"; break;
                    case "builds": builds = kv.Item2; break;
                    case "refineries": refs = kv.Item2; break;
                    case "mines": mines = kv.Item2; break;
                    case "dispenser_pos": dispPos = kv.Item2; break;
                }
            }
            PlayerPrefs.SetInt("save_exists", 1);
            PlayerPrefs.SetInt("save_wave", wave);
            PlayerPrefs.SetInt("save_metal", metal);
            PlayerPrefs.SetInt("save_oil", oil);
            PlayerPrefs.SetInt("save_infinite", infinite ? 1 : 0);
            PlayerPrefs.SetString("save_builds", builds);
            PlayerPrefs.SetString("save_refineries", refs);
            PlayerPrefs.SetString("save_mines", mines);
            PlayerPrefs.SetString("save_disp_pos", dispPos);
            PlayerPrefs.Save();
            CurrentSlot = slot;
            return true;
        }
        catch (Exception e) { Debug.LogError($"[gdf] apply slot {slot}: {e.Message}"); return false; }
    }

    public static void DeleteSlot(int slot)
    {
        try { if (File.Exists(GdfPath(slot))) File.Delete(GdfPath(slot)); if (File.Exists(PngPath(slot))) File.Delete(PngPath(slot)); }
        catch (Exception e) { Debug.LogWarning($"[gdf] delete slot {slot}: {e.Message}"); }
    }

    /// <summary>Load a slot's thumbnail as a texture (or null). Caller may cache it.</summary>
    public static Texture2D LoadThumb(int slot)
    {
        try
        {
            string path = PngPath(slot);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
        }
        catch { }
        return null;
    }

    static (string, string) Split(string line)
    {
        if (string.IsNullOrEmpty(line)) return ("", "");
        int eq = line.IndexOf('=');
        if (eq >= 0) return (line.Substring(0, eq).Trim(), line.Substring(eq + 1));
        int sp = line.IndexOf(' ');
        if (sp >= 0) return (line.Substring(0, sp).Trim(), line.Substring(sp + 1).Trim());
        return (line.Trim(), "");
    }
}
