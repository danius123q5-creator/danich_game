using System.Collections.Generic;
using UnityEngine;

/// <summary>Drives the interactive tutorial: a scripted sequence of steps with on-screen
/// hints, build-button highlights and a final practice mini-wave. Spawned by
/// GameRoot.StartTutorial; normal waves are disabled while GameRoot.IsTutorial is true.
/// Teaches the basics AND the resource economy (capture an НПЗ → oil for super-weapons).</summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;

    // Build-menu button to make glow (a BuildNames index), or -1 for none.
    public static int HighlightBuild = -1;

    const int TotalSteps = 11; // interactive steps (0..10); step 11 = done

    PlayerController player;
    Vector3 spawnPos;
    Refinery tutRefinery;  // a practice НПЗ spawned for the capture lesson
    int step;
    float stepTime;
    float nearDispenser;
    bool waveSpawned;
    bool done;

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) spawnPos = player.transform.position;
        HighlightBuild = -1;
        step = 0; stepTime = 0f;

        // Spawn one practice refinery a short walk ahead so the capture lesson has a target.
        Vector3 rp = spawnPos + new Vector3(15f, 0f, 6f);
        rp.y = GameBootstrap.Hill(rp.x, rp.z);
        tutRefinery = Refinery.Create(rp);
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
                if (stepTime > 6f || (stepTime > 2f && Input.anyKeyDown)) Advance();
                break;

            case 1: // move around
                HighlightBuild = -1;
                if (Flat(player.transform.position - spawnPos).magnitude > 6f) Advance();
                break;

            case 2: // build the dispenser (the critical heart of the base)
                HighlightBuild = 1;
                EnsureMetal(110);
                if (HasBuilding(1)) Advance();
                break;

            case 3: // stand by the dispenser
                HighlightBuild = -1;
                var disp = NearestBuilt(1);
                if (disp != null && Flat(player.transform.position - disp.transform.position).magnitude < 3.5f)
                    nearDispenser += Time.deltaTime;
                if (nearDispenser > 3f) Advance();
                break;

            case 4: // capture the НПЗ (walk to it and stand in the zone)
                HighlightBuild = -1;
                if (tutRefinery == null || tutRefinery.Captured) Advance();
                break;

            case 5: // economy explainer (read-and-advance)
                HighlightBuild = -1;
                if (stepTime > 9f || (stepTime > 3f && Input.anyKeyDown)) Advance();
                break;

            case 6: // build a wall
                HighlightBuild = 3;
                EnsureMetal(40);
                if (HasBuilding(3)) Advance();
                break;

            case 7: // build a turret
                HighlightBuild = 0;
                EnsureMetal(150);
                if (HasBuilding(0)) Advance();
                break;

            case 8: // build a ladder and climb up
                HighlightBuild = 20;
                EnsureMetal(45);
                Vector3 pp = player.transform.position;
                if ((HasBuilding(20) || HasBuilding(23)) && pp.y - GameBootstrap.Hill(pp.x, pp.z) > 2.5f) Advance();
                break;

            case 9: // start the wave early with J
                HighlightBuild = -1;
                if (Input.GetKeyDown(KeyCode.J)) Advance();
                break;

            case 10: // practice mini-wave
                HighlightBuild = -1;
                if (!waveSpawned) { SpawnPracticeWave(); waveSpawned = true; }
                if (waveSpawned && (Zombie.All.Count == 0 || stepTime > 120f)) { ClearZombies(); Advance(); }
                break;

            default: // 11: done
                HighlightBuild = -1;
                PlayerPrefs.SetInt("tutorial_done", 1); PlayerPrefs.Save();
                done = true;
                break;
        }
    }

    void Advance() { step++; stepTime = 0f; }

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
            if (z2 != null) z2.TakeDamage(55f);
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
        "Добро пожаловать! Быстрое обучение: основы управления, базы и экономики.",
        "WASD — идти, мышь — крутить камеру. Пройди немного вперёд.",
        "Зажми Q, выбери РАЗДАТЧИК (подсвечен) и поставь его ЛКМ. Это сердце базы — если его уничтожат, игра окончена. Защищай его!",
        "Встань вплотную к раздатчику: он лечит, даёт металл и пополняет патроны.",
        "Впереди стоит НПЗ. Подойди и постой рядом с ним, чтобы ЗАХВАТИТЬ (зомби рядом мешают захвату).",
        "Захвачено! НПЗ даёт НЕФТЬ — на ней работают супер-пушки (Тесла, Орбиталка). ШАХТЫ дают металл. Точки захватывай и ОБОРОНЯЙ — или построй свои НЕФТ. ВЫШКУ / БУРОВУЮ (захват не нужен). Труба/конвейер тянутся линией (зажми ЛКМ у источника, веди к базе), дозатор/чан у базы сами выдают ресурс. Чем больше источников на сети — тем больше потока.",
        "Построй СТЕНУ (подсвечена) — она задержит зомби.",
        "Построй ТУРЕЛЬ (подсвечена) — она стреляет по зомби сама.",
        "Построй ВЕРТ. ЛЕСТНИЦУ (подсвечена) и заберись наверх: встань вплотную и держи W.",
        "Между волнами идёт ПОДГОТОВКА. Нажми J, чтобы начать волну раньше за бонус металла.",
        "Защити базу! Уничтожь всех зомби — турель и твоя пушка помогут.",
        "Готово! Ты освоил основы и экономику. Обучение завершено — удачи в обороне!",
    };

    static GUIStyle _head, _body, _btn;

    void OnGUI()
    {
        if (player == null) return;
        UI.Begin();
        float cx = UI.W * 0.5f;

        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(cx - 520f, 60f, 1040f, 132f), Texture2D.whiteTexture); // board sized for the longer economy text
        GUI.color = Color.white;

        _head ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _body ??= new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        _btn  ??= new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };

        int s = Mathf.Clamp(step, 0, Hints.Length - 1);
        GUI.color = new Color(0.6f, 0.95f, 0.6f);
        GUI.Label(new Rect(cx - 510f, 66f, 1020f, 26f), done ? "ОБУЧЕНИЕ ЗАВЕРШЕНО" : $"ОБУЧЕНИЕ — шаг {Mathf.Min(step + 1, TotalSteps)}/{TotalSteps}", _head);
        GUI.color = new Color(1f, 0.97f, 0.8f);
        GUI.Label(new Rect(cx - 500f, 94f, 1000f, 92f), Hints[s], _body);
        GUI.color = Color.white;

        if (done)
        {
            if (GUI.Button(new Rect(cx - 110f, 200f, 220f, 38f), "В меню", _btn))
                { if (GameRoot.Instance != null) GameRoot.Instance.ExitToMenu(); }
        }
        else
        {
            if (GUI.Button(new Rect(cx + 380f, 200f, 140f, 32f), "Пропустить ▶", _btn)) Advance();
        }
    }
}
