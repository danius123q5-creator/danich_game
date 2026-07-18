using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>3.2: the runtime for node-graph MODS authored in the visual Mod-Builder. Loads every
/// <c>.zmod</c> file from the game's "mods" folder (next to the launcher/saves), and executes their
/// rules: an EVENT (game start / wave start / zombie killed) fires a list of ACTIONS (give metal,
/// spawn a zombie, multiply wall HP, …). The Mod-Builder emits the same plain rule lines this parses.
///
/// File format (one rule per line): <c>EVENT ACTION:arg ACTION:arg …</c>. Lines starting with '#'
/// (the Mod-Builder's node-layout) are ignored by the game.</summary>
public static class ModRuntime
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // Global stat multipliers set by mods (read by Wall/Rpg/Sentry/PlayerController/Dispenser). Default 1 = off.
    public static float WallHpMult = 1f, RpgDmgMult = 1f, TurretDmgMult = 1f, PlayerHpMult = 1f,
                        PlayerSpeedMult = 1f, DispenserHpMult = 1f;

    public static bool Active { get; private set; }
    public static int RuleCount { get; private set; }

    class Rule { public string evt; public readonly List<KeyValuePair<string, float>> actions = new List<KeyValuePair<string, float>>(); }
    static readonly List<Rule> rules = new List<Rule>();

    public static string ModsDir
    {
        get { string d = Path.Combine(SaveSystem.BaseDir, "mods"); try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { } return d; }
    }

    /// <summary>Reset + load all mods. Called once when a world is built. Applies multiplier actions
    /// immediately (so buildings pick them up), and keeps the rules for later event firing.</summary>
    public static void Load()
    {
        rules.Clear();
        WallHpMult = RpgDmgMult = TurretDmgMult = PlayerHpMult = PlayerSpeedMult = DispenserHpMult = 1f;
        Active = false; RuleCount = 0;

        try
        {
            string dir = Path.Combine(SaveSystem.BaseDir, "mods");
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.zmod"))
                foreach (var raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;             // layout / comments
                    if (line.StartsWith("ZMOD", StringComparison.OrdinalIgnoreCase)) continue; // header
                    var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tok.Length < 1) continue;
                    var r = new Rule { evt = tok[0].ToUpperInvariant() };
                    for (int i = 1; i < tok.Length; i++)
                    {
                        int c = tok[i].IndexOf(':');
                        string name = (c >= 0 ? tok[i].Substring(0, c) : tok[i]).ToUpperInvariant();
                        float arg = 0f;
                        if (c >= 0) float.TryParse(tok[i].Substring(c + 1), NumberStyles.Float, CI, out arg);
                        r.actions.Add(new KeyValuePair<string, float>(name, arg));
                    }
                    rules.Add(r);
                }
        }
        catch (Exception e) { Debug.LogWarning($"[zmod] load: {e.Message}"); }

        RuleCount = rules.Count;
        Active = rules.Count > 0;

        // Apply multiplier actions up front (they're global and safe with no player yet), so the
        // starter base / early builds already get the buff.
        foreach (var r in rules)
            foreach (var a in r.actions)
                ApplyMult(a.Key, a.Value);
    }

    static void ApplyMult(string action, float v)
    {
        switch (action)
        {
            case "WALL_HP_MULT":     if (v > 0f) WallHpMult = v; break;
            case "RPG_DMG_MULT":     if (v > 0f) RpgDmgMult = v; break;
            case "TURRET_DMG_MULT":  if (v > 0f) TurretDmgMult = v; break;
            case "PLAYER_HP_MULT":   if (v > 0f) PlayerHpMult = v; break;
            case "PLAYER_SPEED_MULT":if (v > 0f) PlayerSpeedMult = v; break;
            case "DISPENSER_HP_MULT":if (v > 0f) DispenserHpMult = v; break;
        }
    }

    // ---- event hooks (called from the game) ----
    public static void OnGameStart()     => Fire("GAME_START");
    public static void OnWaveStart()     => Fire("WAVE_START");
    public static void OnWaveClear()     => Fire("WAVE_CLEAR");
    public static void OnZombieKilled()  => Fire("ZOMBIE_KILLED");
    public static void OnPlayerDamaged() => Fire("PLAYER_DAMAGED");
    public static void OnPlayerDied()    => Fire("PLAYER_DIED");
    public static void OnBuildingBuilt() => Fire("BUILDING_BUILT");

    static void Fire(string evt)
    {
        if (!Active) return;
        PlayerController p = null;
        foreach (var r in rules)
        {
            if (r.evt != evt) continue;
            foreach (var a in r.actions)
            {
                // Multiplier actions already applied in Load(); the rest are instant effects.
                if (a.Key.EndsWith("_MULT")) continue;
                if (p == null) p = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                RunInstant(a.Key, a.Value, p);
            }
        }
    }

    static void RunInstant(string action, float v, PlayerController p)
    {
        int n = Mathf.RoundToInt(v);
        switch (action)
        {
            case "GIVE_METAL":  if (p != null) p.AddMetal(n); break;
            case "GIVE_OIL":    if (p != null) p.AddOil(n); break;
            case "GIVE_AMMO":   if (p != null) p.AddAmmo(n); break;
            case "HEAL_PLAYER": if (p != null) p.Heal(n); break;
            case "DAMAGE_PLAYER": if (p != null) p.TakeDamage(v); break;
            case "ADD_SCORE":   if (p != null) p.Score += n; break;
            case "DAMAGE_ZOMBIES":
                foreach (var z in new List<Zombie>(Zombie.All)) if (z != null) z.TakeDamage(v);
                break;
            case "KILL_ALL_ZOMBIES":
                foreach (var z in new List<Zombie>(Zombie.All)) if (z != null) z.TakeDamage(999999f);
                break;
            case "SPAWN_ZOMBIE":
                SpawnZombie(Mathf.Clamp(n, 0, 4), p);
                break;
            case "SPAWN_HORDE":
                for (int i = 0; i < Mathf.Clamp(n, 1, 40); i++) SpawnZombie(0, p);
                break;
        }
    }

    static void SpawnZombie(int kind, PlayerController p)
    {
        Vector3 c = p != null ? p.transform.position : Vector3.zero;
        Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(18f, 40f);
        Vector3 pos = c + new Vector3(r.x, 0f, r.y);
        pos.y = GameBootstrap.Hill(pos.x, pos.z) + 1f;
        Zombie.Create(pos, (Zombie.Kind)kind);
    }
}
