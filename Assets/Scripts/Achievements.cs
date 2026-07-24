using System.Collections.Generic;
using UnityEngine;

/// <summary>2.7: ACHIEVEMENTS — простая система достижений. Прогресс проверяется раз в кадр из
/// GameManager (по волне / килам / числу построек). Разблокировка сохраняется в PlayerPrefs
/// (аккаунт-уровень, не сбрасывается меж игр) и показывается тостом по центру-сверху. Рисуется
/// из PlayerController.OnGUI (Achievements.DrawToast) — своего MonoBehaviour не нужно.</summary>
public static class Achievements
{
    class Def { public string id, ru, en; public System.Func<int,int,int,bool> hit; }

    // (wave, kills, builds) → достигнуто ли условие
    static readonly List<Def> Defs = new List<Def>
    {
        new Def{ id="w10",   ru="Выжить 10 волн",  en="Survive 10 waves",  hit=(w,k,b)=> w>=10 },
        new Def{ id="w20",   ru="Выжить 20 волн",  en="Survive 20 waves",  hit=(w,k,b)=> w>=20 },
        new Def{ id="w40",   ru="Выжить 40 волн",  en="Survive 40 waves",  hit=(w,k,b)=> w>=40 },
        new Def{ id="evac",  ru="Эвакуация (60 волна)", en="Evacuation (wave 60)", hit=(w,k,b)=> w>=60 },
        new Def{ id="k100",  ru="Первая сотня (100 килов)", en="First hundred (100 kills)", hit=(w,k,b)=> k>=100 },
        new Def{ id="k1000", ru="1000 килов",      en="1000 kills",        hit=(w,k,b)=> k>=1000 },
        new Def{ id="k5000", ru="5000 килов",      en="5000 kills",        hit=(w,k,b)=> k>=5000 },
        new Def{ id="b25",   ru="Крепость (25 построек)", en="Fortress (25 buildings)", hit=(w,k,b)=> b>=25 },
        new Def{ id="b60",   ru="Мегабаза (60 построек)", en="Megabase (60 buildings)", hit=(w,k,b)=> b>=60 },
    };

    struct Toast { public string text; public float time; }
    static readonly List<Toast> _toasts = new List<Toast>();
    const float ToastLife = 4.5f;

    static bool Unlocked(string id) => PlayerPrefs.GetInt("ach_" + id, 0) == 1;

    /// <summary>Проверить прогресс. Зовётся раз в кадр из GameManager.</summary>
    public static void Tick(int wave, int kills, int builds)
    {
        foreach (var d in Defs)
        {
            if (Unlocked(d.id)) continue;
            if (d.hit(wave, kills, builds))
            {
                PlayerPrefs.SetInt("ach_" + d.id, 1);
                PlayerPrefs.Save();
                _toasts.Add(new Toast { text = Lang.T("🏆 ДОСТИЖЕНИЕ: " + d.ru, "🏆 ACHIEVEMENT: " + d.en), time = Time.unscaledTime });
            }
        }
    }

    /// <summary>Сколько всего / открыто (для экрана статистики).</summary>
    public static int Total => Defs.Count;
    public static int Earned { get { int n = 0; foreach (var d in Defs) if (Unlocked(d.id)) n++; return n; } }

    /// <summary>Нарисовать тосты недавних разблокировок. Зовётся из OnGUI (UI.Begin уже был).</summary>
    public static void DrawToast()
    {
        if (_toasts.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = _toasts.Count - 1; i >= 0; i--)
            if (now - _toasts[i].time > ToastLife) _toasts.RemoveAt(i);
        if (_toasts.Count == 0) return;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        float cx = UI.W * 0.5f, y = 120f;
        for (int i = 0; i < _toasts.Count; i++)
        {
            float age = now - _toasts[i].time;
            float a = age > ToastLife - 1f ? Mathf.Clamp01((ToastLife - age)) : 1f;
            GUI.color = new Color(1f, 0.9f, 0.35f, a);
            GUI.Label(new Rect(cx - 400f, y, 800f, 30f), _toasts[i].text, st);
            y += 32f;
        }
        GUI.color = Color.white;
    }
}
