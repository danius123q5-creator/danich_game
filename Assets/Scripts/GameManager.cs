using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wave director: a calm PREP phase (build your base) counts down, then a WAVE
/// of zombies spawns and must be cleared. Wave completion is decided by the ACTUAL
/// number of zombies left in the scene (not a counter), so a lost/stuck zombie can
/// never softlock the wave loop.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public float FirstPrepTime = 400f; // first prep before wave 1 (build your base)
    public float PrepTime = 120f;      // prep between later waves
    public float SpawnInterval = 0.6f;
    public int MaxAlive = 120;
    public float SpawnRadius = 40f;
    public int BaseZombies = 6;
    public int PerWave = 6;
    public int EvacWave => GameRoot.Hardcore ? 61 : 55; // reaching this wave triggers the evacuation (hardcore goes longer)
    public float BirdInterval = 13f; // a bird fly-over (drops a zombie) every so often during a wave
    float nextBird;

    // Terrain altered this game (missile craters etc.)? The .gdf save records it. Reset each load.
    public static bool LandscapeChanged;

    public int WaveNumber { get; private set; }
    public bool IsPrep { get; private set; } = true;
    public float PhaseTimeLeft { get; private set; }
    public int ZombiesLeft => zombiesToSpawn + aliveCount;

    int zombiesToSpawn;
    int aliveCount;
    float nextSpawn;
    float noDispTimer;   // how long the base has had zero live dispensers (game-over grace)
    PlayerController player;

    void Awake() { Instance = this; LandscapeChanged = false; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        IsPrep = true;
        PhaseTimeLeft = FirstPrepTime; // long first prep to build your base; later preps use PrepTime

        // Capturable oil refineries — default/waves mode only (not PvP/tutorial/ZvZ; LAN host or SP).
        bool defaultMode = !GameRoot.IsPvp && !GameRoot.IsTutorial && !GameRoot.IsZvZ &&
                           !(LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost);
        if (defaultMode && Refinery.All.Count == 0) Refinery.SpawnAll();
        if (defaultMode && OreMine.All.Count == 0) OreMine.SpawnAll();

        ModRuntime.OnGameStart(); // 3.2: fire GAME_START mod actions (player exists now)
    }

    void Update()
    {
        // PvP is player-vs-player on an open map — no zombie waves.
        if (GameRoot.IsPvp) return;

        // Tutorial owns the world: TutorialManager spawns its own practice zombies.
        if (GameRoot.IsTutorial) return;

        // ZvZ owns the world: ZvZManager runs the match (no AI defense waves).
        if (GameRoot.IsZvZ) return;

        // On a LAN client the host owns the wave/zombie sim — keep the client's world calm.
        if (LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost) return;

        if (EndgameCinematic.Active) return;                          // the cutscene owns the world

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;
        }

        // Source of truth: how many zombies actually exist right now.
        aliveCount = Zombie.All.Count;

        // Base lifeline safety net: once the base exists, if EVERY dispenser is gone for a moment
        // (destroyed — the critical one included), the game is lost. A short grace avoids a false
        // defeat while relocating the base. Suppressed during the evac finale.
        if (Dispenser.BaseEstablished && !GameRoot.BaseLost && !EndgameCinematic.Active &&
            !GameRoot.IsZvZ && !GameRoot.IsPvp && !GameRoot.Sandbox && !GameRoot.Infinite)
        {
            if (Dispenser.AliveCount() == 0)
            {
                noDispTimer += Time.deltaTime;
                if (noDispTimer > 1.5f) GameRoot.BaseLost = true;
            }
            else noDispTimer = 0f;
        }

        if (IsPrep)
        {
            if (Input.GetKeyDown(KeyCode.J)) // press J when ready — skip the prep and start the wave now
            {
                if (!GameRoot.Sandbox) // reward an early start (sandbox has infinite metal anyway)
                {
                    int bonus = 40 + Mathf.RoundToInt(PhaseTimeLeft * 0.6f);
                    foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) p.AddMetal(bonus);
                    if (player != null) Effects.Upgrade(player.transform.position + Vector3.up * 1f); // ding + sparkle
                }
                PhaseTimeLeft = 0f;
            }
            if (!GameRoot.Sandbox) PhaseTimeLeft -= Time.deltaTime; // sandbox: waves start only on J
            if (PhaseTimeLeft <= 0f) StartWave();
            return;
        }

        // Surrender the wave: K clears every zombie and ends the wave, but the price is steep —
        // it strips ALL your oil and metal. A hint is shown in the HUD during a wave.
        if (Input.GetKeyDown(KeyCode.K)) { Surrender(); return; }

        if (zombiesToSpawn > 0 && aliveCount < MaxAlive && Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + SpawnInterval;
            int burst = WaveNumber >= 25 ? 8 : 5; // deeper waves feed the horde in bigger gulps
            int batch = Mathf.Min(burst, Mathf.Min(zombiesToSpawn, MaxAlive - aliveCount));
            for (int i = 0; i < batch; i++) { SpawnZombie(); aliveCount++; }
        }

        // Birds that fly over and drop zombies (unless an AntiAir downs them). Later waves
        // send them more often AND in bigger flocks.
        if (Time.time >= nextBird && aliveCount < MaxAlive)
        {
            nextBird = Time.time + BirdEvery();
            int flock = Mathf.Clamp(1 + WaveNumber / 3, 1, 8); // wave 1-2:1, 3-5:2, ... up to 8
            for (int i = 0; i < flock && aliveCount < MaxAlive; i++) Bird.SpawnOver(player);
        }

        // Wave clears only when everything queued has spawned AND none remain alive.
        if (zombiesToSpawn <= 0 && aliveCount == 0)
        {
            WaveComplete();
        }
    }

    void StartWave()
    {
        WaveNumber++;
        ModRuntime.OnWaveStart(); // 3.2: fire WAVE_START mod actions
        if (WaveNumber >= EvacWave && !GameRoot.Infinite) { EndgameCinematic.Begin(); return; } // evac finale (skipped in endless mode)

        // Horde size: linear early, then a quadratic LATE-GAME SURGE past wave 20 so the deep
        // waves feel like a flood, not a trickle. e.g. w20≈126, w35≈281, w55≈636.
        int count = BaseZombies + WaveNumber * PerWave;
        if (WaveNumber > 20) count += (WaveNumber - 20) * (WaveNumber - 20) / 4;
        zombiesToSpawn = count;

        // Raise the on-screen cap in the late game too — otherwise the bigger queue just
        // drains through the same 120-alive bottleneck and you never SEE the surge.
        MaxAlive = Mathf.Clamp(120 + Mathf.Max(0, WaveNumber - 15) * 5, 120, 220);
        IsPrep = false;
        nextBird = Time.time + BirdEvery();

        // 3.1.1: enemy BOMBER raids removed — the user found them too punishing. (Kamikaze DRONE
        // raids below stay: they're cheaper, telegraphed and easily swatted by the ЗЕНИТКА.)
        // if (WaveNumber >= 24 && WaveNumber % 3 == 0) SpawnAirRaid();

        // Enemy kamikaze DRONE raids: from wave 12, every 4th wave, a swarm dives on your buildings.
        // Cheaper/earlier than the bomber raids — the ЗЕНИТКА shoots them down.
        if (WaveNumber >= 12 && WaveNumber % 4 == 0) SpawnDroneRaid();

        // Снабжение: с 37-й волны кукурузники (Ан-2) пролетают над игроком и сбрасывают
        // на парашюте жирные ящики с нефтью и металлом — помощь в позднем аду. Чем глубже
        // волна, тем больше бортов: 37→1, 43→2, 47→3, 50→4, 54→5.
        int supplyPlanes = SupplyPlaneCount(WaveNumber);
        for (int i = 0; i < supplyPlanes; i++) SupplyPlane.SpawnOver(player);
    }

    // Сколько бортов снабжения в этой волне (ступенчато растёт к финалу).
    static int SupplyPlaneCount(int w)
    {
        if (w >= 54) return 5;
        if (w >= 50) return 4;
        if (w >= 47) return 3;
        if (w >= 43) return 2;
        if (w >= 37) return 1;
        return 0;
    }

    Vector3 BaseCentre() => GameBootstrap.HasBaseSpawn ? GameBootstrap.BaseSpawn
                          : (player != null ? player.transform.position : Vector3.zero);

    void SpawnAirRaid()
    {
        Vector3 baseC = BaseCentre();
        int planes = Mathf.Clamp(2 + (WaveNumber - 24) / 6, 2, 4); // 2 at wave 24, up to 4 later
        for (int i = 0; i < planes; i++)
        {
            Vector2 off = Random.insideUnitCircle * 22f;
            Bomber.SpawnEnemy(baseC + new Vector3(off.x, 0f, off.y));
        }
    }

    void SpawnDroneRaid()
    {
        Vector3 baseC = BaseCentre();
        int drones = Mathf.Clamp(2 + (WaveNumber - 12) / 5, 2, 6); // grows with the wave
        for (int i = 0; i < drones; i++) EnemyDrone.Spawn(baseC);
    }

    // Seconds between bird fly-overs — shrinks as waves get harder (floored at 4s).
    float BirdEvery() => Mathf.Max(4f, BirdInterval - WaveNumber * 0.7f);

    /// <summary>K during a wave: wipe the horde and end the wave early, at the cost of ALL your
    /// oil and metal. A desperate reset button.</summary>
    void Surrender()
    {
        foreach (var z in new List<Zombie>(Zombie.All))
            if (z != null && z.team < 0) z.TakeDamage(999999f); // clear the invading horde
        zombiesToSpawn = 0;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            p.AddMetal(-p.Metal);  // strip metal
            p.AddOil(-p.Oil);      // strip oil
            Effects.AirBlast(p.transform.position + Vector3.up * 1f, 6f);
        }
        IsPrep = true;
        PhaseTimeLeft = PrepTime;
    }

    void WaveComplete()
    {
        // 3.1.1: wave-clear metal ramps hard for late game (+30/wave plus a step every 10 waves),
        // and doubles in endless mode. Was a meagre 40 + wave*15.
        int bonus = Mathf.RoundToInt((60 + WaveNumber * 30 + (WaveNumber / 10) * 300) * GameRoot.IncomeMult);
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            p.AddMetal(bonus);
        }
        ModRuntime.OnWaveClear(); // 3.2: fire WAVE_CLEAR mod actions
        IsPrep = true;
        PhaseTimeLeft = PrepTime;
    }

    /// <summary>3.1.1: metal granted for capturing an НПЗ/ШАХТА — grows every 10 waves (was a flat 677),
    /// doubled in endless mode. Late-game captures finally pay off.</summary>
    public static int CaptureMetalReward()
    {
        var gm = Instance;
        int wave = gm != null ? gm.WaveNumber : 0;
        return Mathf.RoundToInt((PlayerController.CaptureMetalBonus + (wave / 10) * 700) * GameRoot.IncomeMult);
    }

    /// <summary>Used by Continue to resume from a saved wave number.</summary>
    public void SetWave(int w) { WaveNumber = Mathf.Max(0, w); }

    // ---- debug helpers (called by DebugOverlay) ----
    /// <summary>Clear every zombie and finish the current wave with NO penalty (debug).</summary>
    public void DebugClearWave()
    {
        foreach (var z in new List<Zombie>(Zombie.All)) if (z != null) z.TakeDamage(999999f);
        zombiesToSpawn = 0;
        if (!IsPrep) { IsPrep = true; PhaseTimeLeft = PrepTime; }
    }
    /// <summary>Skip the current prep and start the wave now (debug).</summary>
    public void DebugSkipPrep() { if (IsPrep) PhaseTimeLeft = 0f; }
    /// <summary>Jump ahead a number of waves (debug) — bumps the counter used for scaling.</summary>
    public void DebugAddWaves(int n) { WaveNumber = Mathf.Max(0, WaveNumber + n); }

    /// <summary>Co-op client: adopt the host's wave/HUD state (the client doesn't run the sim).</summary>
    public void ApplyNetWave(int wave, bool prep, float timeLeft, int alive)
    {
        WaveNumber = wave;
        IsPrep = prep;
        PhaseTimeLeft = timeLeft;
        zombiesToSpawn = 0;
        aliveCount = alive;
    }

    void SpawnZombie()
    {
        // 3.1.1: spread the horde across the WHOLE map instead of a tight 25-130 m ring, so the FAR
        // field always has targets for long-range weapons (ФАУ-1/Shahed/silo were idling with nothing
        // beyond their minimum range). ~60% of spawns aim for the far field; near defenses still get
        // plenty as the horde walks in. (Zombie range unchanged — only where they SPAWN.)
        float half = GameBootstrap.MapSize * 0.48f;   // nearly the full map
        Vector3 pp = player.transform.position;
        Vector3 pos = pp;
        bool wantFar = Random.value < 0.6f;
        for (int t = 0; t < 16; t++)
        {
            var cand = new Vector3(Random.Range(-half, half), 0f, Random.Range(-half, half));
            pos = cand;
            float dist = Vector3.Distance(cand, pp);
            if (dist < 50f) continue;                 // never right on top of the player
            if (wantFar ? dist > 220f : dist < 260f) break; // far pass wants distant points; near pass anything mid
        }
        pos.y = GameBootstrap.Hill(pos.x, pos.z) + 1f;
        Zombie.Create(pos, PickKind());
        zombiesToSpawn--;
    }

    // Mix of zombie kinds, with the dangerous ones unlocking on later waves.
    Zombie.Kind PickKind()
    {
        // Mini-boss BRUTE: rare, late-game only, slightly more common the deeper you push.
        if (WaveNumber >= 15 && Random.value < Mathf.Min(0.06f, 0.015f + (WaveNumber - 15) * 0.002f))
            return Zombie.Kind.Brute;

        float r = Random.value;
        if (WaveNumber >= 8 && r < 0.10f) return Zombie.Kind.Screamer;  // ~10% summoners (kill first)
        if (WaveNumber >= 12 && r < 0.22f) return Zombie.Kind.Bloater;  // ~12% toxic gas-bags
        if (WaveNumber >= 3 && r < 0.35f) return Zombie.Kind.Runner;    // ~13% fast rushers
        if (WaveNumber >= 4 && r < 0.46f) return Zombie.Kind.Grenadier; // ~11%
        if (WaveNumber >= 3 && r < 0.60f) return Zombie.Kind.Tank;      // ~14%
        if (WaveNumber >= 2 && r < 0.78f) return Zombie.Kind.Pistol;    // ~18%
        return Zombie.Kind.Normal;
    }

    public void OnZombieKilled(PlayerController killer)
    {
        if (killer != null)
        {
            killer.Score += 1; // kills only — no metal for kills
        }
        ModRuntime.OnZombieKilled(); // 3.2: fire ZOMBIE_KILLED mod actions
    }
}
