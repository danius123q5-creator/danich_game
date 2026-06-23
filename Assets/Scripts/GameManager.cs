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

    public float FirstPrepTime = 320f; // first prep before wave 1 (build your base)
    public float PrepTime = 120f;      // prep between later waves
    public float SpawnInterval = 0.6f;
    public int MaxAlive = 120;
    public float SpawnRadius = 40f;
    public int BaseZombies = 6;
    public int PerWave = 6;
    public int EvacWave => GameRoot.Hardcore ? 73 : 60; // reaching this wave triggers the evacuation (hardcore goes longer)
    public float BirdInterval = 13f; // a bird fly-over (drops a zombie) every so often during a wave
    float nextBird;

    public int WaveNumber { get; private set; }
    public bool IsPrep { get; private set; } = true;
    public float PhaseTimeLeft { get; private set; }
    public int ZombiesLeft => zombiesToSpawn + aliveCount;

    int zombiesToSpawn;
    int aliveCount;
    float nextSpawn;
    PlayerController player;

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        IsPrep = true;
        PhaseTimeLeft = FirstPrepTime; // long first prep to build your base; later preps use PrepTime
    }

    void Update()
    {
        // PvP is player-vs-player on an open map — no zombie waves.
        if (GameRoot.IsPvp) return;

        // Tutorial owns the world: TutorialManager spawns its own practice zombies.
        if (GameRoot.IsTutorial) return;

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

        if (IsPrep)
        {
            if (Input.GetKeyDown(KeyCode.J)) // "ready" — skip the prep and start the wave now
            {
                // Reward an early start: more metal the more prep time you skip.
                int bonus = 40 + Mathf.RoundToInt(PhaseTimeLeft * 0.6f);
                foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) p.AddMetal(bonus);
                if (player != null) Effects.Upgrade(player.transform.position + Vector3.up * 1f); // ding + sparkle
                PhaseTimeLeft = 0f;
            }
            PhaseTimeLeft -= Time.deltaTime;
            if (PhaseTimeLeft <= 0f) StartWave();
            return;
        }

        if (zombiesToSpawn > 0 && aliveCount < MaxAlive && Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + SpawnInterval;
            int batch = Mathf.Min(5, Mathf.Min(zombiesToSpawn, MaxAlive - aliveCount)); // spawn in bursts
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
        if (WaveNumber >= EvacWave) { EndgameCinematic.Begin(); return; } // evacuation instead of a normal wave
        zombiesToSpawn = BaseZombies + WaveNumber * PerWave;
        IsPrep = false;
        nextBird = Time.time + BirdEvery();
    }

    // Seconds between bird fly-overs — shrinks as waves get harder (floored at 4s).
    float BirdEvery() => Mathf.Max(4f, BirdInterval - WaveNumber * 0.7f);

    void WaveComplete()
    {
        int bonus = 40 + WaveNumber * 15;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            p.AddMetal(bonus);
        }
        IsPrep = true;
        PhaseTimeLeft = PrepTime;
    }

    /// <summary>Used by Continue to resume from a saved wave number.</summary>
    public void SetWave(int w) { WaveNumber = Mathf.Max(0, w); }

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
        // Random point anywhere on the map, but reachable: 25-130 m from the player.
        float half = GameBootstrap.MapSize * 0.45f;
        Vector3 pos = player.transform.position;
        for (int t = 0; t < 12; t++)
        {
            var cand = new Vector3(Random.Range(-half, half), 0f, Random.Range(-half, half));
            pos = cand;
            float dist = Vector3.Distance(cand, player.transform.position);
            if (dist > 25f && dist < 130f) break;
        }
        pos.y = GameBootstrap.Hill(pos.x, pos.z) + 1f;
        Zombie.Create(pos, PickKind());
        zombiesToSpawn--;
    }

    // Mix of zombie kinds, with the dangerous ones unlocking on later waves.
    Zombie.Kind PickKind()
    {
        float r = Random.value;
        if (WaveNumber >= 3 && r < 0.15f) return Zombie.Kind.Runner;    // ~15% from wave 3 (fast rushers)
        if (WaveNumber >= 4 && r < 0.27f) return Zombie.Kind.Grenadier; // ~12%
        if (WaveNumber >= 3 && r < 0.42f) return Zombie.Kind.Tank;      // ~15%
        if (WaveNumber >= 2 && r < 0.62f) return Zombie.Kind.Pistol;    // ~20%
        return Zombie.Kind.Normal;
    }

    public void OnZombieKilled(PlayerController killer)
    {
        if (killer != null)
        {
            killer.Score += 1; // kills only — no metal for kills
        }
    }
}
