using System.Collections.Generic;
using UnityEngine;

/// <summary>3.7: DAILY QUESTS (ЕЖЕДНЕВНЫЕ ЗАДАНИЯ). A self-contained quest board — 27 tasks drawn from
/// the design doc (уничтожение / стройка / работяги / спец / комбо). Progress is tracked through a
/// handful of event hooks scattered in the gameplay code (kills, builds, income, waves, workers,
/// supply crates), rewards are metal/oil paid into the player, and the whole board RESETS every
/// calendar day. Toggle the log with L. Milestone chests fire at 3 / 10 / 20 / 27 completions.
///
/// Design note: the "hard" worker quests (squad level-up, worker builds/repairs) and the mod-activate
/// task are DEFINED here but have no clean event hook yet — they simply stay at 0 until wired. That's
/// fine: dailies reset, and the milestone rewards only ever need a SUBSET of the board completed.</summary>
public static class QuestSystem
{
    public enum Kind
    {
        Kills, KillScreamer, KillGrenadier, KillBrute, KillSandbox, Snipe, KillV1, ShootAir,
        Build, Upgrade, EarnOil, EarnMetal, Logistics, BuildPlasma, BuildLattice,
        HireWorker, WorkerLevel, WorkerBuild, WorkerRepair, SupplyCrate, ModActivate,
        Bhop, TopBuild, SurviveWave, Combo
    }

    public class Quest
    {
        public string id, ru, en;
        public Kind kind;
        public int target, metal, oil;
        public int progress;
        public bool done;
    }

    static readonly List<Quest> Board = new List<Quest>();
    static bool built;
    static int day = -1;

    // Reward payout is decoupled from the event hooks: completions push into these, and
    // PlayerController drains them each frame (adds metal/oil + shows a toast).
    public static int PendingMetal, PendingOil;
    public static readonly Queue<string> Notices = new Queue<string>();

    // combo helper counters (persisted): turrets & conveyors built this day
    static int turretsBuilt, conveyorsBuilt;

    public static bool LogOpen;

    static readonly HashSet<int> TurretTypes = new HashSet<int> { 0, 10, 19, 21, 24, 34, 37, 40, 41 };

    static Quest Q(string id, Kind k, int target, int metal, int oil, string ru, string en)
    {
        var q = new Quest { id = id, kind = k, target = target, metal = metal, oil = oil, ru = ru, en = en };
        Board.Add(q);
        return q;
    }

