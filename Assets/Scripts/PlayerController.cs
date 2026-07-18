using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// First-person player with three tools (1=Gun, 2=Build, 3=Wrench), a build PDA
/// viewmodel, a polished HUD, and a 3-line info panel when you aim at a building.
/// </summary>
public class PlayerController : MonoBehaviour
{
    public float MoveSpeed = 7f;
    public float JumpSpeed = 6f;
    public float Gravity = 18f;
    public float MouseSensitivity = 2.2f;
    public float MaxHealth = 300f;   // 3.1.1: raised (200→300) — late-game survivability
    public float RespawnDelay = 3f;

    // Hardcore caps the wallet lower and makes builds pricier. After wave 12 the cap grows
    // each wave (+30/wave) so late-game super-weapons stay affordable.
    // 2.2 economy: you keep a big metal stockpile (base 2000, +2500 per captured point),
    // so the cap is high enough to hold it. Hardcore is a bit tighter.
    // Base 600; from wave 20 on the cap grows (+45/wave) so late-game metal income has somewhere to go.
    public static int MetalMax
    {
        get
        {
            int cap = 600;
            var gm = GameManager.Instance;
            // Grows every wave from 15 on (+65/wave) so it reaches 2500+ by wave ~45 — late-game
            // metal income has somewhere to pool for drone / Big-FPV-drun batteries.
            if (gm != null && gm.WaveNumber > 15) cap += (gm.WaveNumber - 15) * 65;
            return cap;
        }
    }
    public const int CaptureMetalBonus = 677; // metal granted when you capture a refinery/mine
    // 2.3: "нефтяной карман" building raises your personal oil capacity by 365 each.
    public static int ExtraOilCap = 0;
    public static bool AutoBhop; // settings toggle: HOLDING space auto-hops (default off = standard jumps)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ExtraOilCap = 0; GodMode = false; AutoBhop = PlayerPrefs.GetInt("bhop_auto", 0) == 1; }
    const int ReserveLoadChunk = 100; // metal loaded into a special weapon's reserve per E press
    const int OilFundChunk = 50;      // oil poured into a super-weapon's funding per E press

    // Build cost, marked up in hardcore.
    static int BCost(int i) => GameRoot.Hardcore ? Mathf.RoundToInt(BuildCosts[i] * 1.5f) : BuildCosts[i];

    [HideInInspector] public float Health;
    [HideInInspector] public int Metal = 700; // base stockpile (2.2 economy) — enough to start
    // Personal oil carry capacity: base 500, grows after wave 12 so late-game super-weapons
    // (which run on oil) stay feasible.
    public static int OilMax
    {
        get
        {
            int cap = 500 + ExtraOilCap;   // +365 per built "нефтяной карман"
            var gm = GameManager.Instance;
            if (gm != null && gm.WaveNumber > 12) cap += (gm.WaveNumber - 12) * 50;
            return cap;
        }
    }
    [HideInInspector] public int Oil = 500;        // oil carried, poured into super-weapons (start with a base stock)
    [HideInInspector] public int Score = 0;
    [HideInInspector] public int Deaths = 0; // how many times the player has died (HUD counter)
    [HideInInspector] public int SelectedBuild = 0;
    [HideInInspector] public bool Disarmed; // evac cutscene: no weapons, just run

    public bool IsDead => Health <= 0f;

    CharacterController cc;
    public static bool NoClip = false;   // ноклип-полёт сквозь стены (тоггл на V, песочница)
    Camera cam;
    float pitch;
    float vSpeed;
    float coyote;     // grace time after leaving the ground when a jump still counts
    float jumpBuffer; // grace time after pressing jump before landing, so early presses still fire
    float deathTime;

    GameObject preview;
    GameObject rangeSphere;
    int previewType = -1;
    Buildable aimed;
    bool buildMenuOpen;
    bool builtSomething; // once true, the "press Q to build" prep hint stops showing
    Car vehicle; // non-null while driving a car

    enum Tool { Gun, Build, Wrench, Shovel }
    Tool tool = Tool.Gun;

    int gunTier = 0;
    int ammo;
    int lastWave = -1;
    float nextShot;
    Buildable heldBuild;      // building currently being relocated with the middle mouse button
    int buildYawStep;         // 0..3 — R rotates the pipe/conveyor drag axis in 90° steps
    bool topBuild;            // top-down logistics build mode (camera overhead, free mouse)
    Vector3 topSavedPos, topSavedRot; // FPS camera transform saved while in top-down mode
    GameObject viewmodel;
    GameObject playerBody;
    Vector3 vmBasePos;     // viewmodel rest position; recoil animates around it
    float gunRecoil;       // 0..1 recoil kick, decays each frame
    float gunHeat;         // 0..1 muzzle heat glow, decays each frame
    GameObject gunMuzzle;  // barrel tip — glows red-hot when firing

    static readonly string[] BuildNames = { "ТУРЕЛЬ", "РАЗДАТЧИК", "РАСТЯЖКА", "СТЕНА", "ДВЕРЬ", "МОСТ", "ЛЕСТНИЦА", "ФУГАС", "КОЛЮЧКА", "АВИАУДАР", "ТЕСЛА", "АРТИЛЛЕРИЯ", "МОСТ-УГОЛ", "МОСТ-Т", "МОСТ-КРЕСТ", "ЗЕНИТКА", "ДЛ. СТЕНА", "ВЫС. СТЕНА", "МАШИНА", "РПГ", "ВЕРТ. ЛЕСТНИЦА", "СТОП-ПУШКА", "ОРБ. СТАНЦИЯ", "СМОТР. БАШНЯ", "ЛЕЗВИЯ", "ФАУ-2", "ПЛАТФОРМА", "ТРУБА НЕФТИ", "ДОЗАТОР НЕФТИ", "НЕФТ. ВЫШКА", "КОНВЕЙЕР", "ЧАН РУДЫ", "БУРОВАЯ", "НЕФТ. КАРМАН", "ОГНЕМЁТ", "НЕФТ. ХАБ", "РЗК", "FPV-ДРОН", "БОЛЬШОЙ ФПВ-ДРУН", "ФАУ-1", "КВАДРО-ТУРЕЛЬ", "ПЛАЗМА-ТУРЕЛЬ", "РЕШЁТКА", "СТЕНА+КОЛЮЧКА", "СТЕНА+ТУРЕЛЬ" };
    static readonly string[] BuildNamesEN = { "TURRET", "DISPENSER", "TRIPWIRE", "WALL", "DOOR", "BRIDGE", "STAIRS", "LANDMINE", "BARBED WIRE", "AIR STRIKE", "TESLA", "ARTILLERY", "BRIDGE-CORNER", "BRIDGE-T", "BRIDGE-CROSS", "AA GUN", "LONG WALL", "TALL WALL", "CAR", "RPG", "VERT. LADDER", "FREEZE GUN", "ORB. STATION", "WATCHTOWER", "BLADES", "V-2", "PLATFORM", "OIL PIPE", "OIL DOSER", "OIL DERRICK", "CONVEYOR", "ORE VAT", "DRILL", "OIL POCKET", "FLAMETHROWER", "OIL HUB", "SAM", "FPV DRONE", "BIG FPV DRONE", "V-1", "QUAD TURRET", "PLASMA TURRET", "LATTICE FENCE", "WALL+WIRE", "WALL+TURRET" };
    static readonly int[] BuildCosts = { 90, 100, 60, 25, 40, 35, 30, 8, 10, 250, 200, 250, 40, 45, 50, 120, 45, 35, 150, 40, 30, 136, 200, 90, 450, 550, 220, 15, 150, 870, 15, 200, 820, 200, 220, 180, 250, 20, 200, 350, 380, 300, 35, 55, 115 };

    // Short "what it is / how it works" blurb per build type — shown in the Q menu on hover.
    static readonly string[] BuildDescriptions =
    {
        "Авто-турель: сама стреляет по зомби в радиусе. С ур.2 пускает ракеты по площади. Улучшай (E) — дальше/больше урона.",
        "Раздатчик: лечит и выдаёт металл стоящим рядом. С уровнем — радиус и отдача больше.",
        "Растяжка: срабатывает на провод, взрыв по площади. 2 заряда. Зомби её не атакуют.",
        "Стена: блокирует зомби. Чини ключом или строителем. Улучшай для прочности.",
        "Дверь: закрыта — стена, нажми E чтобы открыть/закрыть проход. Прочнее с уровнем.",
        "Мост: настил, по которому ходят. Перекрывай реку и ямы.",
        "Лестница/пандус: заехать или забраться наверх.",
        "Фугас-лепёшка: лежит на земле, наступил зомби — взрыв. Зомби его не атакуют.",
        "Колючая проволока: сильно замедляет зомби, идущих сквозь неё.",
        "Авиаудар (супероружие): работает на НЕФТИ — залей нефть с НПЗ вручную (E) (трубой НЕ заправляется), затем вызывает удары по толпе на всю карту. КОМПЬЮТЕР НАВЕДЕНИЯ: посмотри на землю и нажми G — авиаудар будет бить по указанному сектору. Металл не нужен.",
        "Катушка Тесла (супероружие): работает на НЕФТИ — залей нефть с НПЗ (E) ИЛИ подведи к ней ТРУБУ НЕФТИ — заправит сама. Бьёт молнией по ближним зомби, тратит нефть из резерва.",
        "Артиллерия (супероружие): работает на НЕФТИ — залей нефть с НПЗ (E) ИЛИ подведи к ней ТРУБУ НЕФТИ — заправит сама. Фугасы по площади на всю карту, наводится на цель.",
        "Угловой мост (Г): поворот настила.",
        "Т-мост: развилка настила.",
        "Крест-мост: перекрёсток настила.",
        "Зенитка (ПВО): сбивает гранаты и птиц-десантников, 50% за каждую зенитку. Несколько ПВО — выше шанс.",
        "Длинная стена: шире обычной, перекрывает больше за раз.",
        "Высокая стена: выше — зомби не перелезут и не достанут сверху.",
        "Машина: подойди и нажми E чтобы сесть, рули WASD, F — выйти. Зомби её не трогают.",
        "РПГ: дешёвая ракетная турель. Сама бьёт ракетами по площади — хороша против толпы, но хрупкая и медленно перезаряжается.",
        "Вертикальная лестница: встань вплотную и лезь вверх/вниз на W/S. Заберись на стены и мосты. Пробел — спрыгнуть.",
        "Стоп-пушка: раз в ~16с пускает волну, замораживающую ВСЕХ зомби на карте на 10 секунд. Дёшево, без расхода металла.",
        "Орбитальная станция: блок управления, работает на НЕФТИ — залей нефть с НПЗ (E) ИЛИ подведи к ней ТРУБУ НЕФТИ — заправит сама, металл не нужен. Когда готов — в небе появляется станция и циклит 3 атаки: точные лазеры со взрывом, выжигающий луч (ползёт от зомби к зомби) и тройная призма (3 луча крутятся вокруг базы). Тратит металл из своего бака — заряжай E.",
        "Смотровая башня (20 м): залезь по лестнице через люк на площадку наверху — отличная точка для стрельбы, зомби туда не достанут.",
        "Лезвия: крутящийся ротор рубит всех зомби рядом несколько раз в секунду. Работает как турель — сама, без зарядки и расхода металла. Дорогая в постройке.",
        "ФАУ-2: огромная баллистическая ракета, стоящая ВЕРТИКАЛЬНО на пусковой стойке — мощная «супер»-турель ДАЛЬНЕГО действия, работает САМА, без нефти и металла. Бьёт по толпам (3+ зомби) на дистанции до ~2000 м через всю карту: ракета стартует со стойки, идёт по высокой баллистической дуге, переворачивается и падает на цель, а ЖИРНЫЙ сплеш сносит всех вокруг (10-16). Ракеты не бьют в одного зомби дважды. Дорогая в постройке.",
        "Платформа: огромная площадка на 4 толстых столбах. Залезь по лестнице наверх — целый этаж под турели и линию обороны, зомби туда не достанут.",
        "Труба нефти: зажми ЛКМ у захваченного НПЗ и веди к базе — отпустишь, и труба ляжет цепочкой (15 мет./звено). Тянет нефть к дозатору. Зомби её ломают — защищай.",
        "Дозатор нефти: качает нефть из подключённого НПЗ (через трубы) и сам выдаёт её тебе, когда стоишь рядом. Поставь у базы — нефть течёт без беготни.",
        "Нефтяная вышка: своя нефтяная скважина (870 мет.) — не нужно захватывать НПЗ. Качает нефть в свой бак; подключи к ней трубу и веди к дозатору.",
        "Конвейер: зажми ЛКМ у захваченной ШАХТЫ и веди к базе — ляжет цепочкой (15 мет./звено). Возит руду к чану. Зомби ломают — защищай.",
        "Чан для руды: качает руду из ВСЕХ подключённых шахт/буровых (через конвейеры) и сам выдаёт металл тебе рядом. Больше источников на сети — больше металла в секунду.",
        "Буровая: своя установка добычи металла (820 мет.) — не нужно захватывать шахту. Бурит руду в свой бак; подключи к ней конвейер и веди к чану.",
        "Нефтяной карман: доп. бак-хранилище — пока стоит, поднимает твой МАКС. запас нефти на +365. Ставь несколько, чтобы копить больше нефти под супер-пушки. Сломают/продашь — прибавка пропадёт.",
        "Огнемёт: стационарный, поливает коротким конусом огня. ОГРОМНЫЙ урон, но очень малая дальность — жарит всех вблизи, вдали бесполезен. Работает сам, без расхода. Улучшай (E) — урон и радиус.",
        "Нефтяной хаб: узел-сумматор И раздатчик. Протяни к нему трубы от НЕСКОЛЬКИХ НПЗ/вышек — он качает нефть со ВСЕХ сразу и сам выдаёт её тебе рядом. Чем больше труб подключено — тем БЫСТРЕЕ наливает (нефтяной аналог чана для руды).",
        "РЗК: дальнобойная зенитно-ракетная установка на 4 тубуса. Даёт ЗАЛП 4 самонаводящихся ракет по птицам-десантникам — каждая гарантированно сбивает (ЗЕНИТКА бьёт лишь с шансом и вблизи). С 24 волны сбивает вражеские бомбардировщики — сбитый самолёт взрывается прямо в воздухе (никаких падений на базу). Улучшай (E) — дальность и скорострельность.",
        "Дон дрон с дрыном дон, в щещню залетел дон! FPV-дрон (он же друн): дешёвая площадка (20 мет.) — сама запускает друна-камикадзе в ближайшего зомби: взлетает, таранит цель и взрывается сплешем. Работает сам, без нефти и металла — только перезарядка. БЛИЖНИЙ радиус — держит оборону у базы (для дальнобойного бери «Большой ФПВ-друн»). Ставь пачками для роя. Улучшай (E) — быстрее, шире радиус, больше урона. рамзан ахматович одобряет.",
        "БОЛЬШОЙ ФПВ-ДРУН: тяжёлый барражирующий боеприпас с дельта-крылом (200 мет.) — лейтгейм-мопед для тотального ада. Взлетает с площадки, выходит на крейсер ~12 м над землёй, идёт к ближайшему зомби через полкарты и ПИКИРУЕТ в него мощным фугасом. Дёшев для своей мощи — воткни БАТАРЕЮ из 5-6 рамп на полный карман и ковровый удар по орде обеспечен. Работает сам, без нефти. Бьёт по РАЗНЫМ зомби (резерв целей). Улучшай (E) — дальность до ~2000 м, радиус взрыва до 8 и урон до 620.",
        "ФАУ-1: самый большой «друн» — крылатая бомба «Фау-1» на ОГРОМНОЙ наклонной рампе (350 мет.). Бомба идёт РОВНЫМ КРЕЙСЕРОМ ~10 м над землёй через полкарты в дальнего зомби и ныряет в него ГИГАНТСКИМ фугасом (радиус до 32, урон до 4200 — выносит толпу целиком). Дальнобойный (до 2000 м) и самый мощный, но дорогой и с долгой перезарядкой. Бьёт по РАЗНЫМ зомби (резерв целей). Работает сам, без нефти. Улучшай (E) — дальность, радиус взрыва и урон растут.",
        "КВАДРО-ТУРЕЛЬ: тяжёлая турель на ЧЕТЫРЕ ствола (380 мет.) — топовая автопушка ближней/средней обороны. Даёт БЫСТРЫЙ ЗАЛП из 4 стволов, распределяя огонь по ЧЕТЫРЁМ разным зомби сразу — рвёт толпу, а не одну цель. Толстая броня (до 640 ХП) и дальность до 40 м. С ур.2 плюсом ЛУПИТ ракетами по площади. Работает сама, без нефти и патронов. Улучшай (E) — урон, скорострельность, дальность и ракеты.",
        "ПЛАЗМА-ТУРЕЛЬ (ДЛС «Не далёкое будущее»): повстанческий плазма-тех. Стреляет быстрым ПРОБИВАЮЩИМ разрядом — цианный луч прошивает ЦЕЛУЮ ЛИНИЮ врагов до дальности (а не одну цель), поэтому топ против колонн в коридорах. Работает сама, без нефти/патронов. Улучшай (E) — урон, скорострельность, дальность и ширина пробоя.",
        "РЕШЁТКА: электрифицированный решётчатый забор-ловушка. Как колючка — зомби идут СКВОЗЬ (не бьют его), но металлическая сетка бьёт их током: постоянный урон + замедление всем в полосе, плюс раз в ~1с — усиленный РАЗРЯД. Прочнее и длиннее колючки — ставь стенкой поперёк проходов. Работает сама, без нефти. Улучшай (E) — урон тока, разряд, замедление и прочность.",
        "СТЕНА+КОЛЮЧКА: длинная стена + полоса колючей проволоки в 2 метрах ПЕРЕД ней. Стена держит натиск (HP как у длинной стены), а колючка тормозит и медленно режет зомби ещё на подходе — они увязают перед стеной под твоим огнём. Одна постройка вместо двух, за одну цену. Улучшай (E) — HP стены + урон/замедление колючки.",
        "СТЕНА+ТУРЕЛЬ: обычная стена со встроенной автотурелью. Стена блокирует и держит удар (толстый HP), а турель сама лупит по ближайшим зомби. Мгновенная укреплённая огневая точка — одна постройка вместо стены и турели по отдельности. Улучшай (E) — HP + урон/скорострельность турели.",
    };

