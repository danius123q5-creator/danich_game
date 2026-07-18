using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Мини Source-энтити-рантайм поверх VmfEntity: исполняет поведение импортнутых из .vmf
/// энтити и их I/O-связи. Делает лабораторные карты «живыми»:
///   • func_door / func_movelinear — открываются/двигаются (Open/Close/Toggle), авто по проксимити
///   • func_button                 — нажатие E рядом → OnPressed
///   • trigger_multiple/once       — игрок в зоне → OnTrigger / OnStartTouch
///   • math_counter                — Add/Subtract/SetValue → OnHitMax/OnHitMin
///   • logic_relay                 — Trigger → OnTrigger ;  logic_auto → OnMapSpawn на старте
///   • провода: OnX → target,input,param,delay (VmfEntity.outputs)
/// Не полный порт Source, но покрывает типовую логику лабы. Вешается на корень импорта. 2026-07-13.
/// </summary>
public class VmfRuntime : MonoBehaviour
{
    // ---- состояние двери/движка ----
    class Mover
    {
        public VmfEntity e;
        public bool rotating;              // true = func_door_rotating (крутится), false = линейная
        public Vector3 closedPos, openPos; // для линейной
        public Quaternion closedRot, openRot; // для вращающейся
        public float t;          // 0=закрыто, 1=открыто
        public int dir;          // +1 открывается, -1 закрывается, 0 стоит
        public float speed;      // 1/сек (за сколько долей в секунду)
        public bool autoProx;    // авто-открытие по проксимити
        public float autoRadius;
    }
    class Trigger
    {
        public VmfEntity e;
        public Vector3 center, half;   // AABB зоны (точный бокс, а не сфера — иначе ловит издалека)
        public bool inside, fired, once, endZone;
    }

    // Колбэк «зона-конец»: игрок вошёл в триггер с именем titry/exit/credits/выход/улиц → финал/титры.
    // SimEscape вешает сюда показ титров + возврат в меню. 2026-07-13.
    public static System.Action OnEndZone;
    static bool IsEndName(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        return n.Contains("titr") || n.Contains("титр") || n.Contains("credit") || n.Contains("exit")
            || n.Contains("выход") || n.Contains("улиц") || n.Contains("final") || n.Contains("финал")
            || n.Contains("end") || n.Contains("конец") || n.Contains("konec") || n.Contains("koniec");
    }
    struct Pending { public VmfEntity target; public string input, param; public float at; }

    readonly List<Mover> movers = new List<Mover>();
    readonly List<Trigger> triggers = new List<Trigger>();
    readonly List<VmfEntity> buttons = new List<VmfEntity>();
    readonly Dictionary<VmfEntity, float> counters = new Dictionary<VmfEntity, float>();
    readonly List<Pending> pending = new List<Pending>();
    Transform player;
    float now;

    public static VmfRuntime Ensure(Transform mapRoot)
    {
        var rt = mapRoot != null ? mapRoot.GetComponent<VmfRuntime>() : null;
        if (rt == null && mapRoot != null) rt = mapRoot.gameObject.AddComponent<VmfRuntime>();
        if (rt != null) rt.Build();
        return rt;
    }

    void Awake() { FindPlayer(); }