    static void BuildBoard()
    {
        if (built) return;
        built = true;
        Board.Clear();
        // 🧟 УНИЧТОЖЕНИЕ
        Q("kills100", Kind.Kills, 100, 50, 0, "Истребитель — убить 100 зомби", "Exterminator — kill 100 zombies");
        Q("scream5", Kind.KillScreamer, 5, 0, 100, "Охотник на крикунов — убить 5 Крикунов", "Screamer hunter — kill 5 Screamers");
        Q("gren3", Kind.KillGrenadier, 3, 75, 0, "Сапёр — убить 3 Газовиков", "Sapper — kill 3 Grenadiers");
        Q("boss1", Kind.KillBrute, 1, 150, 0, "Боссобой — свалить Громилу", "Boss-slayer — down a Brute");
        Q("sand50", Kind.KillSandbox, 50, 30, 0, "Чистильщик — убить 50 зомби в Песочнице", "Cleaner — kill 50 in Sandbox");
        Q("snipe20", Kind.Snipe, 20, 40, 0, "Снайпер — 20 убийств с 50+ метров", "Sniper — 20 kills from 50+ m");
        Q("v1_10", Kind.KillV1, 10, 0, 60, "Фаустник — 10 убийств ракетой ФАУ-1", "Faustman — 10 kills with the V-1");
        Q("air3", Kind.ShootAir, 3, 50, 0, "Зенитчик — сбить 3 дрона/самолёта", "Ack-ack — down 3 drones/planes");
        // 🏗️ СТРОИТЕЛЬСТВО И ЭКОНОМИКА
        Q("build5", Kind.Build, 5, 40, 0, "Строитель — построить 5 объектов", "Builder — build 5 objects");
        Q("upg2", Kind.Upgrade, 2, 30, 0, "Инженер — улучшить постройки на 2 уровня", "Engineer — 2 levels of upgrades");
        Q("oil500", Kind.EarnOil, 500, 0, 100, "Нефтяной магнат — добыть 500 нефти", "Oil baron — earn 500 oil");
        Q("metal300", Kind.EarnMetal, 300, 80, 0, "Металлург — добыть 300 металла", "Metallurgist — earn 300 metal");
        Q("logi1", Kind.Logistics, 1, 25, 0, "Логист — построить конвейер к добыче", "Logistician — lay a conveyor");
        Q("plasma2", Kind.BuildPlasma, 2, 70, 0, "Плазма-техник — 2 плазма-турели", "Plasma-tech — 2 plasma turrets");
        Q("lattice2", Kind.BuildLattice, 2, 50, 0, "Электрик — 2 решётки-ловушки", "Electrician — 2 lattice fences");
        // 🛠️ РАБОТЯГИ
        Q("hire2", Kind.HireWorker, 2, 50, 0, "Бригадир — нанять 2 работяг", "Foreman — hire 2 workers");
        Q("wlvl3", Kind.WorkerLevel, 1, 75, 0, "Наставник — отряд работяг до 3 ур.", "Mentor — worker squad to lvl 3");
        Q("wbuild3", Kind.WorkerBuild, 3, 60, 0, "Прораб — работяги строят 3 турели", "Overseer — workers build 3 turrets");
        Q("wrepair5", Kind.WorkerRepair, 5, 40, 0, "Ремонтник — работяги чинят 5 построек", "Repairman — workers fix 5 builds");
        // 🎯 СПЕЦИАЛЬНЫЕ
        Q("supply1", Kind.SupplyCrate, 1, 60, 60, "Снабженец — подобрать ящик снабжения", "Quartermaster — grab a supply crate");
        Q("mod1", Kind.ModActivate, 1, 100, 0, "Испытатель модов — активировать мод", "Mod tester — activate a mod");
        Q("bhop1", Kind.Bhop, 1, 20, 0, "Бхопер — разогнаться до 50 м/с", "Bhopper — reach 50 m/s");
        Q("top1", Kind.TopBuild, 1, 30, 0, "Строитель сверху — труба/конвейер (T)", "Top-builder — pipe/conveyor (T)");
        Q("evac60", Kind.SurviveWave, 60, 200, 200, "Эвакуация — дожить до 60-й волны", "Evacuation — survive to wave 60");
        // 🧩 КОМБО
        Q("c_master", Kind.Combo, 1, 25, 0, "Мастер на все руки — «Строитель» + «Металлург»", "Jack of all trades — Builder + Metallurgist");
        Q("c_horde", Kind.Combo, 1, 0, 30, "Охотник на орду — 50 убийств + 3 турели", "Horde hunter — 50 kills + 3 turrets");
        Q("c_indust", Kind.Combo, 1, 40, 0, "Индустриализация — 1 работяга + 2 конвейера", "Industrialization — 1 worker + 2 conveyors");
    }

    static Quest Find(string id) { foreach (var q in Board) if (q.id == id) return q; return null; }