    // English translations of BuildDescriptions, in the SAME order (used when Lang.EN).
    static readonly string[] BuildDescriptionsEN =
    {
        "Auto-turret: fires at zombies within range on its own. From lvl 2 it launches area rockets. Upgrade (E) — more range/damage.",
        "Dispenser: heals and gives metal to those standing nearby. Higher level — bigger radius and output.",
        "Tripwire: triggers on the wire, area explosion. 2 charges. Zombies don't attack it.",
        "Wall: blocks zombies. Repair with the wrench or builder. Upgrade for durability.",
        "Door: closed it's a wall, press E to open/close the passage. Tougher with each level.",
        "Bridge: a walkway to cross. Span rivers and pits.",
        "Stairs/ramp: drive or climb up.",
        "Landmine: lies on the ground, a zombie steps on it — explosion. Zombies don't attack it.",
        "Barbed wire: heavily slows zombies walking through it.",
        "Air strike (super-weapon): runs on OIL — pour in oil from a refinery by hand (E) (NOT fuelled by pipes), then it calls strikes on the crowd across the whole map. TARGETING COMPUTER: look at the ground and press G — the air strike will hit the marked sector. No metal needed.",
        "Tesla coil (super-weapon): runs on OIL — pour in oil from a refinery (E) OR run an OIL PIPE to it — it fuels itself. Zaps nearby zombies with lightning, spends oil from its reserve.",
        "Artillery (super-weapon): runs on OIL — pour in oil from a refinery (E) OR run an OIL PIPE to it — it fuels itself. Area shells across the whole map, aims at a target.",
        "Corner bridge (L-shape): a turn in the walkway.",
        "T-bridge: a walkway junction.",
        "Cross bridge: a walkway crossroads.",
        "AA gun (air defense): shoots down grenades and paratrooper birds, 50% per AA gun. More AA — higher chance.",
        "Long wall: wider than a normal one, covers more at once.",
        "Tall wall: taller — zombies can't climb over or reach across the top.",
        "Car: walk up and press E to get in, steer with WASD, F to get out. Zombies leave it alone.",
        "RPG: a cheap rocket turret. Fires area rockets on its own — good against crowds, but fragile and slow to reload.",
        "Vertical ladder: stand up close and climb up/down with W/S. Get onto walls and bridges. Space to hop off.",
        "Freeze gun: every ~16s it emits a wave that freezes ALL zombies on the map for 10 seconds. Cheap, spends no metal.",
        "Orbital station: a control block, runs on OIL — pour in oil from a refinery (E) OR run an OIL PIPE to it — it fuels itself, no metal needed. When ready a station appears in the sky and cycles 3 attacks: precise lasers with a blast, a burning beam (creeps from zombie to zombie) and a triple prism (3 beams spin around the base). Spends metal from its tank — recharge with E.",
        "Watchtower (20 m): climb the ladder through the hatch to the platform up top — a great spot to shoot from, zombies can't reach it.",
        "Blades: a spinning rotor slices every zombie nearby several times a second. Works like a turret — on its own, no charging or metal cost. Expensive to build.",
        "V-2: a huge ballistic rocket standing VERTICALLY on a launch stand — a powerful LONG-RANGE 'super' turret that works ON ITS OWN, no oil or metal. Hits crowds (3+ zombies) up to ~2000 m away across the map: the rocket lifts off the stand, flies a high ballistic arc, flips over and drops onto the target, and a FAT splash wipes out everyone around (10-16). Missiles never hit the same zombie twice. Expensive to build.",
        "Platform: a huge deck on 4 thick pillars. Climb the ladder up top — a whole floor for turrets and a defensive line, zombies can't reach it.",
        "Oil pipe: hold LMB at a captured refinery and lead it to the base — release and the pipe lays as a chain (15 metal/link). Carries oil to the doser. Zombies break it — protect it.",
        "Oil doser: pumps oil from a connected refinery (through pipes) and hands it to you when you stand nearby. Place it by the base — oil flows without the running around.",
        "Oil derrick: your own oil well (870 metal) — no need to capture a refinery. Pumps oil into its tank; connect a pipe to it and lead it to the doser.",
        "Conveyor: hold LMB at a captured MINE and lead it to the base — lays as a chain (15 metal/link). Hauls ore to the vat. Zombies break it — protect it.",
        "Ore vat: pulls ore from ALL connected mines/drills (through conveyors) and hands metal to you nearby. More sources on the network — more metal per second.",
        "Drill: your own metal-mining rig (820 metal) — no need to capture a mine. Drills ore into its tank; connect a conveyor to it and lead it to the vat.",
        "Oil pocket: an extra storage tank — while it stands it raises your MAX oil reserve by +365. Place several to stockpile more oil for super-weapons. If it's destroyed/sold, the bonus is gone.",
        "Flamethrower: stationary, sprays a short cone of fire. HUGE damage, but very short range — roasts everyone close, useless at a distance. Works on its own, no cost. Upgrade (E) — damage and radius.",
        "Oil hub: a combiner node AND dispenser. Run pipes to it from SEVERAL refineries/derricks — it pumps oil from ALL of them at once and hands it to you nearby. The more pipes connected — the FASTER it fills (the oil equivalent of the ore vat).",
        "SAM: a long-range surface-to-air missile launcher with 4 tubes. Fires a SALVO of 4 homing missiles at paratrooper birds — each one is a guaranteed kill (the AA gun only hits by chance and up close). From wave 24 it downs enemy bombers — a downed plane blows up in mid-air (no crashing onto the base). Upgrade (E) — range and fire rate.",
        "Don drone with a dryn, don! FPV drone (aka 'drun'): a cheap pad (20 metal) — launches a kamikaze drone at the nearest zombie on its own. The drone lifts off, rams the target and detonates in a small blast. Works by itself, no oil or metal — just a reload. SHORT range — holds the line near the base (for long range take the 'Big FPV drone'). Place several for a swarm. Upgrade (E) — faster, wider radius, more damage. ramzan akhmatovich approves.",
        "BIG FPV DRONE: a heavy delta-wing loitering munition (200 metal) — a late-game piece for total mayhem. Lifts off the pad, climbs to a cruise ~12 m above the ground, flies across half the map to the nearest zombie and DIVES into it with a big blast. Cheap for its power — field a BATTERY of 5-6 pads on a full wallet and carpet-bomb the horde. Works on its own, no oil. Targets DIFFERENT zombies (shared reservation). Upgrade (E) — range up to ~2000 m, blast up to 8 and damage up to 620.",
        "V-1: the biggest 'drone' — a V-1 flying bomb on a HUGE inclined launch ramp (350 metal). The bomb cruises in LEVEL ~10 m over the ground across half the map into a distant zombie and dives into it with a GIGANTIC blast (radius up to 32, damage up to 4200 — wipes a whole crowd). Long-range (up to 2000 m) and the most powerful, but expensive with a long reload. Targets DIFFERENT zombies (shared reservation). Works on its own, no oil. Upgrade (E) — range, blast radius and damage all grow.",
        "QUAD TURRET: a heavy FOUR-barrel turret (380 metal) — the top close/mid-range auto-cannon. Fires a FAST 4-barrel BURST, splitting fire across FOUR different zombies at once — it shreds a crowd, not just one target. Thick armour (up to 640 HP) and range up to 40 m. From lvl 2 it also LOBS area rockets. Works on its own, no oil or ammo. Upgrade (E) — damage, fire rate, range and rockets.",
        "PLASMA TURRET (DLC 'Near Future'): captured rebel plasma-tech. Fires a fast PIERCING plasma bolt — a cyan beam rips through a WHOLE LINE of enemies up to range (not just one target), so it's top against columns in corridors. Works on its own, no oil or ammo. Upgrade (E) — damage, fire rate, range and beam width.",
        "LATTICE FENCE: an electrified grid-fence trap. Like barbed wire, zombies walk THROUGH it (they don't attack it), but the metal mesh shocks them: constant damage + slow to everyone in the strip, plus a stronger ZAP every ~1s. Tougher and longer than barbed wire — line it across corridors. Works on its own, no oil. Upgrade (E) — shock damage, zap, slow and durability.",
        "WALL+WIRE: a long wall plus a strip of barbed wire 2 m in FRONT of it. The wall holds the line (long-wall HP), while the wire slows and slowly shreds zombies on the approach — they bog down in front of the wall under your fire. One build instead of two, for one price. Upgrade (E) — wall HP + wire damage/slow.",
        "WALL+TURRET: a normal wall with a built-in auto-turret. The wall blocks and tanks hits (thick HP), while the turret auto-fires at nearby zombies. An instant fortified firing point — one build instead of a separate wall and turret. Upgrade (E) — HP + turret damage/fire rate.",
    };

    // Build-menu sections: each holds the build-type indices shown under that header.
    static readonly string[] BuildCategories = { "СТРОЙКА", "ДОБЫЧА", "ЛОГИСТИКА", "ТУРЕЛИ", "ПВО", "ЛОВУШКИ", "ТЯЖЁЛОЕ" };
    static readonly string[] BuildCategoriesEN = { "CONSTRUCTION", "EXTRACTION", "LOGISTICS", "TURRETS", "AIR DEFENSE", "TRAPS", "HEAVY" };

    // Language accessors for the static build arrays — chosen at USE time so switching
    // languages at runtime works (wrapping the array elements with Lang.T would freeze
    // the language at static-init time).
    // The Dispenser (index 1) is the BASE lifeline — the game spawns it, the player never builds it
    // (in endless mode it's RELOCATED with СКМ, not freshly built). Guard so no hotkey/menu can place one.
    static bool IsPlayerBuildable(int type) => type != 1;

    static string BName(int i) => Lang.EN ? BuildNamesEN[i] : BuildNames[i];
    static string BDesc(int i) => Lang.EN ? BuildDescriptionsEN[i] : BuildDescriptions[i];
    static string BCat(int i) => Lang.EN ? BuildCategoriesEN[i] : BuildCategories[i];
    static readonly int[][] BuildCategoryItems =
    {
        new[] { 3, 16, 17, 4, 6, 20, 23, 26, 5, 43 }, // СТРОЙКА: WALL, LONG/TALL WALL, DOOR, STAIRS, LADDER, WATCHTOWER, BIG PLATFORM, BRIDGE, WALL+WIRE
        new[] { 29, 32 },                         // ДОБЫЧА: OIL DERRICK, METAL DRILL
        new[] { 27, 28, 35, 30, 31 },             // ЛОГИСТИКА: OIL PIPE, OIL DOSER, OIL HUB, CONVEYOR, METAL VAT
        new[] { 0, 40, 41, 19, 37, 24, 34, 10, 21, 44 },  // ТУРЕЛИ: SENTRY, QUAD TURRET, PLASMA TURRET(ДЛС), RPG, FPV DRONE, BLADES, FLAMETHROWER, TESLA, FREEZE, WALL+TURRET
        new[] { 15, 36 },                         // ПВО: AA TURRET (ЗЕНИТКА), SAM (РЗК)
        new[] { 2, 7, 8, 42 },                    // ЛОВУШКИ: MINE, LANDMINE, BARBED WIRE, LATTICE FENCE(решётка)
        new[] { 25, 39, 38, 9, 11, 22, 18 },      // ТЯЖЁЛОЕ: MISSILE SILO, V-1, GERAN-2, AIR STRIKE, ARTILLERY, ORBITAL, CAR
    };

    struct GunStats { public string name; public float dmg; public float rate; public int mag; }
    static readonly GunStats[] Guns =
    {
        new GunStats { name = "ПИСТОЛЕТ", dmg = 22f,  rate = 0.26f, mag = 12 },
        new GunStats { name = "ПП",       dmg = 16f,  rate = 0.06f, mag = 30 },
        new GunStats { name = "ВИНТОВКА", dmg = 34f,  rate = 0.10f, mag = 25 },
        new GunStats { name = "КАРАБИН",  dmg = 46f,  rate = 0.08f, mag = 30 },
        new GunStats { name = "ПУЛЕМЁТ",  dmg = 30f,  rate = 0.05f, mag = 60 },
        new GunStats { name = "РЕЛЬСОТРОН", dmg = 120f, rate = 0.38f, mag = 10 },
    };

