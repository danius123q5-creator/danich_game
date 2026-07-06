using UnityEngine;

/// <summary>Zombie-vs-Zombie match director (mode 2.0). Two bases, a destructible CORE each;
/// grow a horde (G) and crush the enemy core to win. Roles:
///   • offline  — you are side 0, an AI commander runs side 1.
///   • LAN host — you are side 0 and the sim authority; the client is side 1.
///   • LAN client — you are side 1; the host owns the sim, we send spawn intents and mirror state.
/// Normal defense waves are off while GameRoot.IsZvZ is true.</summary>
public class ZvZManager : MonoBehaviour
{
    public static ZvZManager Instance;

    const int SpawnCost = 25;     // metal per released zombie
    const float CoreZ = 60f;      // each core sits this far up/down the Z axis from centre
    const int IncomePerSec = 10;  // passive metal trickle

    readonly Core[] cores = new Core[2];
    PlayerController player;
    LanManager lan;
    bool authority;               // offline or LAN host: runs the sim and owns core damage
    int myTeam;                   // 0 = host/offline, 1 = LAN client
    float nextIncome, matchTime, nextEnemySpawn;
    int winner = -1;              // -1 ongoing, else the winning team
    bool over;

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        lan = LanManager.Instance;
        bool isNet = lan != null && lan.Active;
        authority = !isNet || lan.IsHost;
        myTeam = (isNet && !lan.IsHost) ? 1 : 0;

        Vector3 c0 = Ground(0f, -CoreZ); // side 0 base
        Vector3 c1 = Ground(0f, CoreZ);  // side 1 base
        cores[0] = Core.Create(c0, 0);
        cores[1] = Core.Create(c1, 1);