    void FindPlayer()
    {
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) player = pc.transform;
        else if (Camera.main != null) player = Camera.main.transform;
    }

    public void Build()
    {
        movers.Clear(); triggers.Clear(); buttons.Clear(); counters.Clear(); pending.Clear();
        if (player == null) FindPlayer();

        foreach (var e in VmfEntity.All)
        {
            if (e == null) continue;
            string cls = e.classname ?? "";
            switch (cls)
            {
                case "func_door":
                case "func_door_rotating":
                case "func_movelinear":
                case "func_platrot":
                    SetupDoor(e); break;
                case "func_button":
                case "func_rot_button":
                    buttons.Add(e); break;
                case "math_counter":
                    counters[e] = ParseF(e.Get("startvalue", e.Get("StartValue", "0"))); break;
                case "logic_auto":
                    // старт карты: разошлём OnMapSpawn после кадра
                    Schedule(e, "OnMapSpawn", "", 0.1f); break;
            }
            if (cls.StartsWith("trigger_")) SetupTrigger(e);
        }

        // Source: на старте карты у всех энтити срабатывает выход OnSpawn (logic_relay/logic_auto и пр.)
        // → провода OnSpawn исполняются (напр. relay: OnSpawn → start_door 2, Open, задержка 10с).
        foreach (var e in VmfEntity.All)
            if (e != null) FireOutputs(e, "OnSpawn", "");
    }

    // ---------- настройка ----------
    void SetupDoor(VmfEntity e)
    {
        if (e.moveRoot == null) return;
        var m = new Mover { e = e, closedPos = e.moveRoot.position };
        int flags = (int)ParseF(e.Get("spawnflags", "0"));

        if (e.classname == "func_door_rotating" || e.classname == "func_platrot")
        {
            // ВРАЩЕНИЕ вокруг петли (origin энтити) на distance° по вертикали (Y).
            m.rotating = true;
            float ang = ParseF(e.Get("distance", "90")); if (Mathf.Abs(ang) < 0.1f) ang = 90f;
            if ((flags & 2) != 0) ang = -ang;             // флаг Reverse Dir
            m.closedRot = e.moveRoot.rotation;
            m.openRot = m.closedRot * Quaternion.Euler(0f, ang, 0f);
            float spd = ParseF(e.Get("speed", "100"));    // °/сек
            float dur = spd > 1f ? Mathf.Clamp(Mathf.Abs(ang) / spd, 0.25f, 4f) : 1f;
            m.speed = 1f / dur;
            m.autoProx = true;                            // крутящиеся двери открываем по проксимити
        }
        else
        {
            // ЛИНЕЙНАЯ (func_door / func_movelinear) — едет по movedir.
            Vector3 mdir = MoveDir(e);
            float size = ProjectedSize(e.moveRoot, mdir);
            float dist = Mathf.Max(0.2f, size);
            m.openPos = m.closedPos + mdir * dist;
            float spd = ParseF(e.Get("speed", "100"));
            float dur = spd > 1f ? Mathf.Clamp(dist / (spd * ImportScaleGuess()), 0.25f, 4f) : 1f;
            m.speed = 1f / dur;
            m.autoProx = string.IsNullOrEmpty(e.Targetname) && (flags & 256) == 0 && e.classname == "func_door";
        }
        m.autoRadius = Mathf.Max(2f, e.boundsRadius > 0f ? e.boundsRadius : 3f);
        movers.Add(m);
    }

    void SetupTrigger(VmfEntity e)
    {
        var t = new Trigger { e = e, once = e.classname == "trigger_once" };
        bool hasBox = e.boundsHalf.sqrMagnitude > 1e-4f;
        t.center = hasBox ? e.boundsCenter : e.transform.position;
        t.half = hasBox ? e.boundsHalf : Vector3.one * 1.2f;
        t.endZone = IsEndName(e.Targetname);   // триггер titry/exit/выход/konec → финал/титры
        triggers.Add(t);
    }

    // ---------- цикл ----------
    void Update()
    {
        now += Time.deltaTime;
        if (player == null) FindPlayer();

        // отложенные I/O
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (now >= pending[i].at)
            {
                var p = pending[i]; pending.RemoveAt(i);
                Fire(p.target, p.input, p.param);
            }
        }

        // двери
        foreach (var m in movers)
        {
            if (m.autoProx && player != null)
            {
                bool near = (player.position - m.closedPos).sqrMagnitude < m.autoRadius * m.autoRadius
                         || (player.position - m.openPos).sqrMagnitude < m.autoRadius * m.autoRadius;
                m.dir = near ? 1 : -1;
            }
            if (m.dir != 0)
            {
                m.t = Mathf.Clamp01(m.t + m.dir * m.speed * Time.deltaTime);
                if (m.e.moveRoot != null)
                {
                    if (m.rotating) m.e.moveRoot.rotation = Quaternion.Slerp(m.closedRot, m.openRot, m.t);
                    else m.e.moveRoot.position = Vector3.Lerp(m.closedPos, m.openPos, m.t);
                }
                if (m.t <= 0f || m.t >= 1f) m.dir = 0;
            }
        }

        // триггеры
        foreach (var t in triggers)
        {
            if (player == null) continue;
            // ТОЧНЫЙ бокс-тест (а не сфера): игрок внутри AABB зоны + паддинг под габарит игрока.
            Vector3 dd = player.position - t.center;
            bool inside = Mathf.Abs(dd.x) <= t.half.x + 0.4f
                       && Mathf.Abs(dd.z) <= t.half.z + 0.4f
                       && Mathf.Abs(dd.y) <= t.half.y + 1.6f;
            if (inside && !t.inside)
            {
                if (!(t.once && t.fired))
                {
                    FireOutputs(t.e, "OnStartTouch", "");
                    FireOutputs(t.e, "OnTrigger", "");
                    if (t.endZone) OnEndZone?.Invoke();   // зона-конец → титры/закрыть игру
                    t.fired = true;
                }
            }
            else if (!inside && t.inside) FireOutputs(t.e, "OnEndTouch", "");
            t.inside = inside;
        }

        // кнопки (E рядом)
        if (player != null && Input.GetKeyDown(KeyCode.E))
        {
            foreach (var b in buttons)
            {
                if ((player.position - b.transform.position).sqrMagnitude < 9f) // ~3м
                {
                    FireOutputs(b, "OnPressed", "");
                    FireOutputs(b, "OnUseLocked", "");
                }
            }
        }
    }

    // ---------- I/O ----------
    void Schedule(VmfEntity target, string input, string param, float delay)
    {
        if (target == null) return;
        pending.Add(new Pending { target = target, input = input, param = param, at = now + Mathf.Max(0f, delay) });
    }

    // Разослать выход outputName у энтити e по её проводам.
    void FireOutputs(VmfEntity e, string outputName, string defParam)
    {
        if (e == null) return;
        foreach (var c in e.outputs)
        {
            if (!string.Equals(c.outputName, outputName, System.StringComparison.OrdinalIgnoreCase)) continue;
            var target = VmfEntity.ByName(c.target);
            if (target == null) continue;
            string p = string.IsNullOrEmpty(c.param) ? defParam : c.param;
            Schedule(target, c.input, p, c.delay);
        }
    }

    // Исполнить вход input на энтити target.
    void Fire(VmfEntity target, string input, string param)
    {
        if (target == null) return;
        string cls = target.classname ?? "";
        string inp = input ?? "";

        // двери/движки
        var mv = FindMover(target);
        if (mv != null)
        {
            if (Eq(inp, "Open")) { mv.autoProx = false; mv.dir = 1; }
            else if (Eq(inp, "Close")) { mv.autoProx = false; mv.dir = -1; }
            else if (Eq(inp, "Toggle")) { mv.autoProx = false; mv.dir = mv.t >= 0.5f ? -1 : 1; }
            else if (Eq(inp, "Lock")) { mv.autoProx = false; }
        }

        // math_counter
        if (cls == "math_counter" && counters.ContainsKey(target))
        {
            float val = counters[target];
            float min = ParseF(target.Get("min", "0")), max = ParseF(target.Get("max", "0"));
            bool hasMax = max != 0f || target.kv.ContainsKey("max");
            if (Eq(inp, "Add")) val += ParseF(param);
            else if (Eq(inp, "Subtract")) val -= ParseF(param);
            else if (Eq(inp, "SetValue") || Eq(inp, "SetValueNoFire")) val = ParseF(param);
            if (hasMax) val = Mathf.Min(val, max);
            if (target.kv.ContainsKey("min")) val = Mathf.Max(val, min);
            counters[target] = val;
            if (!Eq(inp, "SetValueNoFire"))
            {
                FireOutputs(target, "OutValue", val.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (hasMax && val >= max) FireOutputs(target, "OnHitMax", "");
                if (target.kv.ContainsKey("min") && val <= min) FireOutputs(target, "OnHitMin", "");
            }
        }

        // logic_relay / logic_auto / общие
        if (cls == "logic_relay" && (Eq(inp, "Trigger") || Eq(inp, "Toggle")))
            FireOutputs(target, "OnTrigger", "");
        if (cls == "logic_auto" && Eq(inp, "OnMapSpawn"))
            FireOutputs(target, "OnMapSpawn", "");

        // универсально: вход "Trigger"/"Fire" → одноимённый выход (реле-подобные)
        if (Eq(inp, "Trigger")) FireOutputs(target, "OnTrigger", param);
    }

    // ---------- хелперы ----------
    Mover FindMover(VmfEntity e) { foreach (var m in movers) if (m.e == e) return m; return null; }
    static bool Eq(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    static float ParseF(string s) { float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f); return f; }

    // грубый масштаб импорта (Source→Unity). Если не угадаем — двери всё равно едут на bounds.
    static float ImportScaleGuess() => 0.03f;

    // размер моврута вдоль направления (для дистанции открытия двери)
    static float ProjectedSize(Transform root, Vector3 dir)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return 2f;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 s = b.size;
        return Mathf.Abs(Vector3.Dot(new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z)), s));
    }

    // направление движения func_door из "movedir" (QAngle pitch yaw roll). Нет → вверх.
    static Vector3 MoveDir(VmfEntity e)
    {
        string md = e.Get("movedir", "");
        if (!string.IsNullOrEmpty(md))
        {
            var p = md.Split(' ');
            if (p.Length >= 2 && float.TryParse(p[0], out var pit) && float.TryParse(p[1], out var yaw))
            {
                float pr = pit * Mathf.Deg2Rad, yr = yaw * Mathf.Deg2Rad;
                // Source forward (x,y,z=up), затем swap в Unity (x, z, y)
                Vector3 u = new Vector3(Mathf.Cos(pr) * Mathf.Cos(yr), -Mathf.Sin(pr), Mathf.Cos(pr) * Mathf.Sin(yr));
                if (u.sqrMagnitude > 1e-4f) return u.normalized;
            }
        }
        return Vector3.up;
    }
}