    void Awake()
    {
        MaxHealth *= ModRuntime.PlayerHpMult;   // 3.2: mod multipliers
        MoveSpeed *= ModRuntime.PlayerSpeedMult;
        Health = MaxHealth;
        Metal = Mathf.Min(Metal, MetalMax); // respect the (lower) hardcore cap from the start
        ammo = Guns[0].mag;

        cc = gameObject.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = Vector3.zero;

        playerBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerBody.transform.SetParent(transform, false);
        playerBody.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        Destroy(playerBody.GetComponent<Collider>());
        GameBootstrap.SetColor(playerBody, new Color(0.3f, 0.5f, 0.9f));

        var camGO = new GameObject("PlayerCamera");
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(transform, false);
        camGO.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        cam = camGO.AddComponent<Camera>();
        camGO.AddComponent<AudioListener>();
        if (GameBootstrap.Night) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.04f, 0.08f); } // dark night sky
        VisualFx.EnablePostFx(cam); // bloom / tonemapping / grading from the global volume

        BuildViewmodel();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!GameRoot.IsPlaying) return; // frozen in menu / pause

        if (GameRoot.Sandbox) { Metal = 999999; Oil = 999999; } // sandbox: bottomless wallet
        if (GameRoot.ModTest) HandleModTest(); // 3.2: hotkeys to reload/fire node-mod events

        SyncGunToWave();

        if (GameRoot.BaseLost) // base lifeline destroyed → game over, free the cursor for the screen
        {
            if (vehicle != null) ExitVehicle();
            if (preview != null) preview.SetActive(false);
            SetAimed(null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (IsDead)
        {
            if (vehicle != null) ExitVehicle(); // eject if killed while driving
            if (preview != null) preview.SetActive(false);
            SetAimed(null);
            Cursor.lockState = CursorLockMode.None; // free the cursor for the death screen
            Cursor.visible = true;
            return; // wait for the player to pick Респавн / Выйти
        }

        // Driving a car takes over input/camera until you get out.
        if (vehicle != null) { DriveVehicle(); return; }

        // Hold Q for the build menu (GMod spawn-menu style): open while held, close on release.
        bool qHeld = Input.GetKey(KeyCode.Q);
        if (qHeld != buildMenuOpen)
        {
            buildMenuOpen = qHeld;
            Cursor.lockState = (buildMenuOpen || topBuild) ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = buildMenuOpen || topBuild;
            if (buildMenuOpen) layingDrag = false; // cancel any in-progress pipe drag
        }
        if (buildMenuOpen)
        {
            if (preview != null) preview.SetActive(false);
            if (rangeSphere != null) rangeSphere.SetActive(false);
            SetAimed(null);
            return; // frozen while the menu is held open; clicks handled in OnGUI
        }

        // ---- Top-down logistics build mode (T): overhead camera + free mouse to lay pipes/conveyors ----
        if (topBuild && (tool != Tool.Build || IsDead || Disarmed)) SetTopBuild(false); // auto-exit if we leave building
        if (Input.GetKeyDown(KeyCode.T) && (tool == Tool.Build || topBuild)) SetTopBuild(!topBuild);
        else if (topBuild && Input.GetKeyDown(KeyCode.Escape)) SetTopBuild(false);

        if (topBuild) TopBuildPan(); // WASD pans the overhead view; mouse-look frozen
        else { Look(); Move(); }

        // Disarmed (evac cutscene): keep mouselook + movement, but no weapons/tools/HUD targeting.
        if (Disarmed)
        {
            if (viewmodel != null && viewmodel.activeSelf) viewmodel.SetActive(false);
            SetAimed(null);
            return;
        }

        SetAimed(RaycastNoSelf(30f, out RaycastHit aimHit) ? aimHit.collider.GetComponentInParent<Buildable>() : null);

        // Air-strike targeting computer: aim at the ground and press G to designate that sector —
        // any online air strike then pounds it for a while. Only usable if you have one built.
        if (Input.GetKeyDown(KeyCode.G) && AirStrike.AnyOnline())
        {
            if (RaycastNoSelf(300f, out RaycastHit gh))
            {
                AirStrike.Designate(gh.point);
                Effects.Burst(gh.point + Vector3.up * 0.2f, new Color(1f, 0.35f, 0.2f), 10);
            }
        }

        // Mouse wheel switches weapon/tool (classic FPS feel).
        float sw = Input.GetAxis("Mouse ScrollWheel");
        if (sw > 0.01f) CycleTool(1);
        else if (sw < -0.01f) CycleTool(-1);

        // Number keys 1-9 pick a building type and switch to the build tool. The
        // special weapons (10+) live past the number row, so they're picked from the Q menu.
        int hotkeys = Mathf.Min(BuildNames.Length, 9);
        for (int k = 0; k < hotkeys; k++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + k) && IsPlayerBuildable(k)) { SelectedBuild = k; SetTool(Tool.Build); }

        // Middle mouse button (СКМ) = pick up / relocate a building. First press grabs the one
        // you're looking at; while held it follows your crosshair; press again to drop it there.
        // R rotates the carried building 90° (e.g. to face a conveyor the other way).
        if (Input.GetMouseButtonDown(2)) MoveBuildAction();
        if (heldBuild != null)
        {
            if (Input.GetKeyDown(KeyCode.R)) heldBuild.transform.Rotate(0f, 90f, 0f);
            FollowHeldBuild();
        }
        // In the build tool, R rotates the pipe/conveyor drag axis by 90° (straight-line laying).
        else if (tool == Tool.Build && Input.GetKeyDown(KeyCode.R))
            buildYawStep = (buildYawStep + 1) & 3;

        switch (tool)
        {
            case Tool.Gun:
                if (Input.GetMouseButton(0)) FireGun();
                break;
            case Tool.Build:
                HandleBuildInput();
                if (Input.GetMouseButtonDown(1)) SellBuild();
                if (Input.GetKeyDown(KeyCode.X)) DeleteByClass(); // снести весь класс постройки, на которую смотришь
                break;
            case Tool.Wrench:
                if (Input.GetMouseButton(0)) Swing();
                break;
            case Tool.Shovel:
                if (Input.GetMouseButton(0)) Dig();
                break;
        }

        if (Input.GetKeyDown(KeyCode.E)) Interact();

        AnimateViewmodel();
        UpdatePreview();
    }

    void SyncGunToWave()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.WaveNumber == lastWave) return;
        lastWave = gm.WaveNumber;
        int tier = Mathf.Clamp(gm.WaveNumber, 0, Guns.Length - 1);
        if (tier > gunTier) gunTier = tier;
        ammo = Guns[gunTier].mag;
        if (tool == Tool.Gun) BuildViewmodel();
    }

    // Track the aimed-at building and flag it as Hovered each frame so it (and only
    // it) shows its floating LVL/health label. Buildable clears Hovered every frame,
    // so the label disappears the instant we stop aiming here — nothing gets stuck.
    void SetAimed(Buildable b)
    {
        aimed = b;
        if (b != null) b.Hovered = true;
    }

    void SetTool(Tool t) { tool = t; BuildViewmodel(); }

    void CycleTool(int dir)
    {
        const int n = 4; // Gun, Build, Wrench, Shovel
        SetTool((Tool)(((int)tool + dir + n) % n));
    }


    // WebGL pointer-lock reports much larger mouse deltas than standalone, so the camera spins
    // wildly. Scale it down in the browser to match the Unity/desktop feel.
    static readonly bool IsWebGL = Application.platform == RuntimePlatform.WebGLPlayer;
    const float WebMouseScale = 0.18f;

    void Look()
    {
        float sens = MouseSensitivity * (IsWebGL ? WebMouseScale : 1f);
        float mx = Input.GetAxis("Mouse X") * sens;
        float my = Input.GetAxis("Mouse Y") * sens;
        transform.Rotate(0f, mx, 0f);
        pitch = Mathf.Clamp(pitch - my, -85f, 85f);
        cam.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void Move()
    {
        // НОКЛИП (V): полёт сквозь стены — ТОЛЬКО в песочнице. В обычной игре читерить нельзя.
        // Если ноклип как-то остался включён вне песочницы — принудительно выключаем. 2026-07-14.
        if (NoClip && !GameRoot.Sandbox) { NoClip = false; cc.enabled = true; }
        if (Input.GetKeyDown(KeyCode.V))
        {
            if (!GameRoot.Sandbox)
            {
                Toast(Lang.T("Ноклип только в песочнице", "Noclip is sandbox-only"));
            }
            else
            {
                NoClip = !NoClip;
                cc.enabled = !NoClip;   // коллайдер off = проходишь стены; on = обычная физика
                Toast(Lang.T(NoClip ? "🛸 Ноклип ВКЛ — полёт сквозь стены (W/S лететь, Space вверх, Ctrl вниз, Shift быстрее)" : "Ноклип выкл",
                             NoClip ? "🛸 Noclip ON — fly through walls (W/S move, Space up, Ctrl down, Shift faster)" : "Noclip OFF"));
            }
        }
        if (NoClip)
        {
            float nh = Input.GetAxisRaw("Horizontal");
            float nv = Input.GetAxisRaw("Vertical");
            float fly = MoveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 3.5f : 1.6f);
            Vector3 dir = cam.transform.forward * nv + cam.transform.right * nh; // летим КУДА СМОТРИМ
            if (Input.GetKey(KeyCode.Space)) dir += Vector3.up;                   // вверх
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) dir += Vector3.down; // вниз
            if (dir.sqrMagnitude > 1e-4f) transform.position += dir.normalized * fly * Time.deltaTime;
            vSpeed = 0f;
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        bool crouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        bool sprint = Input.GetKey(KeyCode.LeftShift) && !crouch;
        float speed = MoveSpeed * (crouch ? 0.5f : (sprint ? 1.7f : 1f));

        // Crouch: shrink the capsule (feet stay planted) and lower the camera.
        float targetH = crouch ? 1.0f : 1.8f;
        cc.height = Mathf.Lerp(cc.height, targetH, 12f * Time.deltaTime);
        cc.center = new Vector3(0f, cc.height * 0.5f - 0.9f, 0f);
        var clp = cam.transform.localPosition;
        clp.y = Mathf.Lerp(clp.y, crouch ? 0.3f : 0.7f, 12f * Time.deltaTime);
        cam.transform.localPosition = clp;

        // Ladder climbing: while standing in a ladder's climb zone, move straight
        // up/down with W/S (gravity off, hangs in place when idle). A/D still strafes
        // so you can step off sideways; Space hops off (falls through to a jump).
        bool onLadder = NearClimb();
        // Engage only when climbing up (W) or already off the ground mid-climb — so you
        // can still walk freely around the ladder's base instead of getting stuck on it.
        if (onLadder && (v > 0.1f || !cc.isGrounded) && !Input.GetKeyDown(KeyCode.Space))
        {
            vSpeed = 0f;
            float climb = v * MoveSpeed * 0.6f;        // W = up, S = down
            Vector3 strafe = transform.right * h;
            cc.Move((strafe * MoveSpeed * 0.5f + Vector3.up * climb) * Time.deltaTime);
            return;
        }

        // ---- BHOP movement (Source-style): momentum + friction on foot, air-strafing airborne ----
        Vector3 wishdir = transform.right * h + transform.forward * v;
        wishdir.y = 0f;
        if (wishdir.sqrMagnitude > 1e-4f) wishdir.Normalize();

        bool grounded = cc.isGrounded;
        coyote = grounded ? 0.1f : coyote - Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.Space)) jumpBuffer = 0.1f;
        else jumpBuffer -= Time.deltaTime;

        // The Settings "Бхоп" checkbox (default OFF) governs the WHOLE bhop feel: momentum/inertia
        // AND auto-hop. OFF → plain arcade movement, no speed build-up; ON → Source-style bhop.
        if (!AutoBhop)
        {
            bool jumpNow = jumpBuffer > 0f && coyote > 0f && !crouch;
            hVel = wishdir * speed;                       // instant velocity, NO inertia carried
            if (jumpNow) { vSpeed = JumpSpeed; jumpBuffer = 0f; coyote = 0f; }
            else if (grounded && vSpeed < 0f) vSpeed = -1f;
            vSpeed -= Gravity * Time.deltaTime;
            cc.Move((hVel + Vector3.up * vSpeed) * Time.deltaTime);
            return;
        }

        // Jump. Auto-hop lets HOLDING space re-hop on landing.
        bool jumpWanted = jumpBuffer > 0f || Input.GetKey(KeyCode.Space);
        bool doJump = jumpWanted && coyote > 0f && !crouch;

        if (grounded && !doJump)
        {
            GroundFriction(Time.deltaTime);
            AccelToward(wishdir, speed, GroundAccel, Time.deltaTime);
            if (vSpeed < 0f) vSpeed = -1f; // stick to the ground while settling
        }
        else
        {
            if (doJump) { vSpeed = JumpSpeed; jumpBuffer = 0f; coyote = 0f; } // launch — NO friction, speed carries
            AirAccelToward(wishdir, speed, AirAccel, AirCap, Time.deltaTime); // strafe to keep/build speed
        }

        vSpeed -= Gravity * Time.deltaTime;
        if (hVel.magnitude > MaxHVel) hVel = hVel.normalized * MaxHVel;       // sane bhop top speed
        cc.Move((hVel + Vector3.up * vSpeed) * Time.deltaTime);
    }

    // --- Source-style bhop helpers. hVel = horizontal velocity carried between frames. ---
    const float Friction = 6f, GroundAccel = 12f, AirAccel = 14f, AirCap = 1.4f, MaxHVel = 24f;
    Vector3 hVel;

    void GroundFriction(float dt)
    {
        float sp = hVel.magnitude;
        if (sp < 0.1f) { hVel = Vector3.zero; return; }
        float control = sp < 3f ? 3f : sp;                       // stopspeed = 3
        hVel *= Mathf.Max(sp - control * Friction * dt, 0f) / sp;
    }

    void AccelToward(Vector3 wishdir, float wishspeed, float accel, float dt)
    {
        float add = wishspeed - Vector3.Dot(hVel, wishdir);
        if (add <= 0f) return;
        hVel += wishdir * Mathf.Min(accel * wishspeed * dt, add);
    }

    void AirAccelToward(Vector3 wishdir, float wishspeed, float accel, float cap, float dt)
    {
        float add = Mathf.Min(wishspeed, cap) - Vector3.Dot(hVel, wishdir); // air speed cap is the strafe magic
        if (add <= 0f) return;
        hVel += wishdir * Mathf.Min(accel * wishspeed * dt, add);
    }

    // True when the player overlaps something climbable: the vertical-ladder buildable, or
    // a ClimbZone trigger (the watchtower's central ladder).
    bool NearClimb()
    {
        var hits = Physics.OverlapBox(transform.position + Vector3.up * 0.9f,
                                      new Vector3(0.45f, 0.9f, 0.45f), transform.rotation,
                                      ~0, QueryTriggerInteraction.Collide);
        foreach (var h in hits)
        {
            if (h.GetComponent<ClimbZone>() != null) // watchtower ladder column
            {
                var wb = h.GetComponentInParent<Buildable>();
                if (wb == null || (!wb.Building && !wb.IsPuppet)) return true;
            }
            var l = h.GetComponentInParent<Ladder>();
            if (l != null && !l.Building && !l.IsPuppet) return true;
        }
        return false;
    }

    // Reused by every aim/fire/interact/preview ray (called several times per frame),
    // so no fresh RaycastHit[] is allocated per cast.
    static readonly RaycastHit[] _rayHits = new RaycastHit[32];

    bool RaycastNoSelf(float dist, out RaycastHit best)
    {
        Vector3 org, dir;
        if (topBuild) { var r = cam.ScreenPointToRay(Input.mousePosition); org = r.origin; dir = r.direction; if (dist < 120f) dist = 120f; }
        else { org = cam.transform.position; dir = cam.transform.forward; }
        int n = Physics.RaycastNonAlloc(org, dir, _rayHits, dist);
        // Pick the NEAREST hit that isn't the player's own collider (single pass, no sort).
        float bestD = float.MaxValue; bool found = false; best = default;
        for (int i = 0; i < n; i++)
        {
            if (_rayHits[i].collider.GetComponentInParent<PlayerController>() == this) continue;
            if (_rayHits[i].distance < bestD) { bestD = _rayHits[i].distance; best = _rayHits[i]; found = true; }
        }
        return found;
    }

    // ---- Top-down logistics build mode: overhead camera + free mouse to lay pipes/conveyors ----
    void SetTopBuild(bool on)
    {
        if (on == topBuild) return;
        topBuild = on;
        layingDrag = false;
        if (on)
        {
            topSavedPos = cam.transform.localPosition;
            topSavedRot = cam.transform.localEulerAngles;
            cam.transform.localPosition = new Vector3(0f, 42f, 0f);     // high above the player's feet
            cam.transform.localEulerAngles = new Vector3(90f, 0f, 0f);  // straight down
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            vSpeed = 0f; hVel = Vector3.zero;
            Toast(Lang.T("Вид СВЕРХУ: мышью веди трубы/конвейеры, R — поворот, WASD — двигать вид, ПРОБЕЛ — прыжок. T/Esc — выйти",
                         "TOP view: drag pipes/conveyors with the mouse, R to rotate, WASD to pan, SPACE to jump. T/Esc to exit"));
        }
        else
        {
            cam.transform.localPosition = topSavedPos;
            cam.transform.localEulerAngles = topSavedRot;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }
    }

    // WASD pans the overhead view (walk the player around under the top-down camera). Space still jumps.
    void TopBuildPan()
    {
        float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 rgt = transform.right;   rgt.y = 0f; rgt.Normalize();
        Vector3 pan = rgt * h + fwd * v;
        if (pan.sqrMagnitude > 1f) pan.Normalize();

        // Jumping IS allowed in build mode — full gravity + Space to hop.
        bool grounded = cc.isGrounded;
        if (grounded && vSpeed < 0f) vSpeed = -2f;               // stick to the ground between jumps
        if (grounded && Input.GetKeyDown(KeyCode.Space)) vSpeed = JumpSpeed; // hop
        vSpeed -= Gravity * Time.deltaTime;

        Vector3 vel = pan * (MoveSpeed * 1.8f) + Vector3.up * vSpeed;
        cc.Move(vel * Time.deltaTime);
    }

    // ---- Gun ----
    void FireGun()
    {
        var g = Guns[gunTier];
        if (Time.time < nextShot || ammo <= 0) return;
        nextShot = Time.time + g.rate;
        ammo--;

        Vector3 start = cam.transform.position;
        Vector3 end = start + cam.transform.forward * 150f;
        if (RaycastNoSelf(150f, out RaycastHit hit))
        {
            end = hit.point;
            var z = hit.collider.GetComponentInParent<Zombie>();
            if (z != null) z.TakeDamage(g.dmg);
            else HitPlayer(hit, g.dmg); // PvP: shoot enemy players
        }
        Effects.Tracer(start + cam.transform.forward * 0.8f, end); // visible trail
        Effects.GunShot(start);
        Effects.MuzzleFlash(cam.transform.TransformPoint(new Vector3(0.32f, -0.26f, 0.95f))); // barrel-tip flash
        gunRecoil = 1f; // kick the viewmodel back
        gunHeat = 1f;   // barrel glows hot
    }

    // Recoil kick (decays) + barrel heat glow, applied to the gun viewmodel each frame.
    void AnimateViewmodel()
    {
        if (viewmodel == null || !viewmodel.activeSelf) return;
        gunRecoil = Mathf.MoveTowards(gunRecoil, 0f, 6f * Time.deltaTime);
        gunHeat = Mathf.MoveTowards(gunHeat, 0f, 0.5f * Time.deltaTime); // ~2s cooldown
        viewmodel.transform.localPosition = vmBasePos + new Vector3(0f, 0.012f, -0.10f) * gunRecoil;
        viewmodel.transform.localRotation = Quaternion.Euler(-7f * gunRecoil, 0f, 0f);
        if (gunMuzzle != null)
        {
            var r = gunMuzzle.GetComponent<Renderer>();
            if (r != null)
            {
                var m = r.material;
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor"))
                    m.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.1f) * (gunHeat * 3f)); // glows red-hot, blooms
            }
        }
    }

    public void AddAmmo(int n) { ammo = Mathf.Min(Guns[gunTier].mag, ammo + n); }

    public void AddMetal(int delta) { Metal = Mathf.Clamp(Metal + delta, 0, MetalMax); }
    public void AddOil(int delta) { Oil = Mathf.Clamp(Oil + delta, 0, OilMax); }

    // ---- Build tool ----
    // Co-op: on a client, buildings are owned by the host. We spend our own metal locally
    // (HUD), then send a request; the host creates/edits the authoritative building and
    // streams it back as a synced copy. On host/offline this is false → act directly.
    static bool NetClient => LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost;

    void BuildPrimary()
    {
        if (!RaycastNoSelf(30f, out RaycastHit hit)) return;
        var b = hit.collider.GetComponentInParent<Buildable>();
        if (b != null && b.NeedsRepair) // damaged → repair; full-health → build on top of it
        {
            if (NetClient) LanManager.Instance.SendBuildAction(b.NetId, 3, 60);
            else b.Repair(60f);
            return;
        }
        Vector3 place = new Vector3(hit.point.x, PlaceY(SelectedBuild, hit.point.x, hit.point.z), hit.point.z);
        PlaceOne(SelectedBuild, place, BuildYaw(), BCost(SelectedBuild));
    }

    // Place a single building (net-aware). Returns true if it went down and metal was spent.
    bool PlaceOne(int type, Vector3 pos, float yaw, int cost)
    {
        if (!IsPlayerBuildable(type)) return false; // base Dispenser is never player-built
        if (Metal < cost) return false;
        if (NetClient)
        {
            LanManager.Instance.SendBuildPlace(type, pos, yaw);
            AddMetal(-cost); builtSomething = true; return true;
        }
        if (Buildable.Create(type, pos, Quaternion.Euler(0f, yaw, 0f), this) != null)
        {
            AddMetal(-cost); builtSomething = true; return true;
        }
        return false;
    }

    // ---- Drag-build (pipe / conveyor): hold LMB at the source, walk, release to lay a line ----
    bool layingDrag;
    Vector3 dragStart;
    int dragSegs, dragTotalCost;   // live count/cost preview shown while dragging a line
    public const float DragSegment = 3f; // pipe/conveyor segment length (metres)

    // Placement scheme: a QUICK click drops exactly one building; HOLDING LMB past HoldToDrag turns
    // it into a drag that lays a whole row (only for drag-capable types). No manual rotation.
    const float HoldToDrag = 0.5f;
    bool lmbArmed;      // LMB is down, deciding click-vs-drag
    float lmbHeld;      // how long LMB has been held this press

    // A quick tap: place a single piece at the crosshair. Drag types reuse LayLine's single-shot
    // branch (wall facing + wall-top snap); everything else goes through BuildPrimary.
    void HandleBuildInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lmbArmed = true; lmbHeld = 0f; layingDrag = false;
            if (RaycastNoSelf(30f, out RaycastHit h0)) dragStart = SnapDragPoint(SelectedBuild, h0.point);
        }
        else if (lmbArmed && Input.GetMouseButton(0))
        {
            lmbHeld += Time.deltaTime;
            if (!layingDrag && lmbHeld >= HoldToDrag && IsDragBuild(SelectedBuild))
                layingDrag = true; // held long enough → become a drag
        }
        else if (lmbArmed && Input.GetMouseButtonUp(0))
        {
            lmbArmed = false;
            if (layingDrag) EndDragBuild();          // lay the whole line
            else PlaceSingleClick();                 // quick tap → one piece
            layingDrag = false;
        }
    }

    void PlaceSingleClick()
    {
        if (IsDragBuild(SelectedBuild))
        {
            if (RaycastNoSelf(30f, out RaycastHit hit))
            {
                Vector3 p = SnapDragPoint(SelectedBuild, hit.point);
                LayLine(SelectedBuild, p, p); // len<1 → single placement (wall facing + surface snap)
            }
        }
        else BuildPrimary();
    }

    // Drag-built (hold LMB, drag a line, release): pipes/conveyors AND every "СТРОИТЕЛЬНОЕ" +
    // "ОБОРОНА" item — so you can lay whole rows of walls, turrets, mines, etc. in one drag.
    // Drag-buildable types (hold LMB, drag a line): walls & construction, extraction rigs, the
    // defensive turrets/traps/AA, and the pipe/conveyor lines. Listed explicitly so it stays
    // correct no matter how the build MENU is split into categories.
    static readonly System.Collections.Generic.HashSet<int> DragTypes = new System.Collections.Generic.HashSet<int>
    {
        3, 16, 17, 4, 6, 20, 23, 26, 5,   // walls, door, stairs, ladder, watchtower, platform, bridge
        29, 32,                            // oil derrick, metal drill
        0, 19, 2, 7, 8, 15, 36, 24, 25, 34, 37, 38, // sentry, rpg, mine, landmine, wire, AA, SAM, blades, silo, flamethrower, FPV pad, Geran-2
        27, 30,                            // oil pipe, conveyor
    };
    static bool IsDragBuild(int type) => DragTypes.Contains(type);
    // Walls, doors & barbed wire run their WIDTH along the drag line (perpendicular facing) so a
    // dragged row forms a continuous barrier; other items face along the line.
    static bool IsWallDrag(int type) => type == 3 || type == 16 || type == 17 || type == 4 || type == 8;
    // Spacing between laid pieces (footprint-based), so a dragged row doesn't overlap.
    static float DragStep(int type)
    {
        switch (type)
        {
            case 16: return 4.4f;                 // long wall
            case 3: case 17: case 4: return 2.2f; // wall / tall wall / door
            case 8: return 2.4f;                  // barbed wire (its width)
            case 27: case 30: return DragSegment; // pipe / conveyor
            case 23: case 26: return 8f;          // watchtower / big platform (huge footprint)
            case 5: case 12: case 13: case 14: return 3.2f; // bridges
            case 29: case 32: return 4f;          // oil derrick / drill
            default: return 2.6f;                 // turrets, mines, ladders, etc.
        }
    }

    void BeginDragBuild()
    {
        if (RaycastNoSelf(30f, out RaycastHit hit)) { dragStart = SnapDragPoint(SelectedBuild, hit.point); layingDrag = true; }
    }

    void EndDragBuild()
    {
        layingDrag = false;
        if (!RaycastNoSelf(30f, out RaycastHit hit)) return;
        LayLine(SelectedBuild, dragStart, SnapDragPoint(SelectedBuild, hit.point));
    }

    // Drag-build snapping: pull a pipe/conveyor endpoint onto the nearest SOURCE (refinery/mine)
    // within range so a line lands cleanly on it. It deliberately does NOT snap to existing
    // pipes/conveyors — that used to (a) make a short drag jump 14 m onto a far line and (b) glue a
    // second line onto the first when running two lines from one source. Networks still auto-connect
    // by proximity (16 m link), so no snap-to-line is needed.
    const float SnapRange = 10f;
    Vector3 SnapDragPoint(int type, Vector3 p)
    {
        float bestSq = SnapRange * SnapRange; bool found = false; Vector3 best = p;
        void Consider(Vector3 tp)
        {
            float dx = tp.x - p.x, dz = tp.z - p.z, d = dx * dx + dz * dz;
            if (d < bestSq) { bestSq = d; best = new Vector3(tp.x, p.y, tp.z); found = true; }
        }
        if (type == 27) // oil pipe → snap to oil sources (НПЗ/вышка/хаб) only
        {
            foreach (var os in OilSources.All) if (os != null && os.OilTransform != null) Consider(os.OilTransform.position);
        }
        else if (type == 30) // conveyor → snap to metal sources (шахта/буровая) only
        {
            foreach (var ms in MetalSources.All) if (ms != null && ms.MetalTransform != null) Consider(ms.MetalTransform.position);
        }
        return found ? best : p;
    }

    // Height to place a dragged piece at (x,z): the TOP of any wall/platform standing there,
    // otherwise the ground. Lets you drag a row of turrets/wire/etc. along the top of a wall
    // instead of burying them at ground level through it. Casts straight down and only steps up
    // onto solid Buildables (never zombies/props), so an empty spot still lands on the ground.
    float SurfaceY(float x, float z)
    {
        float ground = GameBootstrap.Hill(x, z);
        Vector3 from = new Vector3(x, ground + 40f, z);
        var hits = Physics.RaycastAll(from, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = ground;
        foreach (var h in hits)
        {
            if (h.point.y <= best + 0.05f) continue;
            if (h.collider.GetComponentInParent<Buildable>() != null) best = h.point.y; // stand on it
        }
        return best;
    }

    // Placement height: oil pipes (27) & conveyors (30) ALWAYS sit on the ground (never climb onto a
    // hub/wall/building); everything else may sit on top of walls & platforms via SurfaceY.
    float PlaceY(int type, float x, float z) => (type == 27 || type == 30) ? GameBootstrap.Hill(x, z) : SurfaceY(x, z);

    // Pipes AND conveyors now lay FREELY along the drag, like walls — any angle/diagonal, no 90° lock.
    static bool IsPathBuild(int type) => false;

    // Ordered (position, yaw) pieces for a dragged line a→b.
    List<(Vector3 pos, float yaw)> DragPieces(int type, Vector3 a, Vector3 b)
    {
        var list = new List<(Vector3 pos, float yaw)>();
        float step = DragStep(type);
        if (IsPathBuild(type))
        {
            // Project the endpoint onto a single straight axis so the pipe/belt is never diagonal.
            // buildYawStep (R) rotates the chosen axis in 90° steps.
            Vector3 d0 = b - a; d0.y = 0f;
            float ang = Mathf.Round(Mathf.Atan2(d0.x, d0.z) / (Mathf.PI * 0.5f)) * 90f + buildYawStep * 90f;
            float rad = ang * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            float len = Vector3.Dot(d0, axis);              // signed reach of the drag along that axis
            if (len < 0f) { axis = -axis; len = -len; }     // ALWAYS lay toward the drag, never backwards
            AppendLeg(list, a, a + axis * len, step, type, false);
        }
        else AppendLeg(list, a, b, step, type, false);
        return list;
    }

    void AppendLeg(List<(Vector3 pos, float yaw)> list, Vector3 p0, Vector3 p1, float step, int type, bool skipFirst)
    {
        Vector3 d = p1 - p0; d.y = 0f;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Vector3 dir = d / len;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // pipe/belt axis is +Z
        if (IsWallDrag(type)) yaw += 90f;                       // walls: their width runs along the path
        int count = Mathf.Max(1, Mathf.RoundToInt(len / step));
        float s = len / count;
        for (int i = (skipFirst ? 1 : 0); i <= count; i++)
            list.Add((p0 + dir * (i * s), yaw));
    }

    // Lay a chain of segments from a→b. Conveyors/pipes turn a corner (L-shape); others go straight.
    void LayLine(int type, Vector3 a, Vector3 b, bool buildState = false)
    {
        int cost = BCost(type);
        Vector3 d = b - a; d.y = 0f;
        // A click (or a drag shorter than ~¾ of one segment) places EXACTLY ONE piece; you have to
        // drag at least that far to start laying a row. Keeps a plain click from dropping two.
        if (d.magnitude < DragStep(type) * 0.75f) // single click: build EXACTLY as the ghost preview shows
        {
            Vector3 p0 = new Vector3(a.x, PlaceY(type, a.x, a.z), a.z);
            PlaceOne(type, p0, BuildYaw(), cost); // same yaw as the preview (wide side faces the player)
            return;
        }
        foreach (var piece in DragPieces(type, a, b))
        {
            if (Metal < cost) break;
            Vector3 p = piece.pos;
            p.y = PlaceY(type, p.x, p.z);
            PlaceOne(type, p, piece.yaw, cost);
        }
    }

    // Placement yaw. Watchtower (23) and big platform (26) carry their ladder on the +z
    // (front) side; flip them 180° so the ladder faces the player — you build it and can
    // immediately climb up from where you're standing.
    float BuildYaw()
    {
        float yaw = transform.eulerAngles.y;
        if (SelectedBuild == 23 || SelectedBuild == 26) yaw += 180f;
        return yaw;
    }

    void SellBuild()
    {
        if (RaycastNoSelf(8f, out RaycastHit hit))
        {
            var b = hit.collider.GetComponentInParent<Buildable>();
            if (b != null)
            {
                // Never let the base's critical dispenser be sold — that would instantly lose the run.
                if (b is Dispenser d && d.Critical)
                {
                    Toast(Lang.T("Раздатчик — это ваша БАЗА, снести нельзя!", "The Dispenser is your BASE, you can't tear it down!"));
                    Effects.Burst(b.transform.position + Vector3.up * 1.5f, new Color(1f, 0.3f, 0.2f), 6);
                    return;
                }
                AddMetal(b.BuildCost);
                if (NetClient) LanManager.Instance.SendBuildAction(b.NetId, 4, 0);
                else Destroy(b.gameObject);
            }
        }
    }

    // ---- transient on-screen toast (feedback for sell/delete actions) ----
    string toast = ""; float toastUntil;
    void Toast(string s) { toast = s; toastUntil = Time.time + 2.5f; }

    // ---- relocate a building with the middle mouse button ----
    void MoveBuildAction()
    {
        if (heldBuild == null)
        {
            if (!RaycastNoSelf(30f, out RaycastHit hit)) return;
            var b = hit.collider.GetComponentInParent<Buildable>();
            if (b == null) return;
            // The base lifeline can't be relocated normally — but in ENDLESS it can (it respawns if lost).
            if (b is Dispenser d && d.Critical && !GameRoot.Infinite) { Toast(Lang.T("Раздатчик-базу переносить нельзя", "Can't move the base Dispenser")); return; }
            if (b.Building) { Toast(Lang.T("Постройка ещё строится", "Building is still under construction")); return; }
            heldBuild = b;
            b.BeingMoved = true; // freeze its function while carried (no free metal/oil/fire)
            foreach (var c in b.GetComponentsInChildren<Collider>()) if (c != null) c.enabled = false; // let the ground ray pass through
            Toast(Lang.T("Перенос: наведись и нажми СКМ, чтобы поставить", "Move: aim and press MMB to place it"));
        }
        else
        {
            PlaceHeldAtCrosshair();
            foreach (var c in heldBuild.GetComponentsInChildren<Collider>()) if (c != null) c.enabled = true;
            heldBuild.BeingMoved = false; // dropped — re-evaluates its connection at the new spot
            Effects.Burst(heldBuild.transform.position + Vector3.up, new Color(0.4f, 1f, 0.5f), 6);
            heldBuild = null;
        }
    }

    void FollowHeldBuild()
    {
        if (heldBuild == null) return; // may have been destroyed mid-carry
        PlaceHeldAtCrosshair();
    }

    void PlaceHeldAtCrosshair()
    {
        if (heldBuild == null) return;
        if (RaycastNoSelf(80f, out RaycastHit hit))
        {
            Vector3 p = hit.point;
            p.y = GameBootstrap.Hill(p.x, p.z) + 0.02f;
            heldBuild.transform.position = p;
        }
    }

    /// <summary>Delete ALL placed buildings of the same type as the one you're aiming at (a bulk
    /// "clear this class" tool). Refunds each like a sell. The critical dispenser is never touched.</summary>
    void DeleteByClass()
    {
        if (!RaycastNoSelf(8f, out RaycastHit hit)) { Toast(Lang.T("наведись на постройку, чтобы снести весь её класс", "aim at a building to delete its whole class")); return; }
        var aim = hit.collider.GetComponentInParent<Buildable>();
        if (aim == null) return;
        int type = aim.Type;
        string name = (type >= 0 && type < BuildNames.Length) ? BName(type) : "?";

        int removed = 0, refund = 0;
        var doomed = new System.Collections.Generic.List<Buildable>();
        foreach (var b in Buildable.All)
        {
            if (b == null || b.Type != type) continue;
            if (b is Dispenser d && d.Critical) continue; // never the base lifeline
            doomed.Add(b);
        }
        foreach (var b in doomed)
        {
            refund += b.BuildCost;
            if (NetClient) LanManager.Instance.SendBuildAction(b.NetId, 4, 0);
            else Destroy(b.gameObject);
            removed++;
        }
        if (removed > 0) { AddMetal(refund); Toast(Lang.T($"Снесено «{name}»: {removed} шт. (возврат {refund} мет.)", $"Deleted \"{name}\": {removed} pcs. (refund {refund} metal)")); }
        else Toast(Lang.T($"«{name}» — нечего сносить", $"\"{name}\" — nothing to delete"));
    }

    void Interact()
    {
        if (!RaycastNoSelf(8f, out RaycastHit hit)) return;
        // Draw oil from a captured refinery's barrel (E).
        var refinery = hit.collider.GetComponentInParent<Refinery>();
        if (refinery != null) { refinery.CollectOil(this); return; }
        // Get into a car (E). Only a real, finished car (not a co-op puppet copy).
        var car = hit.collider.GetComponentInParent<Car>();
        if (car != null && !car.Building && !car.IsPuppet) { EnterVehicle(car); return; }
        var door = hit.collider.GetComponentInParent<Door>();
        if (door != null)
        {
            door.Toggle(); // local visual/collision feedback
            if (NetClient) LanManager.Instance.SendBuildAction(door.NetId, 5, 0);
            return;
        }
        var b = hit.collider.GetComponentInParent<Buildable>();
        if (b == null) return;
        // Special weapon still being funded: pour a capped chunk of metal per press, then —
        // once the metal goal is met — oil from your personal reserve (needed to switch on).
        if (b.IsFunding)
        {
            // Metal first — fully. Only once the metal goal is met do we start spending oil,
            // so running out of metal mid-funding never silently drains your oil instead.
            if (b.FundingPaid < b.FundingRequired)
            {
                if (Metal <= 0) return; // need metal — don't touch oil yet
                int fund = Mathf.Min(Metal, Mathf.Min(b.FundChunk, b.FundingRemaining));
                if (fund <= 0) return;
                if (NetClient) { if (b.UpgradeReadyIn <= 0f) { AddMetal(-fund); b.MarkNetCooldown(); LanManager.Instance.SendBuildAction(b.NetId, 1, fund); } }
                else if (b.Fund(fund)) AddMetal(-fund);
            }
            else if (b.OilPaid < b.OilRequired && Oil > 0 && !NetClient)
            {
                int oil = Mathf.Min(Oil, Mathf.Min(OilFundChunk, b.OilRemaining));
                if (oil > 0 && b.FundOil(oil)) AddOil(-oil);
            }
        }
        // Funded special weapon: keep its ammo reserve topped up first; once full, E upgrades it.
        // Super-weapons run on OIL (ReserveIsOil); hardcore turret ammo still uses metal.
        else if (b.UsesReserve)
        {
            bool oilFuel = b.ReserveIsOil;
            int wallet = oilFuel ? Oil : Metal;
            if (b.Reserve < b.ReserveMax && wallet > 0)
            {
                int load = Mathf.Min(wallet, Mathf.Min(ReserveLoadChunk, b.ReserveMax - b.Reserve));
                if (load <= 0) return;
                if (NetClient) { if (oilFuel) AddOil(-load); else AddMetal(-load); LanManager.Instance.SendBuildAction(b.NetId, 2, load); }
                else { int got = b.Refill(load); if (oilFuel) AddOil(-got); else AddMetal(-got); }
            }
            else if (b.CanUpgrade && wallet > 0)
            {
                int amount = Mathf.Min(wallet, b.InvestAmount);
                if (NetClient) { if (b.UpgradeReadyIn <= 0f) { if (oilFuel) AddOil(-amount); else AddMetal(-amount); b.MarkNetCooldown(); LanManager.Instance.SendBuildAction(b.NetId, 0, amount); } }
                else if (b.Invest(amount)) { if (oilFuel) AddOil(-amount); else AddMetal(-amount); }
            }
        }
        // Ordinary building: upgrade if possible & affordable; otherwise repair.
        else if (b.CanUpgrade && Metal > 0)
        {
            int amount = Mathf.Min(Metal, b.InvestAmount); // invest up to 50, or all you have
            if (NetClient) { if (b.UpgradeReadyIn <= 0f) { AddMetal(-amount); b.MarkNetCooldown(); LanManager.Instance.SendBuildAction(b.NetId, 0, amount); } }
            else if (b.Invest(amount)) AddMetal(-amount);
        }
        else if (b.NeedsRepair)
        {
            if (NetClient) LanManager.Instance.SendBuildAction(b.NetId, 3, 80);
            else b.Repair(80f);
        }
    }

    // ---- Car ----
    void EnterVehicle(Car car)
    {
        vehicle = car;
        car.SetOccupied(true);
        cc.enabled = false;                                   // car drives us now
        if (playerBody != null) playerBody.SetActive(false);
        if (viewmodel != null) viewmodel.SetActive(false);
        if (preview != null) preview.SetActive(false);
        if (rangeSphere != null) rangeSphere.SetActive(false);
        SetAimed(null);
        // Third-person chase camera, looking slightly down at the car.
        pitch = 14f;
        cam.transform.localPosition = new Vector3(0f, 3.4f, -7f);
        cam.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void DriveVehicle()
    {
        if (Input.GetKeyDown(KeyCode.F)) { ExitVehicle(); return; }

        float throttle = Input.GetAxisRaw("Vertical");   // W/S
        float steer = Input.GetAxisRaw("Horizontal");    // A/D
        vehicle.Drive(throttle, steer);

        // Ride along with the car; the camera (our child) chases from behind.
        transform.position = vehicle.transform.position;
        transform.rotation = Quaternion.Euler(0f, vehicle.transform.eulerAngles.y, 0f);
    }

    void ExitVehicle()
    {
        var car = vehicle;
        vehicle = null;
        if (car != null) car.SetOccupied(false);

        // Step out beside the car, back onto the ground.
        Vector3 basePos = car != null ? car.transform.position : transform.position;
        Vector3 side = car != null ? car.transform.right : transform.right;
        Vector3 outPos = basePos + side * 2.6f;
        outPos.y = GameBootstrap.Hill(outPos.x, outPos.z) + 1.2f;
        transform.position = outPos;

        cc.enabled = true;
        if (playerBody != null) playerBody.SetActive(true);
        if (viewmodel != null) viewmodel.SetActive(true);
        // Restore the first-person camera.
        pitch = 0f;
        cam.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        cam.transform.localEulerAngles = Vector3.zero;
    }

    // ---- Shovel ----
    void Dig()
    {
        if (Time.time < nextShot) return;
        nextShot = Time.time + 0.2f;
        // Only the terrain has a MeshCollider — dig there, not through walls/zombies.
        if (RaycastNoSelf(5f, out RaycastHit hit) && hit.collider is MeshCollider)
        {
            GameBootstrap.Dig(hit.point, 2.5f, 0.7f);
            if (LanManager.Instance != null && LanManager.Instance.Active) LanManager.Instance.FxDig(hit.point, 2.5f, 0.7f);
            Effects.Dirt(hit.point);
        }
    }

    // ---- Wrench ----
    void Swing()
    {
        if (Time.time < nextShot) return;
        nextShot = Time.time + 0.45f;
        if (RaycastNoSelf(3.5f, out RaycastHit hit))
        {
            var z = hit.collider.GetComponentInParent<Zombie>();
            if (z != null) { z.TakeDamage(40f); return; }
            if (HitPlayer(hit, 45f)) return; // PvP melee
            var b = hit.collider.GetComponentInParent<Buildable>();
            if (b != null && b.NeedsRepair) b.Repair(100f);
        }
    }

    // PvP: if the ray hit an enemy player's avatar, send the damage over the network.
    bool HitPlayer(RaycastHit hit, float dmg)
    {
        if (!GameRoot.IsPvp || LanManager.Instance == null || !LanManager.Instance.Active) return false;
        var rp = hit.collider.GetComponentInParent<RemotePlayer>();
        if (rp == null || rp.Team == GameRoot.PvpTeam) return false; // no friendly fire
        LanManager.Instance.SendPlayerHit(rp.Id, dmg);
        return true;
    }

    // ---- Placement ghost ----
    // ---- drag-build path preview: a pooled chain of transparent ghost segments ----
    readonly List<GameObject> dragGhosts = new List<GameObject>();
    int dragGhostType = -1;

    void ShowDragPath(Vector3 a, Vector3 b)
    {
        if (dragGhostType != SelectedBuild) { ClearDragGhosts(); dragGhostType = SelectedBuild; } // rebuilt for the right model

        b = SnapDragPoint(SelectedBuild, b); // preview the snapped endpoint
        Vector3 dd = b - a; dd.y = 0f;
        List<(Vector3 pos, float yaw)> pieces;
        if (dd.magnitude < DragStep(SelectedBuild) * 0.75f) // short = a click: preview exactly ONE
        {
            float sy = transform.eulerAngles.y;
            if (IsWallDrag(SelectedBuild)) sy += 90f;
            pieces = new List<(Vector3 pos, float yaw)> { (a, sy) };
        }
        else pieces = DragPieces(SelectedBuild, a, b); // same L-shape/straight layout as LayLine

        int cost = BCost(SelectedBuild);
        dragSegs = pieces.Count; dragTotalCost = pieces.Count * cost;   // live preview for the HUD
        bool ok = Metal >= pieces.Count * cost;
        Color col = ok ? new Color(0.3f, 1f, 0.3f, 0.16f) : new Color(1f, 0.4f, 0.3f, 0.16f);

        for (int i = 0; i < pieces.Count; i++)
        {
            if (i >= dragGhosts.Count)
            {
                var g = Models.BuildVisual(SelectedBuild, 1);
                GameBootstrap.MakeGhost(g, col);
                dragGhosts.Add(g);
            }
            var go = dragGhosts[i];
            if (go == null) { go = Models.BuildVisual(SelectedBuild, 1); GameBootstrap.MakeGhost(go, col); dragGhosts[i] = go; }
            go.SetActive(true);
            Vector3 p = pieces[i].pos;
            p.y = PlaceY(SelectedBuild, p.x, p.z) + 0.02f; // pipes stay on ground; others sit on wall tops
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, pieces[i].yaw, 0f);
            GameBootstrap.SetGhostColor(go, col);
        }
        for (int i = pieces.Count; i < dragGhosts.Count; i++) if (dragGhosts[i] != null) dragGhosts[i].SetActive(false);
    }

    void HideDragPath()
    {
        for (int i = 0; i < dragGhosts.Count; i++) if (dragGhosts[i] != null) dragGhosts[i].SetActive(false);
    }

    void ClearDragGhosts()
    {
        for (int i = 0; i < dragGhosts.Count; i++) if (dragGhosts[i] != null) Destroy(dragGhosts[i]);
        dragGhosts.Clear();
    }

    void UpdatePreview()
    {
        bool show = false;
        Vector3 pos = Vector3.zero;

        if (tool == Tool.Build && !IsDead)
        {
            if (preview == null || previewType != SelectedBuild)
            {
                if (preview != null) Destroy(preview);
                preview = Models.BuildVisual(SelectedBuild, 1);
                previewType = SelectedBuild;
                GameBootstrap.MakeGhost(preview, new Color(0.3f, 1f, 0.3f, 0.18f));
            }
            if (RaycastNoSelf(30f, out RaycastHit hit))
            {
                var hb = hit.collider.GetComponentInParent<Buildable>();
                if (hb == null || !hb.NeedsRepair) // not aiming to repair an existing structure
                {
                    show = true;
                    // Snap to the TOP of whatever's under the crosshair (wall/platform/bridge) so a
                    // single piece sits on the wall instead of stuck in its side; plain terrain = ground.
                    pos = new Vector3(hit.point.x, PlaceY(SelectedBuild, hit.point.x, hit.point.z) + 0.02f, hit.point.z);
                }
            }
        }

        // Drag-build: show the WHOLE path as a chain of ghost segments, not a single ghost.
        if (layingDrag && IsDragBuild(SelectedBuild))
        {
            if (preview != null) preview.SetActive(false);
            if (show) ShowDragPath(dragStart, pos); else HideDragPath();
            return;
        }
        HideDragPath();

        if (preview != null) preview.SetActive(show);
        if (show)
        {
            preview.transform.position = pos;
            preview.transform.rotation = Quaternion.Euler(0f, BuildYaw(), 0f);
            bool ok = Metal >= BCost(SelectedBuild);
            GameBootstrap.SetGhostColor(preview, ok ? new Color(0.3f, 1f, 0.3f, 0.18f) : new Color(1f, 0.3f, 0.3f, 0.18f));
        }
    }

    // ---- Misc ----
    public void Heal(float amount) { if (!IsDead) Health = Mathf.Min(MaxHealth, Health + amount); }

    // 3.2 node-mod test mode: hotkeys to reload the .zmod files and manually FIRE each event, so you
    // can see your graph's actions trigger without waiting for the real game event.
    void HandleModTest()
    {
        if (Input.GetKeyDown(KeyCode.F5)) { ModRuntime.Load(); ModRuntime.OnGameStart(); Toast(Lang.T($"Моды перезагружены: {ModRuntime.RuleCount} правил", $"Mods reloaded: {ModRuntime.RuleCount} rules")); }
        if (Input.GetKeyDown(KeyCode.F6))  { ModRuntime.OnGameStart();     Toast("→ GAME_START"); }
        if (Input.GetKeyDown(KeyCode.F7))  { ModRuntime.OnWaveStart();     Toast("→ WAVE_START"); }
        if (Input.GetKeyDown(KeyCode.F8))  { ModRuntime.OnWaveClear();     Toast("→ WAVE_CLEAR"); }
        if (Input.GetKeyDown(KeyCode.F9))  { ModRuntime.OnZombieKilled();  Toast("→ ZOMBIE_KILLED"); }
        if (Input.GetKeyDown(KeyCode.F10)) { ModRuntime.OnPlayerDamaged(); Toast("→ PLAYER_DAMAGED"); }
        if (Input.GetKeyDown(KeyCode.F11)) { ModRuntime.OnPlayerDied();    Toast("→ PLAYER_DIED"); }
        if (Input.GetKeyDown(KeyCode.F12)) { ModRuntime.OnBuildingBuilt(); Toast("→ BUILDING_BUILT"); }
    }

    public static bool GodMode; // debug: player takes no damage

    public void TakeDamage(float amount)
    {
        if (IsDead || GodMode || GameRoot.Sandbox) return; // sandbox = immortal
        Health = Mathf.Max(0f, Health - amount);
        ModRuntime.OnPlayerDamaged(); // 3.2: fire PLAYER_DAMAGED mod actions
        if (IsDead) { deathTime = Time.time; Deaths++; ModRuntime.OnPlayerDied(); } // counted once per death (early-out above guards re-entry)
    }

    void Respawn()
    {
        Health = MaxHealth;
        cc.enabled = false;
        transform.position = GameBootstrap.PlayerSpawn(); // always come back beside the starter base
        cc.enabled = true;
    }

    // ---- Viewmodel ----
    GameObject VPrim(PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(viewmodel.transform, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
        return g;
    }

    void BuildViewmodel()
    {
        if (cam == null) return;
        if (viewmodel != null) Destroy(viewmodel);
        viewmodel = new GameObject("Viewmodel");
        viewmodel.transform.SetParent(cam.transform, false);
        viewmodel.transform.localPosition = new Vector3(0.32f, -0.28f, 0.55f);
        vmBasePos = viewmodel.transform.localPosition;
        gunMuzzle = null;

        Color dark = new Color(0.18f, 0.18f, 0.2f);
        Color metal = new Color(0.35f, 0.37f, 0.4f);

        switch (tool)
        {
            case Tool.Gun:
                VPrim(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.12f, 0.3f), dark);
                gunMuzzle = VPrim(PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0.25f), new Vector3(0.04f, 0.18f, 0.04f), metal, new Vector3(90f, 0f, 0f));
                VPrim(PrimitiveType.Cube, new Vector3(0f, -0.14f, -0.06f), new Vector3(0.1f, 0.16f, 0.12f), dark);
                break;
            case Tool.Build:
                // Engineer build PDA: body + screen + LED + 2x5 keypad.
                VPrim(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.24f, 0.3f, 0.06f), new Color(0.22f, 0.22f, 0.24f));
                VPrim(PrimitiveType.Cube, new Vector3(0f, 0.08f, 0.035f), new Vector3(0.17f, 0.09f, 0.01f), new Color(0.05f, 0.08f, 0.12f));
                VPrim(PrimitiveType.Sphere, new Vector3(0.085f, 0.125f, 0.035f), new Vector3(0.02f, 0.02f, 0.02f), Color.red);
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 5; col++)
                        VPrim(PrimitiveType.Cube, new Vector3(-0.08f + col * 0.04f, -0.05f - row * 0.045f, 0.035f), new Vector3(0.03f, 0.03f, 0.012f), new Color(0.82f, 0.8f, 0.76f));
                break;
            case Tool.Wrench:
                VPrim(PrimitiveType.Cube, new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.04f, 0.42f), metal);
                VPrim(PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.22f), new Vector3(0.14f, 0.06f, 0.08f), metal);
                break;
            case Tool.Shovel:
                VPrim(PrimitiveType.Cube, new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.04f, 0.5f), new Color(0.4f, 0.28f, 0.16f)); // handle
                VPrim(PrimitiveType.Cube, new Vector3(0f, 0.0f, 0.32f), new Vector3(0.18f, 0.03f, 0.22f), metal);                       // blade
                break;
        }
    }

    // ---- HUD ----
    static GUIStyle _lbl, _ctr, _sm, _tool16, _line24, _big52, _lblRight;
    static GUIStyle Lbl => _lbl ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
    static GUIStyle LblRight => _lblRight ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
    static GUIStyle Ctr => _ctr ??= new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle Sm => _sm ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle Tool16 => _tool16 ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle Line24 => _line24 ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle Big52 => _big52 ??= new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

    void Panel(Rect r)
    {
        GUI.color = new Color(0f, 0f, 0f, UISettings.PanelAlpha);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void Bar(float x, float y, float w, float h, float frac, Color fill, string label)
    {
        GUI.color = new Color(0.12f, 0.12f, 0.13f, 0.95f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(frac), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 2f, w, h), label, Sm);
    }

    static int _dragId = -1;
    static Vector2 _dragGrab; // cursor pos where the HUD-element drag started
    // Apply a movable element's saved offset; in layout-edit mode, frame it and drag it.
    Rect Place(int id, Rect baseRect)
    {
        Vector2 off = UISettings.Offsets[id];
        Rect r = new Rect(baseRect.x + off.x, baseRect.y + off.y, baseRect.width, baseRect.height);
        if (UISettings.EditLayout)
        {
            // Drag by POLLING Input directly (not IMGUI events) — robust: works regardless of
            // event dispatch order, e.Use() consumption or MouseDrag events firing. Input.mousePosition
            // is screen-space with a bottom-left origin, so flip Y and divide by the HUD scale.
            Vector3 mp = Input.mousePosition;
            Vector2 m = new Vector2(mp.x, Screen.height - mp.y) / UI.Scale;
            bool held = Input.GetMouseButton(0);
            if (held && _dragId == -1 && r.Contains(m)) { _dragId = id; _dragGrab = m; } // grab this element
            if (_dragId == id)
            {
                if (!held) { _dragId = -1; }               // released → drop
                else
                {
                    UISettings.Offsets[id] += m - _dragGrab; // follow the cursor by absolute position
                    _dragGrab = m;
                    r = new Rect(baseRect.x + UISettings.Offsets[id].x, baseRect.y + UISettings.Offsets[id].y, baseRect.width, baseRect.height);
                }
            }

            GUI.color = new Color(0.3f, 1f, 0.5f, 0.9f); // green frame so each element reads as movable
            float t = 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        return r;
    }

    static GUIStyle _ammoStyle;

    // Hardcore HUD: a small ammo readout floating over every turret, coloured by how much
    // is left (green → low/orange → empty/red). Projects each turret to GUI space.
    void DrawTurretAmmo()
    {
        if (cam == null) return;
        _ammoStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        foreach (var b in Buildable.All)
        {
            if (b == null || !(b is Sentry) || !b.UsesReserve || b.Building) continue;
            Vector3 sp = cam.WorldToScreenPoint(b.transform.position + Vector3.up * 2.3f);
            if (sp.z <= 0.5f || sp.z > 65f) continue;             // behind camera or too far
            float gx = sp.x / UI.Scale;
            float gy = (Screen.height - sp.y) / UI.Scale;
            int ammo = b.Reserve, max = Mathf.Max(1, b.ReserveMax);
            float frac = (float)ammo / max;
            GUI.color = ammo <= 0 ? new Color(1f, 0.3f, 0.3f)
                      : frac < 0.25f ? new Color(1f, 0.75f, 0.2f)
                      : new Color(0.55f, 1f, 0.55f);
            GUI.Label(new Rect(gx - 80f, gy - 10f, 160f, 20f), ammo <= 0 ? Lang.T("НЕТ ПАТРОНОВ", "NO AMMO") : Lang.T("патроны ", "ammo ") + ammo, _ammoStyle);
        }
        GUI.color = Color.white;
    }

    static GUIStyle _refStyle;

    // Floating НПЗ status: name + state, a control/capture bar, barrel oil, and an E-prompt
    // when you're stood at the barrel. Projects each refinery to GUI space (like turret ammo).
    void DrawRefineries()
    {
        if (cam == null) return;
        _refStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        foreach (var rf in Refinery.All)
        {
            if (rf == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(rf.transform.position + Vector3.up * 7.2f);
            if (sp.z <= 0.5f || sp.z > 140f) continue; // behind camera or too far
            float gx = sp.x / UI.Scale, gy = (Screen.height - sp.y) / UI.Scale;
            float w = 188f;
            Rect box = new Rect(gx - w * 0.5f, gy - 18f, w, 52f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(box, Texture2D.whiteTexture);

            string state; Color sc;
            if (!rf.Captured) { state = rf.Capture > 0f ? Lang.T($"ЗАХВАТ {Mathf.RoundToInt(rf.Capture / Refinery.CaptureTime * 100f)}%", $"CAPTURE {Mathf.RoundToInt(rf.Capture / Refinery.CaptureTime * 100f)}%") : Lang.T("НЕЙТРАЛЕН", "NEUTRAL"); sc = new Color(0.8f, 0.8f, 0.8f); }
            else if (rf.NearZombies > 0) { state = Lang.T("ПОД АТАКОЙ!", "UNDER ATTACK!"); sc = new Color(1f, 0.5f, 0.2f); }
            else { state = Lang.T("ЗАХВАЧЕН", "CAPTURED"); sc = new Color(0.4f, 1f, 0.5f); }

            GUI.color = sc; GUI.Label(new Rect(box.x, box.y + 1f, w, 18f), Lang.T($"НПЗ — {state}", $"REFINERY — {state}"), _refStyle);

            // bar: capture progress (neutral) or control (held)
            float frac = rf.Captured ? rf.Control / Refinery.ControlMax : rf.Capture / Refinery.CaptureTime;
            Rect bar = new Rect(box.x + 8f, box.y + 21f, w - 16f, 7f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = rf.Captured ? (rf.NearZombies > 0 ? new Color(1f, 0.5f, 0.2f) : new Color(0.4f, 0.9f, 0.5f)) : new Color(0.6f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(box.x, box.y + 30f, w, 18f), Lang.T($"бочка: {Mathf.FloorToInt(rf.Oil)}/{Mathf.RoundToInt(Refinery.OilCap)}   E — набрать", $"barrel: {Mathf.FloorToInt(rf.Oil)}/{Mathf.RoundToInt(Refinery.OilCap)}   E — collect"), _refStyle);
        }
        GUI.color = Color.white;
    }

    // Floating ШАХТА status: name + state, capture/control bar, ore pile. Metal twin of DrawRefineries.
    void DrawMines()
    {
        if (cam == null) return;
        _refStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        foreach (var mn in OreMine.All)
        {
            if (mn == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(mn.transform.position + Vector3.up * 6.6f);
            if (sp.z <= 0.5f || sp.z > 140f) continue;
            float gx = sp.x / UI.Scale, gy = (Screen.height - sp.y) / UI.Scale;
            float w = 188f;
            Rect box = new Rect(gx - w * 0.5f, gy - 18f, w, 52f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(box, Texture2D.whiteTexture);

            string state; Color sc;
            if (!mn.Captured) { state = mn.Capture > 0f ? Lang.T($"ЗАХВАТ {Mathf.RoundToInt(mn.Capture / OreMine.CaptureTime * 100f)}%", $"CAPTURE {Mathf.RoundToInt(mn.Capture / OreMine.CaptureTime * 100f)}%") : Lang.T("НЕЙТРАЛЬНА", "NEUTRAL"); sc = new Color(0.8f, 0.8f, 0.8f); }
            else if (mn.NearZombies > 0) { state = Lang.T("ПОД АТАКОЙ!", "UNDER ATTACK!"); sc = new Color(1f, 0.5f, 0.2f); }
            else { state = Lang.T("ЗАХВАЧЕНА", "CAPTURED"); sc = new Color(0.4f, 1f, 0.5f); }

            GUI.color = sc; GUI.Label(new Rect(box.x, box.y + 1f, w, 18f), Lang.T($"ШАХТА — {state}", $"MINE — {state}"), _refStyle);

            float frac = mn.Captured ? mn.Control / OreMine.ControlMax : mn.Capture / OreMine.CaptureTime;
            Rect bar = new Rect(box.x + 8f, box.y + 21f, w - 16f, 7f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = mn.Captured ? (mn.NearZombies > 0 ? new Color(1f, 0.5f, 0.2f) : new Color(0.4f, 0.9f, 0.5f)) : new Color(0.6f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), Texture2D.whiteTexture);

            GUI.color = new Color(0.8f, 0.85f, 1f);
            GUI.Label(new Rect(box.x, box.y + 30f, w, 18f), Lang.T($"руда: {Mathf.FloorToInt(mn.Ore)}/{Mathf.RoundToInt(OreMine.OreCap)}   конвейер→чан", $"ore: {Mathf.FloorToInt(mn.Ore)}/{Mathf.RoundToInt(OreMine.OreCap)}   conveyor→vat"), _refStyle);
        }
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        UI.Begin(); // scale the whole HUD to the screen resolution
        float cx = UI.W * 0.5f;
        float cy = UI.H * 0.5f;

        // Crosshair — size from settings (0 = hidden).
        float ch = UISettings.Crosshair;
        if (ch > 0.01f)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cx - 14f * ch, cy - 2f * ch, 28f * ch, 4f * ch), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 2f * ch, cy - 14f * ch, 4f * ch, 28f * ch), Texture2D.whiteTexture);
        }

        // 3.2: node-mod TEST mode — status + hotkey legend so you can fire mod events on demand.
        if (GameRoot.ModTest && !buildMenuOpen)
        {
            var mt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            Panel(new Rect(10f, 96f, 520f, 96f));
            GUI.color = ModRuntime.Active ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.4f);
            GUI.Label(new Rect(20f, 100f, 500f, 22f), Lang.T($"ТЕСТ НОД — моды: {(ModRuntime.Active ? "ВКЛ" : "выкл")} ({ModRuntime.RuleCount} правил)",
                                                             $"NODE TEST — mods: {(ModRuntime.Active ? "ON" : "off")} ({ModRuntime.RuleCount} rules)"), mt);
            GUI.color = new Color(0.85f, 0.9f, 0.95f);
            GUI.Label(new Rect(20f, 124f, 500f, 22f), "F5 " + Lang.T("перезагрузить моды", "reload mods"), mt);
            GUI.Label(new Rect(20f, 146f, 500f, 22f), "F6 GAME_START  F7 WAVE_START  F8 WAVE_CLEAR  F9 ZOMBIE_KILLED", mt);
            GUI.Label(new Rect(20f, 168f, 500f, 22f), "F10 PLAYER_DAMAGED  F11 PLAYER_DIED  F12 BUILDING_BUILT", mt);
            GUI.color = Color.white;
        }

        // Live cost while dragging a build line — updates with the length.
        if (layingDrag && IsDragBuild(SelectedBuild))
        {
            bool afford = Metal >= dragTotalCost;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = afford ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.4f);
            GUI.Label(new Rect(cx - 220f, cy + 44f, 440f, 30f), Lang.T($"{dragSegs} шт.  —  {dragTotalCost} мет.", $"{dragSegs} pcs.  —  {dragTotalCost} metal"), st);
            GUI.color = Color.white;
        }

        // Hardcore: floating ammo readout above each turret (so you can see which need a refill).
        if (GameRoot.Hardcore && !buildMenuOpen) DrawTurretAmmo();

        // Refineries (НПЗ): floating capture/control/oil status over each (default mode only).
        if (Refinery.All.Count > 0 && !buildMenuOpen) DrawRefineries();
        // Mines (ШАХТА): same floating status for the metal source points.
        if (OreMine.All.Count > 0 && !buildMenuOpen) DrawMines();

        // Top-left stats panel (kills only — metal moved to bottom-centre)
        // Kills counter — top-right corner (movable).
        Rect kills = Place(2, new Rect(UI.W - 392f, 10f, 380f, 46f));
        Panel(kills);
        GUI.color = Color.yellow; GUI.Label(new Rect(kills.x + 12f, kills.y + 7f, 360f, 34f), Lang.T($"УБИТО: {Score}", $"KILLS: {Score}"), LblRight);

        // Player death counter — under the kills panel (movable).
        Rect deaths = Place(4, new Rect(UI.W - 392f, 60f, 380f, 38f));
        Panel(deaths);
        GUI.color = new Color(1f, 0.45f, 0.45f); GUI.Label(new Rect(deaths.x + 12f, deaths.y + 5f, 360f, 28f), Lang.T($"СМЕРТЕЙ: {Deaths}", $"DEATHS: {Deaths}"), LblRight);
        GUI.color = Color.white;

        // Bottom-left player HP bar (raised + enlarged; movable)
        Rect hp = Place(0, new Rect(20f, UI.H - 110f, 520f, 48f));
        Bar(hp.x, hp.y, hp.width, hp.height, Health / MaxHealth, new Color(0.2f, 0.8f, 0.25f), Lang.T($"ХП {Mathf.RoundToInt(Health)}", $"HP {Mathf.RoundToInt(Health)}"));

        // Top-down build mode: persistent hint banner across the top of the screen.
        if (topBuild)
        {
            var th = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GUI.color = new Color(0.55f, 0.95f, 0.55f);
            GUI.Label(new Rect(cx - 520f, 14f, 1040f, 26f),
                Lang.T("СТРОЙКА СВЕРХУ:  ЛКМ — веди линию,  R — поворот,  WASD — двигать вид,  ПРОБЕЛ — прыжок,  T / Esc — выйти",
                       "TOP-DOWN BUILD:  LMB — drag a line,  R — rotate,  WASD — pan,  SPACE — jump,  T / Esc — exit"), th);
            GUI.color = Color.white;
        }

        // Bottom-centre metal readout (above the tool line)
        Rect metal = Place(1, new Rect(cx - 170f, UI.H - 92f, 340f, 40f));
        Panel(metal);
        GUI.color = UISettings.Accent;
        GUI.Label(new Rect(metal.x, metal.y + 2f, 340f, 36f), Lang.T($"МЕТАЛЛ: {Metal}", $"METAL: {Metal}"), Ctr);
        GUI.color = Color.white;

        // Speedometer above the resource readouts (shows your bhop momentum).
        float spdY = (Refinery.All.Count > 0) ? metal.y - 66f : metal.y - 24f;
        var spdSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.55f, 0.9f, 1f);
        GUI.Label(new Rect(metal.x, spdY, 340f, 20f), Lang.T($"скорость: {hVel.magnitude:0.0} м/с", $"speed: {hVel.magnitude:0.0} m/s"), spdSt);
        GUI.color = Color.white;

        // Oil readout (only once refineries exist on the map — default mode).
        if (Refinery.All.Count > 0)
        {
            Rect oil = Place(5, new Rect(cx - 170f, UI.H - 134f, 340f, 36f));
            Panel(oil);
            GUI.color = Oil > 0 ? new Color(1f, 0.85f, 0.35f) : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(oil.x, oil.y + 2f, 340f, 32f), Lang.T($"НЕФТЬ: {Oil}/{OilMax}", $"OIL: {Oil}/{OilMax}"), Ctr);
            GUI.color = Color.white;
        }

        // Bottom-center tool line (smaller font + centred so the longer RU text fits)
        string toolLine;
        if (tool == Tool.Gun) toolLine = Lang.T($"[1] ПУШКА {Guns[gunTier].name}   патроны {ammo}/{Guns[gunTier].mag}", $"[1] GUN {Guns[gunTier].name}   ammo {ammo}/{Guns[gunTier].mag}");
        else if (tool == Tool.Build && IsDragBuild(SelectedBuild)) toolLine = Lang.T($"[2] {BName(SelectedBuild)} ({BCost(SelectedBuild)}/звено)   клик = 1 шт   зажми ЛКМ 0.5с и веди = ряд/линия{(IsPathBuild(SelectedBuild) ? "   R=повернуть ось" : "")}   ПКМ=продать  X=снести класс  Q=меню", $"[2] {BName(SelectedBuild)} ({BCost(SelectedBuild)}/link)   click = 1   hold LMB 0.5s and drag = row/line{(IsPathBuild(SelectedBuild) ? "   R=rotate axis" : "")}   RMB=sell  X=delete class  Q=menu");
        else if (tool == Tool.Build) toolLine = Lang.T($"[2] СТРОЙКА {BName(SelectedBuild)} ({BCost(SelectedBuild)})   ЛКМ=ставить  E=улучшить  ПКМ=продать  X=снести класс  Q=меню", $"[2] BUILD {BName(SelectedBuild)} ({BCost(SelectedBuild)})   LMB=place  E=upgrade  RMB=sell  X=delete class  Q=menu");
        else if (tool == Tool.Wrench) toolLine = Lang.T("[3] КЛЮЧ — ближний бой + починка", "[3] WRENCH — melee + repair");
        else toolLine = Lang.T("[4] ЛОПАТА — зажми ЛКМ чтобы копать", "[4] SHOVEL — hold LMB to dig");
        toolLine += Lang.T("     колесо=оружие   СКМ=перенести постройку", "     wheel=weapon   MMB=move building");
        Rect toolR = Place(3, new Rect(8f, UI.H - 44f, UI.W - 16f, 34f));
        Panel(toolR);
        GUI.color = Color.white; GUI.Label(new Rect(toolR.x, toolR.y + 1f, toolR.width, 30f), toolLine, Tool16);

        // Top-center wave banner (hidden in PvP, tutorial and ZvZ — no normal waves there)
        var gm = GameManager.Instance;
        if (gm != null && !GameRoot.IsPvp && !GameRoot.IsTutorial && !GameRoot.IsZvZ)
        {
            if (gm.IsPrep)
            {
                Panel(new Rect(cx - 400f, 8f, 800f, 64f));
                GUI.color = Color.cyan;
                GUI.Label(new Rect(cx - 400f, 12f, 800f, 28f), Lang.T("ПОДГОТОВКА — стройте базу!", "PREP — build your base!"), Line24);
                GUI.Label(new Rect(cx - 400f, 42f, 800f, 24f), GameRoot.Infinite ? Lang.T($"след. волна: {gm.WaveNumber + 1}  (бесконечный режим)", $"next wave: {gm.WaveNumber + 1}  (endless mode)") : Lang.T($"след. волна: {gm.WaveNumber + 1}/{gm.EvacWave} волн", $"next wave: {gm.WaveNumber + 1}/{gm.EvacWave} waves"), Sm);
                if (GameRoot.Sandbox)
                    GUI.Label(new Rect(cx - 300f, 78f, 600f, 64f), Lang.T("ПЕСОЧНИЦА — J начинает волну", "SANDBOX — J starts a wave"), Line24);
                else
                    GUI.Label(new Rect(cx - 300f, 78f, 600f, 64f), Lang.T($"{Mathf.CeilToInt(gm.PhaseTimeLeft)}с", $"{Mathf.CeilToInt(gm.PhaseTimeLeft)}s"), Big52);

                // Pulsing prompts during prep (cached styles — no per-frame GUIStyle alloc).
                float pulse = 0.6f + 0.4f * Mathf.PingPong(Time.unscaledTime * 1.5f, 1f);
                if (!builtSomething) // the Q hint goes away once you've built your first thing
                {
                    GUI.color = new Color(1f, 0.9f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 350f, 146f, 700f, 32f), Lang.T("нажмите Q для стройки", "press Q to build"), Line24);
                }

                // "Press J when ready" — skips the prep. Hidden for co-op clients (the host owns the waves).
                if (!NetClient)
                {
                    GUI.color = new Color(1f, 0.3f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 380f, 180f, 760f, 28f),
                        GameRoot.Sandbox ? Lang.T("нажмите J, чтобы запустить волну (песочница)", "press J to launch a wave (sandbox)")
                                         : Lang.T("если вы готовы — нажмите J, чтобы начать волну", "if you're ready — press J to start the wave"), Sm);
                }
                if (GameRoot.Hardcore)
                {
                    GUI.color = new Color(1f, 0.7f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 400f, 208f, 800f, 26f), Lang.T("хардкор: постройки дороже, раздатчик отдаёт лишь накопленный металл", "hardcore: buildings cost more, the dispenser only gives out accumulated metal"), Sm);
                }
                GUI.color = Color.white;
            }
            else
            {
                Panel(new Rect(cx - 360f, 8f, 720f, 62f));
                GUI.color = new Color(1f, 0.55f, 0.35f);
                GUI.Label(new Rect(cx - 360f, 11f, 720f, 30f), Lang.T($"ВОЛНА {gm.WaveNumber}   зомби: {gm.ZombiesLeft}", $"WAVE {gm.WaveNumber}   zombies: {gm.ZombiesLeft}"), Line24);
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                GUI.Label(new Rect(cx - 360f, 40f, 720f, 24f), Lang.T("чтобы сдаться нажмите K (заберёт всю нефть и металл)", "press K to surrender (takes all oil & metal)"), Sm);
                GUI.color = Color.white;
            }
        }

        // Enemy air-raid warning (wave 24+): flashing banner while raider bombers are overhead.
        bool airRaid = false;
        foreach (var bmb in Bomber.All) if (bmb != null && bmb.enemy) { airRaid = true; break; }
        if (airRaid)
        {
            Panel(new Rect(cx - 320f, 74f, 640f, 32f));
            GUI.color = new Color(1f, 0.3f, 0.22f, 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 3f, 1f));
            GUI.Label(new Rect(cx - 320f, 77f, 640f, 26f), Lang.T("ВОЗДУШНЫЙ НАЛЁТ — сбивайте самолёты РЗК!", "AIR RAID — shoot down the planes with SAM!"), Line24);
            GUI.color = Color.white;
        }

        // Air-strike targeting computer hint — only during a WAVE (in prep the timer HUD owns the
        // top-centre, so showing it there overlapped the text).
        // Air-strike computer hint — tidy top-LEFT corner (that area is otherwise empty, so it
        // never collides with the wave banner, oil/metal readouts or the aimed-building panel).
        if (AirStrike.AnyOnline() && gm != null && !gm.IsPrep)
        {
            Panel(new Rect(12f, 100f, 470f, 28f));
            GUI.color = AirStrike.HasDesignation ? new Color(1f, 0.55f, 0.35f) : new Color(0.9f, 0.85f, 0.8f);
            var las = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(20f, 103f, 456f, 22f),
                AirStrike.HasDesignation ? Lang.T("АВИАУДАР наведён на сектор", "AIR STRIKE aimed at the sector") : Lang.T("G — навести авиаудар на сектор", "G — aim the air strike at a sector"), las);
            GUI.color = Color.white;
        }

        // Driving hint
        if (vehicle != null)
        {
            Panel(new Rect(cx - 320f, UI.H - 96f, 640f, 40f));
            GUI.color = new Color(0.7f, 0.95f, 1f);
            GUI.Label(new Rect(cx - 320f, UI.H - 92f, 640f, 32f), Lang.T("WASD — ехать       F — выйти", "WASD — drive       F — exit"), Line24);
            GUI.color = Color.white;
        }

        // Transient toast (sell/delete feedback)
        if (Time.time < toastUntil && !string.IsNullOrEmpty(toast))
        {
            Panel(new Rect(cx - 340f, UI.H - 150f, 680f, 38f));
            GUI.color = new Color(1f, 0.92f, 0.7f);
            GUI.Label(new Rect(cx - 340f, UI.H - 146f, 680f, 30f), toast, Line24);
            GUI.color = Color.white;
        }

        // Building info (3 elements) when aiming at one
        if (aimed != null)
        {
            float pw = 460f, px = cx - pw * 0.5f;

            // Compute the panel height first so the whole thing can be anchored near the BOTTOM
            // (just above the oil/metal readouts) instead of covering the centre of the screen.
            bool reserveUpgrade = aimed.UsesReserve && aimed.Reserve >= aimed.ReserveMax && aimed.CanUpgrade;
            bool twoBars = !aimed.Building && (aimed.IsFunding || (aimed.CanUpgrade && !aimed.UsesReserve) || reserveUpgrade);
            bool fundingFour = aimed.IsFunding && aimed.FundingRequired > 0 && aimed.OilRequired > 0;
            float panelH = fundingFour ? 140f : (twoBars ? 116f : 92f);
            float py = UI.H - 150f - panelH;

            // Description of the building you're LOOKING AT (bound to its type, not the menu cursor).
            if (aimed.Type >= 0 && aimed.Type < BuildDescriptions.Length)
            {
                float dh2 = 74f, dy2 = py - 12f - dh2;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(px - 8f, dy2, pw + 16f, dh2), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.9f, 0.55f);
                var dt2 = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                var db2 = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft };
                GUI.Label(new Rect(px, dy2 + 5f, pw, 22f), BName(aimed.Type), dt2);
                GUI.color = Color.white;
                GUI.Label(new Rect(px, dy2 + 28f, pw, dh2 - 32f), BDesc(aimed.Type), db2);
            }

            Panel(new Rect(px - 8f, py - 8f, pw + 16f, panelH));
            GUI.color = Color.white;
            GUI.Label(new Rect(px, py, pw, 22f), Lang.T($"{BName(aimed.Type)}  -  УР {aimed.Level}  -  ваше", $"{BName(aimed.Type)}  -  LVL {aimed.Level}  -  yours"), Sm);
            Bar(px, py + 24f, pw, 20f, aimed.Health / aimed.MaxHealth, new Color(0.2f, 0.8f, 0.25f), Lang.T($"{Mathf.Max(0, Mathf.RoundToInt(aimed.Health))} / {Mathf.RoundToInt(aimed.MaxHealth)} ХП", $"{Mathf.Max(0, Mathf.RoundToInt(aimed.Health))} / {Mathf.RoundToInt(aimed.MaxHealth)} HP"));

            if (aimed.Building)
            {
                Bar(px, py + 48f, pw, 20f, 1f, new Color(0.9f, 0.7f, 0.2f), Lang.T("строится...", "building..."));
            }
            else if (aimed.IsFunding)
            {
                // Funding bars, top to bottom: (metal if needed) → (oil if needed) → cooldown.
                bool hasMetal = aimed.FundingRequired > 0, hasOil = aimed.OilRequired > 0;
                bool metalDone = aimed.FundingPaid >= aimed.FundingRequired;
                float row = py + 48f;

                if (hasMetal)
                {
                    float f = (float)aimed.FundingPaid / Mathf.Max(1, aimed.FundingRequired);
                    int chunk = Mathf.Min(Metal, Mathf.Min(aimed.FundChunk, aimed.FundingRemaining));
                    string mtxt = metalDone ? Lang.T($"металл готов ({aimed.FundingRequired})", $"metal ready ({aimed.FundingRequired})")
                        : Metal > 0 ? Lang.T($"E: вложить +{chunk}   ({aimed.FundingPaid}/{aimed.FundingRequired})", $"E: invest +{chunk}   ({aimed.FundingPaid}/{aimed.FundingRequired})")
                        : Lang.T($"нужен металл   ({aimed.FundingPaid}/{aimed.FundingRequired})", $"need metal   ({aimed.FundingPaid}/{aimed.FundingRequired})");
                    Bar(px, row, pw, 20f, f, metalDone ? new Color(0.3f, 0.6f, 0.45f) : new Color(0.4f, 0.8f, 1f), mtxt);
                    row += 24f;
                }
                if (hasOil)
                {
                    float of = (float)aimed.OilPaid / Mathf.Max(1, aimed.OilRequired);
                    int ochunk = Mathf.Min(Oil, Mathf.Min(OilFundChunk, aimed.OilRemaining));
                    string otxt = aimed.OilPaid >= aimed.OilRequired ? Lang.T($"нефть готова ({aimed.OilRequired})", $"oil ready ({aimed.OilRequired})")
                        : (hasMetal && !metalDone) ? Lang.T($"потом нефть   ({aimed.OilPaid}/{aimed.OilRequired})", $"oil next   ({aimed.OilPaid}/{aimed.OilRequired})")
                        : Oil > 0 ? Lang.T($"E: нефть +{ochunk}   ({aimed.OilPaid}/{aimed.OilRequired})", $"E: oil +{ochunk}   ({aimed.OilPaid}/{aimed.OilRequired})")
                        : Lang.T($"нужна нефть с НПЗ   ({aimed.OilPaid}/{aimed.OilRequired})", $"need oil from a refinery   ({aimed.OilPaid}/{aimed.OilRequired})");
                    Bar(px, row, pw, 20f, of, new Color(1f, 0.8f, 0.3f), otxt);
                    row += 24f;
                }
                if (aimed.UpgradeReadyIn > 0f)
                    Bar(px, row, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), Lang.T($"перезаряд {aimed.UpgradeReadyIn:0.0}с", $"reload {aimed.UpgradeReadyIn:0.0}s"));
                else
                    Bar(px, row, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), Lang.T("готово (E)", "ready (E)"));
            }
            else if (aimed.UsesReserve)
            {
                // Funded special weapon: fill its ammo reserve; once full, E upgrades it.
                // Super-weapons charge from OIL (ReserveIsOil); hardcore turrets from metal.
                bool oilFuel = aimed.ReserveIsOil;
                int wallet = oilFuel ? Oil : Metal;
                string needRes = oilFuel ? Lang.T("нужна нефть", "need oil") : Lang.T("нужен металл", "need metal");
                float rf = (float)aimed.Reserve / Mathf.Max(1, aimed.ReserveMax);
                if (aimed.Reserve < aimed.ReserveMax)
                {
                    int load = Mathf.Min(wallet, Mathf.Min(ReserveLoadChunk, aimed.ReserveMax - aimed.Reserve));
                    string txt = wallet > 0
                        ? Lang.T($"E: зарядить +{load}   (заряд {aimed.Reserve}/{aimed.ReserveMax})", $"E: load +{load}   (charge {aimed.Reserve}/{aimed.ReserveMax})")
                        : $"{needRes}   " + Lang.T($"(заряд {aimed.Reserve}/{aimed.ReserveMax})", $"(charge {aimed.Reserve}/{aimed.ReserveMax})");
                    Bar(px, py + 48f, pw, 20f, rf, oilFuel ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.8f, 1f), txt);
                }
                else if (aimed.CanUpgrade)
                {
                    float invFrac2 = (float)aimed.Invested / aimed.UpgradeCost;
                    string txt = wallet > 0
                        ? Lang.T($"E: апгрейд +{Mathf.Min(wallet, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})", $"E: upgrade +{Mathf.Min(wallet, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})")
                        : Lang.T($"ПОЛНО — {needRes} на апгрейд", $"FULL — {needRes} to upgrade");
                    Bar(px, py + 48f, pw, 20f, invFrac2, new Color(0.2f, 0.7f, 0.9f), txt);

                    // Bar 3: cooldown before the next investment (same gate as normal upgrades).
                    if (aimed.UpgradeReadyIn > 0f)
                        Bar(px, py + 72f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), Lang.T($"перезаряд {aimed.UpgradeReadyIn:0.0}с", $"reload {aimed.UpgradeReadyIn:0.0}s"));
                    else
                        Bar(px, py + 72f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), Lang.T("готово (E)", "ready (E)"));
                }
                else
                {
                    Bar(px, py + 48f, pw, 20f, 1f, new Color(0.3f, 0.6f, 0.4f), Lang.T($"заряд {aimed.Reserve}/{aimed.ReserveMax}  (МАКС)", $"charge {aimed.Reserve}/{aimed.ReserveMax}  (MAX)"));
                }
            }
            else if (aimed.CanUpgrade)
            {
                // Bar 2: investment progress toward the next level.
                float invFrac = (float)aimed.Invested / aimed.UpgradeCost;
                string invTxt = Metal > 0
                    ? Lang.T($"E: вложить +{Mathf.Min(Metal, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})", $"E: invest +{Mathf.Min(Metal, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})")
                    : Lang.T($"нужен металл   ({aimed.Invested}/{aimed.UpgradeCost})", $"need metal   ({aimed.Invested}/{aimed.UpgradeCost})");
                Bar(px, py + 48f, pw, 20f, invFrac, new Color(0.2f, 0.7f, 0.9f), invTxt);

                // Bar 3: cooldown before the next investment.
                if (aimed.UpgradeReadyIn > 0f)
                    Bar(px, py + 72f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), Lang.T($"перезаряд {aimed.UpgradeReadyIn:0.0}с", $"reload {aimed.UpgradeReadyIn:0.0}s"));
                else
                    Bar(px, py + 72f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), Lang.T("готово (E)", "ready (E)"));
            }
            else if (aimed.NeedsRepair)
            {
                Bar(px, py + 48f, pw, 20f, aimed.Health / aimed.MaxHealth, new Color(0.2f, 0.8f, 0.4f), Lang.T("E: чинить", "E: repair"));
            }
            else
            {
                Bar(px, py + 48f, pw, 20f, 1f, new Color(0.4f, 0.4f, 0.45f), Lang.T("МАКС УРОВЕНЬ", "MAX LEVEL"));
            }
        }

        if (GameRoot.BaseLost && GameRoot.IsPlaying)
        {
            // Base lifeline (the critical dispenser) destroyed → game over.
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var big = new GUIStyle(GUI.skin.label) { fontSize = 70, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.9f, 0.25f, 0.2f);
            GUI.Label(new Rect(0f, cy - 200f, UI.W, 100f), Lang.T("БАЗА ПАЛА", "BASE HAS FALLEN"), big);
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.9f, 0.55f, 0.3f);
            GUI.Label(new Rect(0f, cy - 110f, UI.W, 40f), Lang.T("раздатчик уничтожен — игра окончена", "dispenser destroyed — game over"), sub);
            GUI.color = Color.white;

            var mbtn = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
            GUI.backgroundColor = new Color(0.75f, 0.35f, 0.32f);
            if (GUI.Button(new Rect(cx - 180f, cy + 10f, 360f, 84f), Lang.T("В МЕНЮ", "TO MENU"), mbtn))
                { if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu(); }
            GUI.backgroundColor = Color.white;
            return; // defeat screen owns the view
        }

        if (IsDead)
        {
            // Full death screen: dim everything, big title, two big buttons.
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(new Rect(0f, 0f, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var dead = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.85f, 0.2f, 0.18f);
            GUI.Label(new Rect(0f, cy - 200f, UI.W, 100f), Lang.T("ВЫ ПОГИБЛИ", "YOU DIED"), dead);
            if (GameRoot.Hardcore)
            {
                var hc = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.color = new Color(0.9f, 0.55f, 0.2f);
                GUI.Label(new Rect(0f, cy - 110f, UI.W, 36f), Lang.T("ХАРДКОР — прогресс сброшен", "HARDCORE — progress reset"), hc);
            }
            GUI.color = Color.white;

            var big = new GUIStyle(GUI.skin.button) { fontSize = 34, fontStyle = FontStyle.Bold };
            float bw = 360f, bh = 90f, gap = 40f;
            float bx = cx - bw - gap * 0.5f;

            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.35f);
            string firstBtn = GameRoot.Hardcore ? Lang.T("ЗАНОВО (волна 1)", "RESTART (wave 1)") : Lang.T("РЕСПАВН", "RESPAWN");
            if (GUI.Button(new Rect(bx, cy - 10f, bw, bh), firstBtn, big))
            {
                if (GameRoot.Hardcore) { if (GameRoot.Instance != null) GameRoot.Instance.RestartRun(); }
                else Respawn();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            GUI.backgroundColor = new Color(0.75f, 0.35f, 0.32f);
            if (GUI.Button(new Rect(cx + gap * 0.5f, cy - 10f, bw, bh), Lang.T("ВЫЙТИ", "EXIT"), big))
            {
                if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu();
            }
            GUI.backgroundColor = Color.white;
        }

        // GMod-style spawn menu (Q): dim overlay + clickable grid of buildables.
        if (buildMenuOpen)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Each category is a header followed by its buttons, wrapping to a new row
            // every perRow items so wide categories don't run off-screen.
            const int perRow = 6;
            float bw = 138f, bh = 76f, gap = 10f, headH = 24f, sectGap = 10f;
            float gridW = perRow * bw + (perRow - 1) * gap;
            float leftX = cx - gridW * 0.5f;

            // Total height = sum of each category's header + its wrapped rows.
            float blockH = 0f;
            foreach (var items in BuildCategoryItems)
            {
                int rows = Mathf.CeilToInt(items.Length / (float)perRow);
                blockH += headH + rows * bh + (rows - 1) * gap + sectGap;
            }
            blockH -= sectGap;
            float startY = cy - blockH * 0.5f;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GUI.Label(new Rect(cx - 460f, startY - 44f, 920f, 30f), Lang.T("МЕНЮ ПОСТРОЙКИ   —   клик для выбора   (отпусти Q чтобы закрыть)", "BUILD MENU   —   click to select   (release Q to close)"), title);

            var head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            Vector2 mouse = Event.current.mousePosition / UI.Scale; // GUI.matrix isn't applied to the event pos
            int hoverItem = -1;

            float y = startY;
            for (int ci = 0; ci < BuildCategories.Length; ci++)
            {
                GUI.color = new Color(1f, 0.88f, 0.5f);
                GUI.Label(new Rect(leftX + 4f, y, gridW, headH), BCat(ci), head);
                GUI.color = Color.white;

                var items = BuildCategoryItems[ci];
                float ry = y + headH;
                for (int j = 0; j < items.Length; j++)
                {
                    int i = items[j];
                    int row = j / perRow, col = j % perRow;
                    var rect = new Rect(leftX + col * (bw + gap), ry + row * (bh + gap), bw, bh);
                    if (rect.Contains(mouse)) hoverItem = i;
                    // Tutorial: glow a pulsing cyan border behind the build the current step asks for.
                    if (TutorialManager.HighlightBuild == i)
                    {
                        float pb = 4f + 3f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
                        GUI.color = new Color(0.3f, 1f, 1f, 0.95f);
                        GUI.DrawTexture(new Rect(rect.x - pb, rect.y - pb, rect.width + 2f * pb, rect.height + 2f * pb), Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }
                    bool afford = Metal >= BCost(i);
                    GUI.backgroundColor = (i == SelectedBuild) ? Color.yellow : (afford ? new Color(0.4f, 0.6f, 0.85f) : new Color(0.5f, 0.35f, 0.35f));
                    if (GUI.Button(rect, Lang.T($"{BName(i)}\n{BCost(i)} мет.", $"{BName(i)}\n{BCost(i)} metal"), btn))
                    {
                        SelectedBuild = i;
                        SetTool(Tool.Build);
                        // menu stays open until you release Q (GMod-style)
                    }
                }
                int rowsUsed = Mathf.CeilToInt(items.Length / (float)perRow);
                y += headH + rowsUsed * bh + (rowsUsed - 1) * gap + sectGap;
            }
            GUI.backgroundColor = Color.white;

            // Hover tooltip: name + cost + "what it is / how it works".
            if (hoverItem >= 0 && hoverItem < BuildDescriptions.Length)
            {
                float dw = gridW, dx = leftX, dh = 92f, dy = y + 6f;
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(new Rect(dx, dy, dw, dh), Texture2D.whiteTexture);
                GUI.color = Color.white;
                var dTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                var dBody = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, alignment = TextAnchor.UpperLeft };
                GUI.color = new Color(1f, 0.9f, 0.55f);
                GUI.Label(new Rect(dx + 14f, dy + 6f, dw - 28f, 26f), Lang.T($"{BName(hoverItem)}   —   {BCost(hoverItem)} металла", $"{BName(hoverItem)}   —   {BCost(hoverItem)} metal"), dTitle);
                GUI.color = Color.white;
                GUI.Label(new Rect(dx + 14f, dy + 34f, dw - 28f, dh - 40f), BDesc(hoverItem), dBody);
            }
        }
    }
}