    /// <summary>Reset the board when the calendar day changes; otherwise load today's saved progress.</summary>
    static void EnsureDaily()
    {
        BuildBoard();
        int today = System.DateTime.Now.Year * 1000 + System.DateTime.Now.DayOfYear;
        if (day == today) return;
        int savedDay = PlayerPrefs.GetInt("quest_day", -1);
        if (savedDay != today)
        {
            // brand-new day: wipe everything
            foreach (var q in Board) { q.progress = 0; q.done = false; }
            turretsBuilt = 0; conveyorsBuilt = 0;
            PlayerPrefs.SetInt("quest_day", today);
            foreach (var q in Board) { PlayerPrefs.SetInt("qp_" + q.id, 0); PlayerPrefs.SetInt("qc_" + q.id, 0); }
            PlayerPrefs.SetInt("q_turrets", 0); PlayerPrefs.SetInt("q_conv", 0);
            for (int m = 0; m < 4; m++) PlayerPrefs.SetInt("qm_" + m, 0);
            PlayerPrefs.Save();
        }
        else
        {
            // same day, fresh session: reload persisted progress
            foreach (var q in Board) { q.progress = PlayerPrefs.GetInt("qp_" + q.id, 0); q.done = PlayerPrefs.GetInt("qc_" + q.id, 0) == 1; }
            turretsBuilt = PlayerPrefs.GetInt("q_turrets", 0);
            conveyorsBuilt = PlayerPrefs.GetInt("q_conv", 0);
        }
        day = today;
    }

    static void Persist(Quest q)
    {
        PlayerPrefs.SetInt("qp_" + q.id, q.progress);
        PlayerPrefs.SetInt("qc_" + q.id, q.done ? 1 : 0);
    }

    static void Complete(Quest q)
    {
        if (q.done) return;
        q.done = true;
        q.progress = q.target;
        PendingMetal += q.metal; PendingOil += q.oil;
        string rew = (q.metal > 0 ? $"+{q.metal} мет." : "") + (q.oil > 0 ? $" +{q.oil} нефти" : "");
        Notices.Enqueue(Lang.T($"✅ {q.ru}  {rew}", $"✅ {q.en}  " + (q.metal > 0 ? $"+{q.metal} metal" : "") + (q.oil > 0 ? $" +{q.oil} oil" : "")));
        Persist(q);
        Milestones();
    }

    static void Bump(Kind k, int amount)
    {
        if (amount <= 0) return;
        EnsureDaily();
        bool any = false;
        foreach (var q in Board)
        {
            if (q.kind != k || q.done) continue;
            q.progress = Mathf.Min(q.target, q.progress + amount);
            Persist(q);
            if (q.progress >= q.target) Complete(q);
            any = true;
        }
        if (any) RecheckCombos();
    }

    static void RecheckCombos()
    {
        var build5 = Find("build5"); var metal300 = Find("metal300"); var kills = Find("kills100");
        var hire = Find("hire2");
        // Master: Строитель + Металлург
        var m = Find("c_master");
        if (m != null && !m.done && build5 != null && metal300 != null && build5.done && metal300.done) Complete(m);
        // Horde: 50 kills + 3 turrets
        var h = Find("c_horde");
        if (h != null && !h.done && kills != null && kills.progress >= 50 && turretsBuilt >= 3) Complete(h);
        // Industrialization: 1 worker + 2 conveyors
        var ind = Find("c_indust");
        if (ind != null && !ind.done && hire != null && hire.progress >= 1 && conveyorsBuilt >= 2) Complete(ind);
    }

    static void Milestones()
    {
        int done = 0; foreach (var q in Board) if (q.done) done++;
        void Give(int idx, int need, int metal, int oil, string ru, string en)
        {
            if (done < need || PlayerPrefs.GetInt("qm_" + idx, 0) == 1) return;
            PlayerPrefs.SetInt("qm_" + idx, 1);
            PendingMetal += metal; PendingOil += oil;
            Notices.Enqueue(Lang.T($"🎁 {ru}  +{metal} мет. +{oil} нефти", $"🎁 {en}  +{metal} metal +{oil} oil"));
        }
        Give(0, 3, 50, 50, "3 задания выполнено", "3 quests done");
        Give(1, 10, 400, 400, "Малый сундук снабжения (10)", "Small supply chest (10)");
        Give(2, 20, 900, 900, "Большой сундук снабжения (20)", "Big supply chest (20)");
        Give(3, 27, 2500, 2500, "ЛЕГЕНДАРНЫЙ сундук (все 27!)", "LEGENDARY chest (all 27!)");
    }

