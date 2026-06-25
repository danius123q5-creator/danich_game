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
    public float MaxHealth = 200f;
    public float RespawnDelay = 3f;

    // Hardcore caps the wallet lower and makes builds pricier. After wave 12 the cap grows
    // each wave (+30/wave) so late-game super-weapons stay affordable.
    public static int MetalMax
    {
        get
        {
            int cap = GameRoot.Hardcore ? 170 : 300;
            var gm = GameManager.Instance;
            if (gm != null && gm.WaveNumber > 12) cap += (gm.WaveNumber - 12) * 30;
            return cap;
        }
    }
    const int ReserveLoadChunk = 100; // metal loaded into a special weapon's reserve per E press
    const int OilFundChunk = 50;      // oil poured into a super-weapon's funding per E press

    // Build cost, marked up in hardcore.
    static int BCost(int i) => GameRoot.Hardcore ? Mathf.RoundToInt(BuildCosts[i] * 1.5f) : BuildCosts[i];

    [HideInInspector] public float Health;
    [HideInInspector] public int Metal = 250;
    public const int OilMax = 500;                 // personal oil carry capacity (from refineries)
    [HideInInspector] public int Oil = 500;        // oil carried, poured into super-weapons (start with a base stock)
    [HideInInspector] public int Score = 0;
    [HideInInspector] public int Deaths = 0; // how many times the player has died (HUD counter)
    [HideInInspector] public int SelectedBuild = 0;
    [HideInInspector] public bool Disarmed; // evac cutscene: no weapons, just run

    public bool IsDead => Health <= 0f;

    CharacterController cc;
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
    float nextBonus;          // 10-minute cooldown on the +100 metal bonus
    const float BonusCooldown = 600f;
    GameObject viewmodel;
    GameObject playerBody;
    Vector3 vmBasePos;     // viewmodel rest position; recoil animates around it
    float gunRecoil;       // 0..1 recoil kick, decays each frame
    float gunHeat;         // 0..1 muzzle heat glow, decays each frame
    GameObject gunMuzzle;  // barrel tip — glows red-hot when firing

    static readonly string[] BuildNames = { "ТУРЕЛЬ", "РАЗДАТЧИК", "РАСТЯЖКА", "СТЕНА", "ДВЕРЬ", "МОСТ", "ЛЕСТНИЦА", "ФУГАС", "КОЛЮЧКА", "АВИАУДАР", "ТЕСЛА", "АРТИЛЛЕРИЯ", "МОСТ-УГОЛ", "МОСТ-Т", "МОСТ-КРЕСТ", "ЗЕНИТКА", "ДЛ. СТЕНА", "ВЫС. СТЕНА", "МАШИНА", "РПГ", "ВЕРТ. ЛЕСТНИЦА", "СТОП-ПУШКА", "ОРБ. СТАНЦИЯ", "СМОТР. БАШНЯ", "ЛЕЗВИЯ", "РАКЕТ. ШАХТА", "ПЛАТФОРМА" };
    static readonly int[] BuildCosts = { 130, 100, 60, 25, 40, 35, 30, 30, 20, 250, 200, 250, 40, 45, 50, 120, 45, 35, 150, 40, 30, 136, 200, 90, 450, 550, 220 };

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
        "Авиаудар (супероружие): копи металл + нефть с НПЗ (E), затем вызывает удары по толпе на всю карту.",
        "Катушка Тесла (супероружие): копи металл + нефть с НПЗ (E). Бьёт молнией по ближним зомби, тратит металл из резерва.",
        "Артиллерия (супероружие): копи металл + нефть с НПЗ (E). Фугасы по площади на всю карту, наводится на цель.",
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
        "Орбитальная станция: блок управления (копи 3000 металла + нефть с НПЗ, E). Когда готов — в небе появляется станция и циклит 3 атаки: точные лазеры со взрывом, выжигающий луч (ползёт от зомби к зомби) и тройная призма (3 луча крутятся вокруг базы). Тратит металл из своего бака — заряжай E.",
        "Смотровая башня (20 м): залезь по лестнице через люк на площадку наверху — отличная точка для стрельбы, зомби туда не достанут.",
        "Лезвия: крутящийся ротор рубит всех зомби рядом несколько раз в секунду. Работает как турель — сама, без зарядки и расхода металла. Дорогая в постройке.",
        "Ракетная шахта: ждёт, пока соберётся толпа (3+ зомби), и пускает ракету в самую гущу — мощный взрыв (урон 350). Работает как турель, без расхода металла. Дорогая.",
        "Платформа: огромная площадка на 4 толстых столбах. Залезь по лестнице наверх — целый этаж под турели и линию обороны, зомби туда не достанут.",
    };

    // Build-menu sections: each holds the build-type indices shown under that header.
    static readonly string[] BuildCategories = { "СТРОИТЕЛЬНОЕ", "ОБОРОНА", "ОСТАЛЬНОЕ" };
    static readonly int[][] BuildCategoryItems =
    {
        new[] { 3, 16, 17, 4, 6, 20, 23, 26, 5 }, // WALL, LONG/TALL WALL, DOOR, STAIRS, LADDER, WATCHTOWER, BIG PLATFORM, BRIDGE
        new[] { 0, 19, 1, 2, 7, 8, 15, 24, 25 },  // SENTRY, RPG, DISPENSER, MINE, LANDMINE, BARBED WIRE, AA TURRET, BLADES, MISSILE SILO
        new[] { 9, 10, 11, 21, 22, 18 },          // AIR STRIKE, TESLA, ARTILLERY, FREEZE, ORBITAL, CAR
    };

    struct GunStats { public string name; public float dmg; public float rate; public int mag; }
    static readonly GunStats[] Guns =
    {
        new GunStats { name = "ПИСТОЛЕТ", dmg = 22f,  rate = 0.35f, mag = 12 },
        new GunStats { name = "ПП",       dmg = 16f,  rate = 0.09f, mag = 30 },
        new GunStats { name = "ВИНТОВКА", dmg = 34f,  rate = 0.14f, mag = 25 },
        new GunStats { name = "КАРАБИН",  dmg = 46f,  rate = 0.11f, mag = 30 },
        new GunStats { name = "ПУЛЕМЁТ",  dmg = 30f,  rate = 0.07f, mag = 60 },
        new GunStats { name = "РЕЛЬСОТРОН", dmg = 120f, rate = 0.50f, mag = 10 },
    };

    void Awake()
    {
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
        VisualFx.EnablePostFx(cam); // bloom / tonemapping / grading from the global volume

        BuildViewmodel();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!GameRoot.IsPlaying) return; // frozen in menu / pause

        SyncGunToWave();

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
            Cursor.lockState = buildMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = buildMenuOpen;
        }
        if (buildMenuOpen)
        {
            if (preview != null) preview.SetActive(false);
            if (rangeSphere != null) rangeSphere.SetActive(false);
            SetAimed(null);
            return; // frozen while the menu is held open; clicks handled in OnGUI
        }

        Look();
        Move();

        // Disarmed (evac cutscene): keep mouselook + movement, but no weapons/tools/HUD targeting.
        if (Disarmed)
        {
            if (viewmodel != null && viewmodel.activeSelf) viewmodel.SetActive(false);
            SetAimed(null);
            return;
        }

        SetAimed(RaycastNoSelf(30f, out RaycastHit aimHit) ? aimHit.collider.GetComponentInParent<Buildable>() : null);

        // Mouse wheel switches weapon/tool (classic FPS feel).
        float sw = Input.GetAxis("Mouse ScrollWheel");
        if (sw > 0.01f) CycleTool(1);
        else if (sw < -0.01f) CycleTool(-1);

        // Number keys 1-9 pick a building type and switch to the build tool. The
        // special weapons (10+) live past the number row, so they're picked from the Q menu.
        int hotkeys = Mathf.Min(BuildNames.Length, 9);
        for (int k = 0; k < hotkeys; k++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + k)) { SelectedBuild = k; SetTool(Tool.Build); }

        // Middle mouse button (СКМ) = +100 metal bonus, once per 10 minutes.
        if (Input.GetMouseButtonDown(2) && Time.time >= nextBonus)
        {
            AddMetal(100);
            nextBonus = Time.time + BonusCooldown;
        }

        switch (tool)
        {
            case Tool.Gun:
                if (Input.GetMouseButton(0)) FireGun();
                break;
            case Tool.Build:
                if (Input.GetMouseButtonDown(0)) BuildPrimary();
                if (Input.GetMouseButtonDown(1)) SellBuild();
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


    void Look()
    {
        float mx = Input.GetAxis("Mouse X") * MouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * MouseSensitivity;
        transform.Rotate(0f, mx, 0f);
        pitch = Mathf.Clamp(pitch - my, -85f, 85f);
        cam.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void Move()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = transform.right * h + transform.forward * v;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

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

        // Jump with coyote time + an input buffer so a press never gets eaten by the
        // frame where the controller is still settling on the ground ("works every other time").
        bool grounded = cc.isGrounded;
        coyote = grounded ? 0.1f : coyote - Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.Space)) jumpBuffer = 0.1f;
        else jumpBuffer -= Time.deltaTime;

        if (grounded && vSpeed < 0f) vSpeed = -1f; // stick to the ground while settling

        if (jumpBuffer > 0f && coyote > 0f && !crouch)
        {
            vSpeed = JumpSpeed;
            jumpBuffer = 0f;
            coyote = 0f; // consume, so one press = one jump
        }

        vSpeed -= Gravity * Time.deltaTime;
        cc.Move((dir * speed + Vector3.up * vSpeed) * Time.deltaTime);
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
        int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _rayHits, dist);
        // Pick the NEAREST hit that isn't the player's own collider (single pass, no sort).
        float bestD = float.MaxValue; bool found = false; best = default;
        for (int i = 0; i < n; i++)
        {
            if (_rayHits[i].collider.GetComponentInParent<PlayerController>() == this) continue;
            if (_rayHits[i].distance < bestD) { bestD = _rayHits[i].distance; best = _rayHits[i]; found = true; }
        }
        return found;
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
        int cost = BCost(SelectedBuild);
        if (Metal < cost) return;
        var rot = Quaternion.Euler(0f, BuildYaw(), 0f);
        if (NetClient)
        {
            LanManager.Instance.SendBuildPlace(SelectedBuild, hit.point, BuildYaw());
            AddMetal(-cost);
            builtSomething = true;
        }
        else if (Buildable.Create(SelectedBuild, hit.point, rot, this) != null) { AddMetal(-cost); builtSomething = true; }
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
                AddMetal(b.BuildCost);
                if (NetClient) LanManager.Instance.SendBuildAction(b.NetId, 4, 0);
                else Destroy(b.gameObject);
            }
        }
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
        else if (b.UsesReserve)
        {
            if (b.Reserve < b.ReserveMax && Metal > 0)
            {
                int load = Mathf.Min(Metal, Mathf.Min(ReserveLoadChunk, b.ReserveMax - b.Reserve));
                if (load <= 0) return;
                if (NetClient) { AddMetal(-load); LanManager.Instance.SendBuildAction(b.NetId, 2, load); }
                else AddMetal(-b.Refill(load));
            }
            else if (b.CanUpgrade && Metal > 0)
            {
                int amount = Mathf.Min(Metal, b.InvestAmount);
                if (NetClient) { if (b.UpgradeReadyIn <= 0f) { AddMetal(-amount); b.MarkNetCooldown(); LanManager.Instance.SendBuildAction(b.NetId, 0, amount); } }
                else if (b.Invest(amount)) AddMetal(-amount);
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
                    pos = hit.point + Vector3.up * 0.02f; // terrain or on top of a full-health structure (bridge)
                }
            }
        }

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

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Max(0f, Health - amount);
        if (IsDead) { deathTime = Time.time; Deaths++; } // counted once per death (early-out above guards re-entry)
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
    // Apply a movable element's saved offset; in layout-edit mode, frame it and drag it.
    Rect Place(int id, Rect baseRect)
    {
        Vector2 off = UISettings.Offsets[id];
        Rect r = new Rect(baseRect.x + off.x, baseRect.y + off.y, baseRect.width, baseRect.height);
        if (UISettings.EditLayout)
        {
            var e = Event.current;
            Vector2 m = e.mousePosition / UI.Scale;                 // GUI.matrix isn't applied to the event pos
            if (e.type == EventType.MouseDown && r.Contains(m)) { _dragId = id; e.Use(); }
            else if (_dragId == id && e.type == EventType.MouseDrag) { UISettings.Offsets[id] += e.delta / UI.Scale; e.Use(); }
            else if (_dragId == id && e.type == EventType.MouseUp) { _dragId = -1; e.Use(); }

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
            GUI.Label(new Rect(gx - 80f, gy - 10f, 160f, 20f), ammo <= 0 ? "НЕТ ПАТРОНОВ" : "патроны " + ammo, _ammoStyle);
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
            if (!rf.Captured) { state = rf.Capture > 0f ? $"ЗАХВАТ {Mathf.RoundToInt(rf.Capture / Refinery.CaptureTime * 100f)}%" : "НЕЙТРАЛЕН"; sc = new Color(0.8f, 0.8f, 0.8f); }
            else if (rf.NearZombies > 0) { state = "ПОД АТАКОЙ!"; sc = new Color(1f, 0.5f, 0.2f); }
            else { state = "ЗАХВАЧЕН"; sc = new Color(0.4f, 1f, 0.5f); }

            GUI.color = sc; GUI.Label(new Rect(box.x, box.y + 1f, w, 18f), $"НПЗ — {state}", _refStyle);

            // bar: capture progress (neutral) or control (held)
            float frac = rf.Captured ? rf.Control / Refinery.ControlMax : rf.Capture / Refinery.CaptureTime;
            Rect bar = new Rect(box.x + 8f, box.y + 21f, w - 16f, 7f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = rf.Captured ? (rf.NearZombies > 0 ? new Color(1f, 0.5f, 0.2f) : new Color(0.4f, 0.9f, 0.5f)) : new Color(0.6f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(box.x, box.y + 30f, w, 18f), $"бочка: {Mathf.FloorToInt(rf.Oil)}/{Mathf.RoundToInt(Refinery.OilCap)}   E — набрать", _refStyle);
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

        // Hardcore: floating ammo readout above each turret (so you can see which need a refill).
        if (GameRoot.Hardcore && !buildMenuOpen) DrawTurretAmmo();

        // Refineries (НПЗ): floating capture/control/oil status over each (default mode only).
        if (Refinery.All.Count > 0 && !buildMenuOpen) DrawRefineries();

        // Top-left stats panel (kills only — metal moved to bottom-centre)
        // Kills counter — top-right corner (movable).
        Rect kills = Place(2, new Rect(UI.W - 392f, 10f, 380f, 46f));
        Panel(kills);
        GUI.color = Color.yellow; GUI.Label(new Rect(kills.x + 12f, kills.y + 7f, 360f, 34f), $"УБИТО: {Score}", LblRight);

        // Player death counter — under the kills panel (movable).
        Rect deaths = Place(4, new Rect(UI.W - 392f, 60f, 380f, 38f));
        Panel(deaths);
        GUI.color = new Color(1f, 0.45f, 0.45f); GUI.Label(new Rect(deaths.x + 12f, deaths.y + 5f, 360f, 28f), $"СМЕРТЕЙ: {Deaths}", LblRight);
        GUI.color = Color.white;

        // Bottom-left player HP bar (raised + enlarged; movable)
        Rect hp = Place(0, new Rect(20f, UI.H - 110f, 520f, 48f));
        Bar(hp.x, hp.y, hp.width, hp.height, Health / MaxHealth, new Color(0.2f, 0.8f, 0.25f), $"ХП {Mathf.RoundToInt(Health)}");

        // Bottom-centre metal readout (above the tool line)
        Rect metal = Place(1, new Rect(cx - 170f, UI.H - 92f, 340f, 40f));
        Panel(metal);
        GUI.color = UISettings.Accent;
        GUI.Label(new Rect(metal.x, metal.y + 2f, 340f, 36f), $"МЕТАЛЛ: {Metal}", Ctr);
        GUI.color = Color.white;

        // Oil readout (only once refineries exist on the map — default mode).
        if (Refinery.All.Count > 0)
        {
            Rect oil = Place(5, new Rect(cx - 170f, UI.H - 134f, 340f, 36f));
            Panel(oil);
            GUI.color = Oil > 0 ? new Color(1f, 0.85f, 0.35f) : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(oil.x, oil.y + 2f, 340f, 32f), $"НЕФТЬ: {Oil}/{OilMax}", Ctr);
            GUI.color = Color.white;
        }

        // Bottom-center tool line (smaller font + centred so the longer RU text fits)
        string toolLine;
        if (tool == Tool.Gun) toolLine = $"[1] ПУШКА {Guns[gunTier].name}   патроны {ammo}/{Guns[gunTier].mag}";
        else if (tool == Tool.Build) toolLine = $"[2] СТРОЙКА {BuildNames[SelectedBuild]} ({BCost(SelectedBuild)})   ЛКМ=ставить/чинить  E=улучшить  ПКМ=продать  Q=меню";
        else if (tool == Tool.Wrench) toolLine = "[3] КЛЮЧ — ближний бой + починка";
        else toolLine = "[4] ЛОПАТА — зажми ЛКМ чтобы копать";
        float bonusRem = nextBonus - Time.time;
        string bonusTxt = bonusRem <= 0f ? "СКМ:+100 металла" : $"бонус {Mathf.FloorToInt(bonusRem / 60f)}:{Mathf.FloorToInt(bonusRem % 60f):00}";
        toolLine += $"     колесо=оружие   {bonusTxt}";
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
                GUI.Label(new Rect(cx - 400f, 12f, 800f, 28f), "ПОДГОТОВКА — стройте базу!", Line24);
                GUI.Label(new Rect(cx - 400f, 42f, 800f, 24f), $"след. волна: {gm.WaveNumber + 1}/{gm.EvacWave} волн", Sm);
                GUI.Label(new Rect(cx - 300f, 78f, 600f, 64f), $"{Mathf.CeilToInt(gm.PhaseTimeLeft)}с", Big52);

                // Pulsing prompts during prep (cached styles — no per-frame GUIStyle alloc).
                float pulse = 0.6f + 0.4f * Mathf.PingPong(Time.unscaledTime * 1.5f, 1f);
                if (!builtSomething) // the Q hint goes away once you've built your first thing
                {
                    GUI.color = new Color(1f, 0.9f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 350f, 146f, 700f, 32f), "нажмите Q для стройки", Line24);
                }

                // "Press J when ready" — skips the prep. Hidden for co-op clients (the host owns the waves).
                if (!NetClient)
                {
                    GUI.color = new Color(1f, 0.3f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 380f, 180f, 760f, 28f), "если вы готовы — нажмите J, чтобы начать волну", Sm);
                }
                if (GameRoot.Hardcore)
                {
                    GUI.color = new Color(1f, 0.7f, 0.3f, pulse);
                    GUI.Label(new Rect(cx - 400f, 208f, 800f, 26f), "хардкор: турели тратят патроны — пополняй их (E); раздатчик отдаёт лишь накопленное", Sm);
                }
                GUI.color = Color.white;
            }
            else
            {
                Panel(new Rect(cx - 360f, 8f, 720f, 40f));
                GUI.color = new Color(1f, 0.55f, 0.35f);
                GUI.Label(new Rect(cx - 360f, 11f, 720f, 30f), $"ВОЛНА {gm.WaveNumber}   зомби: {gm.ZombiesLeft}", Line24);
                GUI.color = Color.white;
            }
        }

        // Driving hint
        if (vehicle != null)
        {
            Panel(new Rect(cx - 320f, UI.H - 96f, 640f, 40f));
            GUI.color = new Color(0.7f, 0.95f, 1f);
            GUI.Label(new Rect(cx - 320f, UI.H - 92f, 640f, 32f), "WASD — ехать       F — выйти", Line24);
            GUI.color = Color.white;
        }

        // Building info (3 elements) when aiming at one
        if (aimed != null)
        {
            float pw = 460f, px = cx - pw * 0.5f, py = cy + 28f;
            // The 3rd "перезаряд" bar appears whenever a cooldown-gated deposit is possible:
            // funding, a normal upgrade, OR upgrading a fully-charged reserve weapon.
            bool reserveUpgrade = aimed.UsesReserve && aimed.Reserve >= aimed.ReserveMax && aimed.CanUpgrade;
            bool twoBars = !aimed.Building && (aimed.IsFunding || (aimed.CanUpgrade && !aimed.UsesReserve) || reserveUpgrade); // 2-bar layouts
            bool fundingOil = aimed.IsFunding && aimed.OilRequired > 0; // metal + oil + cooldown = an extra bar
            Panel(new Rect(px - 8f, py - 8f, pw + 16f, fundingOil ? 140f : (twoBars ? 116f : 92f)));
            GUI.color = Color.white;
            GUI.Label(new Rect(px, py, pw, 22f), $"{BuildNames[aimed.Type]}  -  УР {aimed.Level}  -  ваше", Sm);
            Bar(px, py + 24f, pw, 20f, aimed.Health / aimed.MaxHealth, new Color(0.2f, 0.8f, 0.25f), $"{Mathf.Max(0, Mathf.RoundToInt(aimed.Health))} / {Mathf.RoundToInt(aimed.MaxHealth)} ХП");

            if (aimed.Building)
            {
                Bar(px, py + 48f, pw, 20f, 1f, new Color(0.9f, 0.7f, 0.2f), "строится...");
            }
            else if (aimed.IsFunding)
            {
                // Bar 2: metal funding (capped chunk per press).
                bool metalDone = aimed.FundingPaid >= aimed.FundingRequired;
                float f = (float)aimed.FundingPaid / Mathf.Max(1, aimed.FundingRequired);
                int chunk = Mathf.Min(Metal, Mathf.Min(aimed.FundChunk, aimed.FundingRemaining));
                string mtxt = metalDone ? $"металл готов ({aimed.FundingRequired})"
                    : Metal > 0 ? $"E: вложить +{chunk}   ({aimed.FundingPaid}/{aimed.FundingRequired})"
                    : $"нужен металл   ({aimed.FundingPaid}/{aimed.FundingRequired})";
                Bar(px, py + 48f, pw, 20f, f, metalDone ? new Color(0.3f, 0.6f, 0.45f) : new Color(0.4f, 0.8f, 1f), mtxt);

                if (aimed.OilRequired > 0)
                {
                    // Bar 3: oil funding (from your reserve) — unlocks once the metal is in.
                    float of = (float)aimed.OilPaid / Mathf.Max(1, aimed.OilRequired);
                    int ochunk = Mathf.Min(Oil, Mathf.Min(OilFundChunk, aimed.OilRemaining));
                    string otxt = aimed.OilPaid >= aimed.OilRequired ? $"нефть готова ({aimed.OilRequired})"
                        : !metalDone ? $"потом нефть   ({aimed.OilPaid}/{aimed.OilRequired})"
                        : Oil > 0 ? $"E: нефть +{ochunk}   ({aimed.OilPaid}/{aimed.OilRequired})"
                        : $"нужна нефть с НПЗ   ({aimed.OilPaid}/{aimed.OilRequired})";
                    Bar(px, py + 72f, pw, 20f, of, new Color(1f, 0.8f, 0.3f), otxt);

                    // Bar 4: deposit cooldown (kept visible — it just moved down a row).
                    if (aimed.UpgradeReadyIn > 0f)
                        Bar(px, py + 96f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), $"перезаряд {aimed.UpgradeReadyIn:0.0}с");
                    else
                        Bar(px, py + 96f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), "готово (E)");
                }
                else if (aimed.UpgradeReadyIn > 0f)
                    Bar(px, py + 72f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), $"перезаряд {aimed.UpgradeReadyIn:0.0}с");
                else
                    Bar(px, py + 72f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), "готово (E)");
            }
            else if (aimed.UsesReserve)
            {
                // Funded special weapon: fill its ammo reserve; once full, E upgrades it.
                float rf = (float)aimed.Reserve / Mathf.Max(1, aimed.ReserveMax);
                if (aimed.Reserve < aimed.ReserveMax)
                {
                    int load = Mathf.Min(Metal, Mathf.Min(ReserveLoadChunk, aimed.ReserveMax - aimed.Reserve));
                    string txt = Metal > 0
                        ? $"E: зарядить +{load}   (заряд {aimed.Reserve}/{aimed.ReserveMax})"
                        : $"нужен металл   (заряд {aimed.Reserve}/{aimed.ReserveMax})";
                    Bar(px, py + 48f, pw, 20f, rf, new Color(0.4f, 0.8f, 1f), txt);
                }
                else if (aimed.CanUpgrade)
                {
                    float invFrac2 = (float)aimed.Invested / aimed.UpgradeCost;
                    string txt = Metal > 0
                        ? $"E: апгрейд +{Mathf.Min(Metal, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})"
                        : $"ПОЛНО — нужен металл на апгрейд";
                    Bar(px, py + 48f, pw, 20f, invFrac2, new Color(0.2f, 0.7f, 0.9f), txt);

                    // Bar 3: cooldown before the next investment (same gate as normal upgrades).
                    if (aimed.UpgradeReadyIn > 0f)
                        Bar(px, py + 72f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), $"перезаряд {aimed.UpgradeReadyIn:0.0}с");
                    else
                        Bar(px, py + 72f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), "готово (E)");
                }
                else
                {
                    Bar(px, py + 48f, pw, 20f, 1f, new Color(0.3f, 0.6f, 0.4f), $"заряд {aimed.Reserve}/{aimed.ReserveMax}  (МАКС)");
                }
            }
            else if (aimed.CanUpgrade)
            {
                // Bar 2: investment progress toward the next level.
                float invFrac = (float)aimed.Invested / aimed.UpgradeCost;
                string invTxt = Metal > 0
                    ? $"E: вложить +{Mathf.Min(Metal, aimed.InvestAmount)}   ({aimed.Invested}/{aimed.UpgradeCost})"
                    : $"нужен металл   ({aimed.Invested}/{aimed.UpgradeCost})";
                Bar(px, py + 48f, pw, 20f, invFrac, new Color(0.2f, 0.7f, 0.9f), invTxt);

                // Bar 3: cooldown before the next investment.
                if (aimed.UpgradeReadyIn > 0f)
                    Bar(px, py + 72f, pw, 20f, 1f - aimed.UpgradeReadyIn / aimed.UpgradeCooldown, new Color(0.9f, 0.6f, 0.2f), $"перезаряд {aimed.UpgradeReadyIn:0.0}с");
                else
                    Bar(px, py + 72f, pw, 20f, 1f, new Color(0.25f, 0.6f, 0.3f), "готово (E)");
            }
            else if (aimed.NeedsRepair)
            {
                Bar(px, py + 48f, pw, 20f, aimed.Health / aimed.MaxHealth, new Color(0.2f, 0.8f, 0.4f), "E: чинить");
            }
            else
            {
                Bar(px, py + 48f, pw, 20f, 1f, new Color(0.4f, 0.4f, 0.45f), "МАКС УРОВЕНЬ");
            }
        }

        if (IsDead)
        {
            // Full death screen: dim everything, big title, two big buttons.
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(new Rect(0f, 0f, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var dead = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0.85f, 0.2f, 0.18f);
            GUI.Label(new Rect(0f, cy - 200f, UI.W, 100f), "ВЫ ПОГИБЛИ", dead);
            if (GameRoot.Hardcore)
            {
                var hc = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.color = new Color(0.9f, 0.55f, 0.2f);
                GUI.Label(new Rect(0f, cy - 110f, UI.W, 36f), "ХАРДКОР — прогресс сброшен", hc);
            }
            GUI.color = Color.white;

            var big = new GUIStyle(GUI.skin.button) { fontSize = 34, fontStyle = FontStyle.Bold };
            float bw = 360f, bh = 90f, gap = 40f;
            float bx = cx - bw - gap * 0.5f;

            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.35f);
            string firstBtn = GameRoot.Hardcore ? "ЗАНОВО (волна 1)" : "РЕСПАВН";
            if (GUI.Button(new Rect(bx, cy - 10f, bw, bh), firstBtn, big))
            {
                if (GameRoot.Hardcore) { if (GameRoot.Instance != null) GameRoot.Instance.RestartRun(); }
                else Respawn();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            GUI.backgroundColor = new Color(0.75f, 0.35f, 0.32f);
            if (GUI.Button(new Rect(cx + gap * 0.5f, cy - 10f, bw, bh), "ВЫЙТИ", big))
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
            const int perRow = 5;
            float bw = 150f, bh = 84f, gap = 12f, headH = 26f, sectGap = 14f;
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
            GUI.Label(new Rect(cx - 460f, startY - 44f, 920f, 30f), "МЕНЮ ПОСТРОЙКИ   —   клик для выбора   (отпусти Q чтобы закрыть)", title);

            var head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            Vector2 mouse = Event.current.mousePosition / UI.Scale; // GUI.matrix isn't applied to the event pos
            int hoverItem = -1;

            float y = startY;
            for (int ci = 0; ci < BuildCategories.Length; ci++)
            {
                GUI.color = new Color(1f, 0.88f, 0.5f);
                GUI.Label(new Rect(leftX + 4f, y, gridW, headH), BuildCategories[ci], head);
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
                    if (GUI.Button(rect, $"{BuildNames[i]}\n{BCost(i)} мет.", btn))
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
                GUI.Label(new Rect(dx + 14f, dy + 6f, dw - 28f, 26f), $"{BuildNames[hoverItem]}   —   {BCost(hoverItem)} металла", dTitle);
                GUI.color = Color.white;
                GUI.Label(new Rect(dx + 14f, dy + 34f, dw - 28f, dh - 40f), BuildDescriptions[hoverItem], dBody);
            }
        }
    }
}
