using UnityEngine;

/// <summary>Zombie-vs-Zombie match director (mode 2.0, Phase 1). Sets up two bases with a
/// destructible CORE each, gives the player metal income, and lets the player release their
/// own zombie horde (team 0) that marches on the enemy core (team 1). Win by destroying the
/// enemy core. Normal waves are off while GameRoot.IsZvZ is true. (AI commander, factory
/// upgrades and LAN come in later phases.)</summary>
public class ZvZManager : MonoBehaviour
{
    public static ZvZManager Instance;

    const int SpawnCost = 25;     // metal per released zombie
    const float CoreZ = 60f;      // each core sits this far up/down the Z axis from centre
    const int IncomePerSec = 10;  // passive metal trickle

    readonly Core[] cores = new Core[2];
    PlayerController player;
    float nextIncome;
    int winner = -1;              // -1 ongoing, 0 player wins, 1 player loses
    bool over;

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();

        Vector3 pPos = Ground(0f, -CoreZ);
        Vector3 ePos = Ground(0f, CoreZ);
        cores[0] = Core.Create(pPos, 0);
        cores[1] = Core.Create(ePos, 1);

        Vector3 spawn = pPos + new Vector3(5f, 1.6f, 5f);
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = spawn;
            if (cc != null) cc.enabled = true;
            player.Metal = 100;
        }
        GameBootstrap.BaseSpawn = spawn;
        GameBootstrap.HasBaseSpawn = true;

        PlaceEnemyDefenses(ePos);
    }

    static Vector3 Ground(float x, float z) => new Vector3(x, GameBootstrap.Hill(x, z), z);

    // A short wall line shielding the enemy core, so your horde has to chew through it.
    void PlaceEnemyDefenses(Vector3 core)
    {
        for (int i = -1; i <= 1; i++)
        {
            float x = core.x + i * 3f, z = core.z - 6f;
            var go = Buildable.Create(3, new Vector3(x, GameBootstrap.Hill(x, z), z), Quaternion.identity, null);
            var b = go != null ? go.GetComponent<Buildable>() : null;
            if (b != null) { b.Team = 1; b.LoadState(1, 9999f, 0); }
        }
    }

    void Update()
    {
        if (over) return;

        if (player != null && Time.time >= nextIncome)
        {
            nextIncome = Time.time + 1f;
            player.AddMetal(IncomePerSec);
        }

        if (player != null && Input.GetKeyDown(KeyCode.G) && player.Metal >= SpawnCost)
        {
            player.AddMetal(-SpawnCost);
            SpawnMyZombie();
        }
    }

    void SpawnMyZombie()
    {
        if (cores[0] == null) return;
        Vector3 at = cores[0].transform.position + new Vector3(Random.Range(-3f, 3f), 1f, 5f);
        var z = Zombie.Create(at, Zombie.Kind.Normal);
        if (z != null) z.team = 0;
    }

    public Core CoreOf(int team) => (team >= 0 && team < 2) ? cores[team] : null;

    public void OnCoreDestroyed(int team)
    {
        if (over) return;
        over = true;
        winner = team == 1 ? 0 : 1; // enemy core down → you win
        Time.timeScale = 1f;
        FreeCursor();
    }

    static void FreeCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    void OnGUI()
    {
        UI.Begin();
        float cx = UI.W * 0.5f;

        DrawCoreBar(20f, "ТВОЁ ЯДРО", cores[0], new Color(0.35f, 0.65f, 1f));
        DrawCoreBar(UI.W - 360f, "ВРАЖЕСКОЕ ЯДРО", cores[1], new Color(1f, 0.45f, 0.35f));

        var s = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.7f, 1f, 0.7f);
        GUI.Label(new Rect(cx - 320f, UI.H - 150f, 640f, 26f), $"G — выпустить зомби ({SpawnCost} мет.)   •   веди орду к вражескому ядру", s);
        GUI.color = Color.white;

        if (over)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, UI.W, UI.H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = winner == 0 ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, UI.H * 0.38f, UI.W, 90f), winner == 0 ? "ПОБЕДА!" : "ПОРАЖЕНИЕ", big);
            GUI.color = Color.white;
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(cx - 110f, UI.H * 0.38f + 110f, 220f, 44f), "В меню", btn))
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