    // ─────────────────────────── event hooks (called from gameplay code) ───────────────────────────
    public static void OnKill(Zombie.Kind kind, string by, Vector3 zombiePos, PlayerController p)
    {
        Bump(Kind.Kills, 1);
        if (kind == Zombie.Kind.Screamer) Bump(Kind.KillScreamer, 1);
        if (kind == Zombie.Kind.Grenadier) Bump(Kind.KillGrenadier, 1);
        if (kind == Zombie.Kind.Brute) Bump(Kind.KillBrute, 1);
        if (GameRoot.Sandbox) Bump(Kind.KillSandbox, 1);
        if (p != null && (zombiePos - p.transform.position).magnitude >= 50f) Bump(Kind.Snipe, 1);
        if (!string.IsNullOrEmpty(by) && (by.Contains("ФАУ-1") || by.Contains("V-1") || by.Contains("VOne"))) Bump(Kind.KillV1, 1);
    }

    public static void OnBuild(int type, bool topMode)
    {
        EnsureDaily();
        Bump(Kind.Build, 1);
        if (type == 41) Bump(Kind.BuildPlasma, 1);
        if (type == 42) Bump(Kind.BuildLattice, 1);
        if (type == 30) { Bump(Kind.Logistics, 1); conveyorsBuilt++; PlayerPrefs.SetInt("q_conv", conveyorsBuilt); }
        if (TurretTypes.Contains(type)) { turretsBuilt++; PlayerPrefs.SetInt("q_turrets", turretsBuilt); }
        if (topMode && (type == 27 || type == 30)) Bump(Kind.TopBuild, 1);
        RecheckCombos();
    }

    public static void OnUpgrade() => Bump(Kind.Upgrade, 1);
    public static void OnEarnMetal(int amount) => Bump(Kind.EarnMetal, amount);
    public static void OnEarnOil(int amount) => Bump(Kind.EarnOil, amount);
    public static void OnHireWorker() => Bump(Kind.HireWorker, 1);
    public static void OnSupplyCrate() => Bump(Kind.SupplyCrate, 1);
    public static void OnAirKill() => Bump(Kind.ShootAir, 1);
    public static void OnModActivate() => Bump(Kind.ModActivate, 1);
    public static void OnBhop(float speed) { if (speed >= 50f) Bump(Kind.Bhop, 1); }

    public static void OnWaveReached(int wave)
    {
        EnsureDaily();
        var q = Find("evac60");
        if (q == null || q.done) return;
        q.progress = Mathf.Min(q.target, wave);
        Persist(q);
        if (q.progress >= q.target) Complete(q);
    }

    // ─────────────────────────── quest-log overlay (drawn by PlayerController.OnGUI) ───────────────
    public static void DrawLog(float w, float h)
    {
        EnsureDaily();
        float pw = 560f, ph = 640f, x = (w - pw) * 0.5f, y = (h - ph) * 0.5f;
        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(x, y, pw, ph), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        int done = 0; foreach (var q in Board) if (q.done) done++;
        GUI.Label(new Rect(x, y + 8f, pw, 30f), Lang.T($"ЕЖЕДНЕВНЫЕ ЗАДАНИЯ  ({done}/{Board.Count})", $"DAILY QUESTS  ({done}/{Board.Count})"), title);
        var hint = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.7f, 0.75f, 0.8f);
        GUI.Label(new Rect(x, y + 36f, pw, 18f), Lang.T("L — закрыть · сбрасываются каждый день", "L — close · resets every day"), hint);
        GUI.color = Color.white;

        var row = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
        float ry = y + 60f;
        foreach (var q in Board)
        {
            GUI.color = q.done ? new Color(0.5f, 1f, 0.55f) : new Color(0.86f, 0.88f, 0.9f);
            string prog = q.target > 1 ? $"  [{Mathf.Min(q.progress, q.target)}/{q.target}]" : (q.done ? "  [✓]" : "");
            GUI.Label(new Rect(x + 16f, ry, pw - 32f, 20f), (q.done ? "✅ " : "◻ ") + Lang.T(q.ru, q.en) + prog, row);
            ry += 21f;
        }
        GUI.color = Color.white;
    }
}
