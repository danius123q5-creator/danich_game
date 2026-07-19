using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Работяга — союзная пешка игрока. Нанимается за металл (P), качается отрядом (Shift+P).
/// Роли по приоритету: 1) чинит повреждённые постройки, 2) достраивает кольцо обороны
/// (турели) вокруг базы — УМНО, ровными слотами на радиусе, а не по всей карте,
/// 3) качает постройки до макс. уровня, 4) если делать нечего — носит игроку металл и нефть
/// (курьер). Трудится бесплатно (это и есть его ценность — автоматизация). Гибнет, если рядом зомби.
///
/// Кинематический агент: двигается через transform и снапится к рельефу, БЕЗ коллайдера —
/// чтобы не перекрывать пули игрока, LOS турелей и не толкаться с зомби. Хост/оффлайн.
/// </summary>
public class AllyPawn : MonoBehaviour
{
    public const int MaxCount = 12;
    public const int MaxTier  = 5;
    public const int UnlockWave = 30; // работяги — для лейт-гейма, не для читерского старта

    // Разблокированы только с 30-й волны (иначе спам пешек в начале ломает баланс).
    public static bool Unlocked => GameManager.Instance != null && GameManager.Instance.WaveNumber >= UnlockWave;

    // Общий уровень отряда — качается за металл, применяется сразу ко всем пешкам.
    public static int Tier { get; private set; } = 1;
    public static readonly List<AllyPawn> All = new List<AllyPawn>();
    public static int Count => All.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); Tier = 1; }

    // Цена найма растёт с числом пешек; цена прокачки — с уровнем отряда.
    public static int RecruitCost() => 120 + 45 * Count;
    public static int UpgradeCost() => 220 * Tier;

    // --- статы по уровню отряда ---
    static float MaxHP       => 120f + Tier * 40f;
    static float MoveSpeed   => 5.5f;
    static float RepairRate  => 10f + Tier * 6f;   // HP/с ремонта построек
    static int   InvestChunk => 20 + Tier * 15;    // металла за тик апгрейда (кулдаун у Invest свой)
    static int   HaulMetal   => 12 + Tier * 10;    // металла за одну ходку
    static int   HaulOil     => 8 + Tier * 7;      // нефти за одну ходку

    const float HaulInterval = 4f;    // как часто курьер приносит ресурсы
    const float WorkRange    = 2.4f;  // дистанция «работы» у постройки
    const float PickRadius   = 70f;   // как далеко ищет постройки
    const float ThinkEvery   = 0.4f;

    // «Умная» стройка: ровное кольцо ТУРЕЛЕЙ (тип 0) вокруг базы — концентрированно, не по всей карте.
    const int   RingSlots  = 8;       // сколько турелей в кольце (жёсткий лимит)
    const float RingRadius = 15f;     // радиус кольца от центра базы
    const float SlotFree   = 4f;      // слот считается занятым, если рядом уже есть постройка
    const int   SentryType = 0;       // id турели в контракте Buildable

    const float CapRadius = 90f;      // как далеко пешка идёт захватывать НПЗ

    PlayerController owner;
    float health;
    Transform hat, tool;
    float nextThink, nextHaul, nextHurt, nextBuild;
    Buildable job;         // текущая постройка (ремонт/апгрейд)
    Vector3 buildTarget;   // куда идём строить турель
    bool hasBuild;         // есть слот под стройку
    Refinery capTarget;    // НПЗ, который идём захватывать
    bool courier;          // делать нечего → несём ресурсы игроку

    // Пешка считается «в зоне» НПЗ — засчитывается в захват вместо игрока.
    public static bool AnyInZone(Vector3 c, float radius)
    {
        float rSq = radius * radius;
        foreach (var p in All) if (p != null && (p.transform.position - c).sqrMagnitude <= rSq) return true;
        return false;
    }

    public static void Spawn(PlayerController player)
    {
        if (player == null) return;
        var root = new GameObject("AllyPawn");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 pos = player.transform.position + fwd * 2.5f + Vector3.right * Random.Range(-1f, 1f);
        pos.y = GameBootstrap.Hill(pos.x, pos.z);
        root.transform.position = pos;
        var p = root.AddComponent<AllyPawn>();
        p.owner = player;
        p.health = MaxHP;
        p.BuildModel();
        Effects.Upgrade(pos + Vector3.up * 1f); // «прибыл» — динь + искры
    }

    public static void UpgradeTier()
    {
        if (Tier < MaxTier) Tier++;
        foreach (var p in All) if (p != null) p.health = MaxHP; // подлечить весь отряд на апгрейде
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void BuildModel()
    {
        Color suit = new Color(0.20f, 0.42f, 0.72f);  // синяя роба
        Color skin = new Color(0.85f, 0.68f, 0.55f);
        Color hatc = new Color(0.95f, 0.78f, 0.15f);  // жёлтая каска
        Color dark = new Color(0.12f, 0.14f, 0.18f);

        Prim(PrimitiveType.Capsule, new Vector3(-0.18f, 0.5f, 0f), new Vector3(0.2f, 0.5f, 0.2f), dark); // ноги
        Prim(PrimitiveType.Capsule, new Vector3(0.18f, 0.5f, 0f),  new Vector3(0.2f, 0.5f, 0.2f), dark);
        Prim(PrimitiveType.Capsule, new Vector3(0f, 1.25f, 0f),    new Vector3(0.55f, 0.45f, 0.38f), suit); // корпус
        Prim(PrimitiveType.Sphere,  new Vector3(0f, 1.9f, 0f),     new Vector3(0.42f, 0.42f, 0.42f), skin); // голова
        hat = Prim(PrimitiveType.Sphere, new Vector3(0f, 2.05f, 0f), new Vector3(0.5f, 0.32f, 0.5f), hatc).transform; // каска

        // гаечный ключ в руке
        tool = new GameObject("Tool").transform;
        tool.SetParent(transform, false);
        tool.localPosition = new Vector3(0.42f, 1.35f, 0.35f);
        var wr = Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.1f, 0.1f, 0.5f), new Color(0.6f, 0.6f, 0.65f));
        wr.transform.SetParent(tool, false);
    }

    Transform Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
        return g.transform;
    }

    void Update()
    {
        if (owner == null || owner.IsDead) { Idle(); return; }

        if (Time.time >= nextThink)
        {
            nextThink = Time.time + ThinkEvery;
            Rethink();
            TakeZombieContact();
        }

        // выбор цели: ремонт/апгрейд → захват НПЗ → слот стройки → игрок (курьер)
        Vector3 targetPos; float reach;
        if (job != null && !job.BeingMoved && job.Health > 0f) { targetPos = job.transform.position; reach = WorkRange; }
        else if (capTarget != null && !capTarget.Captured && capTarget.NearZombies == 0) { targetPos = capTarget.transform.position; reach = Refinery.Zone * 0.55f; }
        else if (hasBuild) { targetPos = buildTarget; reach = WorkRange; }
        else { courier = true; job = null; targetPos = owner.transform.position; reach = 3.2f; } // к игроку не липнем

        // движение к цели (кинематически, снап к рельефу)
        Vector3 to = targetPos - transform.position; to.y = 0f;
        float dist = to.magnitude;
        if (dist > reach)
        {
            Vector3 step = to / dist * MoveSpeed * Time.deltaTime;
            var np = transform.position + step;
            np.y = GameBootstrap.Hill(np.x, np.z);
            transform.position = np;
            Face(targetPos);
        }
        else
        {
            if (job != null) { Face(targetPos); WorkOn(job); }
            else if (capTarget != null) { /* стоим в зоне — НПЗ засчитывает пешку через AnyInZone */ }
            else if (hasBuild) { Face(targetPos); Construct(); }
            else { Face(targetPos); HaulToOwner(); }
        }
    }

    void Idle()
    {
        var p = transform.position; p.y = GameBootstrap.Hill(p.x, p.z); transform.position = p;
    }

    // Выбрать задачу: ремонт → достроить кольцо обороны → апгрейд → курьер.
    void Rethink()
    {
        Buildable repair = null, upgrade = null;
        float rSq = PickRadius * PickRadius, bestR = rSq, bestU = rSq;
        Vector3 c = transform.position;
        foreach (var b in Buildable.All)
        {
            if (b == null || b.IsTrap || b.Building || b.BeingMoved || b.Team != 0) continue;
            float d = (b.transform.position - c).sqrMagnitude;
            if (d > rSq) continue;
            if (b.NeedsRepair && d < bestR) { bestR = d; repair = b; }
            else if (b.CanUpgrade && d < bestU) { bestU = d; upgrade = b; }
        }

        hasBuild = false; capTarget = null;
        if (repair != null) { job = repair; return; }            // 1) чинить — важнее всего

        capTarget = NearestCapturable();                          // 2) захватить чистый НПЗ
        if (capTarget != null) { job = null; return; }

        if (FindBuildSlot(out Vector3 slot)) { job = null; hasBuild = true; buildTarget = slot; return; } // 3) достроить кольцо

        job = upgrade;                                            // 4) апгрейд, иначе 5) курьер (job == null)
        courier = job == null;
    }

    // Ближайший НЕзахваченный НПЗ без зомби в зоне (иначе пешка — на убой).
    Refinery NearestCapturable()
    {
        Refinery best = null;
        float bestSq = CapRadius * CapRadius;
        Vector3 c = transform.position;
        foreach (var rf in Refinery.All)
        {
            if (rf == null || rf.Captured || rf.NearZombies > 0) continue;
            float d = (rf.transform.position - c).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = rf; }
        }
        return best;
    }

    // Центр базы: сердце — критический раздатчик, иначе любой раздатчик, иначе флаг стартовой
    // базы. НЕ зависит от HasBaseSpawn (у загруженных сейвов он false, но раздатчик есть).
    bool TryBaseCenter(out Vector3 center)
    {
        foreach (var d in Dispenser.All) if (d != null && d.Critical) { center = d.transform.position; return true; }
        foreach (var d in Dispenser.All) if (d != null) { center = d.transform.position; return true; }
        if (GameBootstrap.HasBaseSpawn) { center = GameBootstrap.BaseSpawn; return true; }
        center = default; return false;
    }

    // Найти свободный слот в кольце турелей вокруг базы (умная, концентрированная стройка).
    // Строим только при наличии базы и пока турелей в кольце меньше лимита.
    bool FindBuildSlot(out Vector3 slot)
    {
        slot = default;
        if (!TryBaseCenter(out Vector3 bc)) return false;        // нет базы (нет раздатчика) — не строим
        if (Time.time < nextBuild) return false;                 // не спамим стройкой

        // глобальный лимит: сколько турелей уже стоит рядом с базой
        int sentriesNearBase = 0;
        foreach (var b in Buildable.All)
            if (b != null && b.Type == SentryType && b.Team == 0 &&
                (b.transform.position - bc).sqrMagnitude < (RingRadius + SlotFree) * (RingRadius + SlotFree))
                sentriesNearBase++;
        if (sentriesNearBase >= RingSlots) return false;

        // ищем первый пустой слот кольца
        for (int i = 0; i < RingSlots; i++)
        {
            float a = i * Mathf.PI * 2f / RingSlots;
            Vector3 s = bc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RingRadius;
            s.y = GameBootstrap.Hill(s.x, s.z);
            if (!SlotOccupied(s)) { slot = s; return true; }
        }
        return false;
    }

    static bool SlotOccupied(Vector3 s)
    {
        float rSq = SlotFree * SlotFree;
        foreach (var b in Buildable.All)
            if (b != null && b.Team == 0 && (b.transform.position - s).sqrMagnitude < rSq) return true;
        return false;
    }

    // Достроить турель в выбранном слоте (если он всё ещё пуст).
    void Construct()
    {
        hasBuild = false;
        nextBuild = Time.time + 2.5f; // пауза, чтобы отряд не лепил всё разом
        if (SlotOccupied(buildTarget)) return;                   // кто-то успел раньше
        Buildable.Create(SentryType, buildTarget, Quaternion.identity, owner);
        Effects.Upgrade(buildTarget + Vector3.up * 0.7f);
    }

    void WorkOn(Buildable b)
    {
        if (b.NeedsRepair) { b.Repair(RepairRate * ThinkEvery); if (tool != null) tool.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 20f) * 40f, 0f, 0f); }
        else if (b.CanUpgrade) b.Invest(InvestChunk); // Invest сам гейтит по своему кулдауну
        else job = null; // готово — переоценим на следующем тике
    }

    void HaulToOwner()
    {
        if (Time.time < nextHaul) return;
        nextHaul = Time.time + HaulInterval;
        owner.AddMetal(HaulMetal);
        owner.AddOil(HaulOil);
        Effects.Burst(transform.position + Vector3.up * 1f, new Color(0.4f, 0.9f, 1f), 12); // «доставил»
    }

    // Рядом зомби? — работягу калечат. Гибнет при HP<=0.
    void TakeZombieContact()
    {
        Vector3 c = transform.position;
        float rSq = 1.7f * 1.7f;
        bool touched = false;
        foreach (var z in Zombie.All)
            if (z != null && z.team < 0 && (z.transform.position - c).sqrMagnitude < rSq) { touched = true; break; }
        if (touched && Time.time >= nextHurt)
        {
            nextHurt = Time.time + 0.5f;
            health -= 8f; // ~16 HP/с под когтями
            if (health <= 0f)
            {
                Effects.Burst(c + Vector3.up * 1f, new Color(0.9f, 0.3f, 0.2f), 18);
                Destroy(gameObject);
            }
        }
    }

    void Face(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 10f * Time.deltaTime);
    }
}