        // Drop the local player at THEIR base, facing the enemy.
        Vector3 myBase = myTeam == 0 ? c0 : c1;
        Vector3 enemyBase = myTeam == 0 ? c1 : c0;
        Vector3 spawn = myBase + new Vector3(5f, 1.6f, myTeam == 0 ? 5f : -5f);
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = spawn;
            Vector3 face = enemyBase - myBase; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(face);
            if (cc != null) cc.enabled = true;
            player.Metal = 100;
        }
        GameBootstrap.BaseSpawn = spawn;
        GameBootstrap.HasBaseSpawn = true;

        if (authority)
        {
            PlaceWalls(c1, 1); // shield each core with a short wall line (host streams them to clients)
            PlaceWalls(c0, 0);
            nextEnemySpawn = Time.time + 8f;
        }
    }

    static Vector3 Ground(float x, float z) => new Vector3(x, GameBootstrap.Hill(x, z), z);

    void PlaceWalls(Vector3 core, int team)
    {
        float front = team == 0 ? 6f : -6f; // wall on the enemy-facing side
        for (int i = -1; i <= 1; i++)
        {
            float x = core.x + i * 3f, z = core.z + front;
            var go = Buildable.Create(3, new Vector3(x, GameBootstrap.Hill(x, z), z), Quaternion.identity, null);
            var b = go != null ? go.GetComponent<Buildable>() : null;
            if (b != null) { b.Team = team; b.LoadState(1, 9999f, 0); }
        }
    }

    void Update()
    {
        if (over) return;
        matchTime += Time.deltaTime;

        if (player != null && Time.time >= nextIncome)
        {
            nextIncome = Time.time + 1f;
            player.AddMetal(IncomePerSec);
        }

        if (player != null && Input.GetKeyDown(KeyCode.G) && player.Metal >= SpawnCost)
        {
            player.AddMetal(-SpawnCost);
            if (authority) SpawnZombie(myTeam, Zombie.Kind.Normal);
            else lan.SendZvZSpawn(); // client: ask the host to release my (team-1) zombie
        }

        if (authority)
        {
            if (lan != null && lan.Active)
            {
                // LAN host: spawn the client's requested team-1 zombies
                int reqs = lan.TakeZvZSpawns();
                for (int i = 0; i < reqs; i++) SpawnZombie(1, Zombie.Kind.Normal);

                // stream core state + result to the client
                lan.HostZvZActive = true;
                lan.HostZvZCore0 = cores[0] != null ? cores[0].Health : 0f;
                lan.HostZvZCore1 = cores[1] != null ? cores[1].Health : 0f;
                lan.HostZvZWinner = winner;
            }
            else if (cores[1] != null && Time.time >= nextEnemySpawn)
            {
                // OFFLINE: AI commander pushes escalating team-1 hordes
                float interval = Mathf.Max(2f, 6f - matchTime / 60f);
                nextEnemySpawn = Time.time + interval;
                int batch = 1 + (int)(matchTime / 45f);
                for (int i = 0; i < batch; i++) SpawnZombie(1, PickEnemyKind());
            }
        }
        else
        {
            // LAN client: mirror the host's core HPs + result
            if (lan != null)
            {
                if (cores[0] != null) cores[0].Health = lan.ZvZCore0;
                if (cores[1] != null) cores[1].Health = lan.ZvZCore1;
                if (lan.ZvZWinner >= 0) { winner = lan.ZvZWinner; over = true; FreeCursor(); }
            }
        }
    }

    void SpawnZombie(int team, Zombie.Kind kind)
    {
        var core = cores[team];
        if (core == null) return;
        float front = team == 0 ? 7f : -7f; // emerge on the enemy-facing side
        Vector3 at = core.transform.position + new Vector3(Random.Range(-4f, 4f), 1f, front);
        at.y = GameBootstrap.Hill(at.x, at.z) + 1f;
        var z = Zombie.Create(at, kind);
        if (z != null) z.team = team;
    }

    Zombie.Kind PickEnemyKind()
    {
        float r = Random.value;
        if (matchTime > 90f && r < 0.18f) return Zombie.Kind.Tank;   // heavies late
        if (matchTime > 45f && r < 0.35f) return Zombie.Kind.Runner; // fast rushers mid-game
        return Zombie.Kind.Normal;
    }

    public Core CoreOf(int team) => (team >= 0 && team < 2) ? cores[team] : null;

    public void OnCoreDestroyed(int team)
    {
        if (over) return;
        over = true;
        winner = team == 1 ? 0 : 1; // the side whose enemy core fell wins
        if (lan != null && lan.Active) { lan.HostZvZActive = true; lan.HostZvZWinner = winner; }
        Time.timeScale = 1f;
        FreeCursor();
    }

    static void FreeCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    void OnGUI()
    {
        UI.Begin();
        float cx = UI.W * 0.5f;

        DrawCoreBar(20f, Lang.T("ТВОЁ ЯДРО", "YOUR CORE"), cores[myTeam], new Color(0.35f, 0.65f, 1f));
        DrawCoreBar(UI.W - 360f, Lang.T("ВРАЖЕСКОЕ ЯДРО", "ENEMY CORE"), cores[1 - myTeam], new Color(1f, 0.45f, 0.35f));

        var s = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        var box = new Rect(cx - 470f, UI.H - 150f, 940f, 50f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = new Color(0.75f, 1f, 0.75f);
        GUI.Label(box, Lang.T($"G — выпустить зомби ({SpawnCost} мет.)    •    снеси вражеское ядро    •    Q — стройка/защита",
                              $"G — release a zombie ({SpawnCost} metal)    •    destroy the enemy core    •    Q — build/defend"), s);
        GUI.color = Color.white;

        if (over)
        {
            bool iWon = winner == myTeam;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = iWon ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, UI.H * 0.38f, UI.W, 90f), iWon ? Lang.T("ПОБЕДА!", "VICTORY!") : Lang.T("ПОРАЖЕНИЕ", "DEFEAT"), big);
            GUI.color = Color.white;
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(cx - 110f, UI.H * 0.38f + 110f, 220f, 44f), Lang.T("В меню", "To menu"), btn))
                { if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu(); }
        }
    }

    void DrawCoreBar(float x, string label, Core c, Color col)
    {
        if (c == null) return;
        var lab = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(x, 16f, 340f, 20f), label, lab);
        float frac = Mathf.Clamp01(c.Health / c.MaxHealth);
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x, 38f, 340f, 22f), Texture2D.whiteTexture);
        GUI.color = col; GUI.DrawTexture(new Rect(x, 38f, 340f * frac, 22f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var hp = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(x, 38f, 340f, 22f), $"{Mathf.CeilToInt(c.Health)} / {Mathf.CeilToInt(c.MaxHealth)}", hp);
    }
}
