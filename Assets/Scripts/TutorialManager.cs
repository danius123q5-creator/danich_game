using System.Collections.Generic;
using UnityEngine;

/// <summary>Drives the interactive tutorial: a scripted sequence of steps with on-screen
/// hints, build-button highlights and a final practice mini-wave. Spawned by
/// GameRoot.StartTutorial; normal waves are disabled while GameRoot.IsTutorial is true.</summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;

    // Build-menu button to make glow (a BuildNames index), or -1 for none.
    // Read by PlayerController's Q-menu draw loop.
    public static int HighlightBuild = -1;

    PlayerController player;
    Vector3 spawnPos;
    int step;
    float stepTime;        // seconds since the current step began
    float nearDispenser;   // accumulated seconds spent next to a dispenser
    bool waveSpawned;
    bool done;

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) spawnPos = player.transform.position;
        HighlightBuild = -1;
        step = 0; stepTime = 0f;
    }

    void OnDestroy() { if (Instance == this) Instance = null; HighlightBuild = -1; }

    void Update()
    {
        if (done || player == null) return;
        stepTime += Time.deltaTime;

        switch (step)
        {
            case 0: // welcome
                HighlightBuild = -1;
                if (stepTime > 6f || (stepTime > 2f && Input.anyKeyDown)) Advance(); // give time to read the intro
                break;

            case 1: // move around
                HighlightBuild = -1;
                if (Flat(player.transform.position - spawnPos).magnitude > 6f) Advance();
                break;

            case 2: // build a dispenser (the heart of the base)
                HighlightBuild = 1;
                EnsureMetal(110);
                if (HasBuilding(1)) Advance();
                break;

            case 3: // stand by the dispenser to get metal / heal / ammo
                HighlightBuild = -1;
                var disp = NearestBuilt(1);
                if (disp != null && Flat(player.transform.position - disp.transform.position).magnitude < 3.5f)
                    nearDispenser += Time.deltaTime;
                if (nearDispenser > 3f) Advance();
                break;

            case 4: // build a wall
                HighlightBuild = 3;
                EnsureMetal(40);
                if (HasBuilding(3)) Advance();
                break;

            case 5: // build a turret
                HighlightBuild = 0;
                EnsureMetal(150);
                if (HasBuilding(0)) Advance();
                break;

            case 6: // build a ladder and climb up
                HighlightBuild = 20;
                EnsureMetal(45);
                // Height ABOVE the terrain under the player (robust wherever they wandered) —
                // climbing the 4 m ladder/tower clears this easily; standing on the ground doesn't.
                Vector3 pp = player.transform.position;
                if ((HasBuilding(20) || HasBuilding(23)) && pp.y - GameBootstrap.Hill(pp.x, pp.z) > 2.5f) Advance();
                break;

            case 7: // start the wave early with J
                HighlightBuild = -1;
                if (Input.GetKeyDown(KeyCode.J)) Advance();
                break;

            case 8: // practice mini-wave
                HighlightBuild = -1;
                if (!waveSpawned) { SpawnPracticeWave(); waveSpawned = true; }
                if (waveSpawned && (Zombie.All.Count == 0 || stepTime > 120f)) { ClearZombies(); Advance(); }
                break;

            default: // 9: done
                HighlightBuild = -1;
                PlayerPrefs.SetInt("tutorial_done", 1); PlayerPrefs.Save();
                done = true;
                break;
        }
    }

    void Advance() { step++; stepTime = 0f; }

    // Keep the player able to afford the build a step asks for (tutorial-friendly: never softlock).
    void EnsureMetal(int amount) { if (player != null && player.Metal < amount) player.AddMetal(amount - player.Metal); }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static bool HasBuilding(int type)
    {
        foreach (var b in Buildable.All)
            if (b != null && b.Type == type && !b.IsPuppet) return true;
        return false;
    }

    Buildable NearestBuilt(int type)
    {
        Buildable best = null; float bd = float.MaxValue;
        foreach (var b in Buildable.All)
        {
            if (b == null || b.Type != type || b.Building || b.IsPuppet) continue;
            float d = (b.transform.position - player.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    void SpawnPracticeWave()
    {
        for (int i = 0; i < 4; i++)
        {
            float ang = i * (Mathf.PI * 0.5f) + 0.6f;
            const float r = 18f;
            float x = player.transform.position.x + Mathf.Cos(ang) * r;
            float z = player.transform.position.z + Mathf.Sin(ang) * r;
            var pos = new Vector3(x, GameBootstrap.Hill(x, z) + 1f, z);
            var z2 = Zombie.Create(pos, Zombie.Kind.Normal);
            if (z2 != null) z2.TakeDamage(55f); // soften: tutorial zombies are weak
        }
    }

    static void ClearZombies()
    {
        var snapshot = new List<Zombie>(Zombie.All);
        foreach (var z in snapshot) if (z != null) z.TakeDamage(999999f);
    }

    // ---- hints ----
    static readonly string[] Hints =
    {
        "Добро пожаловать! Это быстрое обучение основам управления и базой.",
        "Осмотрись: WASD — идти, мышь — крутить камеру. Пройди немного вперёд.",
        "Зажми Q, выбери РАЗДАТЧИК (подсвечен) и поставь его ЛКМ — это сердце базы.",
        "Встань вплотную к раздатчику: он даёт металл, лечит и пополняет патроны.",
        "Построй СТЕНУ (подсвечена) — она задержит зомби.",
        "Построй ТУРЕЛЬ (подсвечена) — она стреляет по зомби сама.",
        "Построй ВЕРТ. ЛЕСТНИЦУ (подсвечена) и заберись наверх: встань вплотную и держи W.",
        "Между волнами идёт ПОДГОТОВКА. Нажми J, чтобы начать волну раньше за бонус металла.",
        "Защити базу! Уничтожь всех зомби — турель и твоя пушка помогут.",
        "Готово! Ты освоил основы. Обучение завершено — удачи в обороне!",
    };

    static GUIStyle _head, _body, _btn;

    void OnGUI()
    {
        if (player == null) return;
        UI.Begin();
        float cx = UI.W * 0.5f;

        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(cx - 500f, 60f, 1000f, 112f), Texture2D.whiteTexture); // bigger board so the text fits
        GUI.color = Color.white;

        _head ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _body ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        _btn  ??= new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };

        int s = Mathf.Clamp(step, 0, Hints.Length - 1);
        GUI.color = new Color(0.6f, 0.95f, 0.6f);
        GUI.Label(new Rect(cx - 490f, 66f, 980f, 26f), done ? "ОБУЧЕНИЕ ЗАВЕРШЕНО" : $"ОБУЧЕНИЕ — шаг {Mathf.Min(step + 1, 9)}/9", _head);
        GUI.color = new Color(1f, 0.97f, 0.8f);
        GUI.Label(new Rect(cx - 480f, 96f, 960f, 70f), Hints[s], _body);
        GUI.color = Color.white;

        if (done)
        {
            if (GUI.Button(new Rect(cx - 110f, 182f, 220f, 38f), "В меню", _btn))
                { if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu(); }
        }
        else
        {
            if (GUI.Button(new Rect(cx + 360f, 182f, 140f, 32f), "Пропустить ▶", _btn)) Advance();
        }
    }
}
