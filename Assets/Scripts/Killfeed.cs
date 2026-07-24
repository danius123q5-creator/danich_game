using System.Collections.Generic;
using UnityEngine;

/// <summary>2.7: KILLFEED — a GMod-style running list of recent kills in the top-right corner.
/// Each kill shows WHO killed WHAT (killer ⚔ victim). Damage sources tag themselves via the
/// optional `by` argument on Zombie.TakeDamage; unattributed kills fall back to a generic label.
/// Drawn from PlayerController.OnGUI so it needs no MonoBehaviour of its own.</summary>
public static class Killfeed
{
    const int MaxLines = 6;      // most recent kills kept on screen
    const float Life = 5f;       // seconds a line stays before it fades out
    const float Fade = 1f;       // last second is a fade-out

    struct Entry { public string killer, victim; public float time; public Color col; }
    static readonly List<Entry> Entries = new List<Entry>();

    // Reset between games so a fresh run starts with an empty feed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Entries.Clear(); }

    /// <summary>Localized display name for a zombie kind (the victim side of the feed).</summary>
    public static string VictimName(Zombie.Kind k)
    {
        switch (k)
        {
            case Zombie.Kind.Pistol:    return Lang.T("стрелок", "gunner");
            case Zombie.Kind.Tank:      return Lang.T("танк", "tank");
            case Zombie.Kind.Grenadier: return Lang.T("гранатомётчик", "grenadier");
            case Zombie.Kind.Runner:    return Lang.T("бегун", "runner");
            case Zombie.Kind.Bloater:   return Lang.T("вздутый", "bloater");
            case Zombie.Kind.Screamer:  return Lang.T("крикун", "screamer");
            case Zombie.Kind.Brute:     return Lang.T("громила", "brute");
            default:                    return Lang.T("зомби", "zombie");
        }
    }

    /// <summary>Record a kill. `killer` may be null/empty → shown as a generic defence kill.</summary>
    public static void Add(string killer, Zombie.Kind victim)
    {
        bool youKill = !string.IsNullOrEmpty(killer) && (killer == "ВЫ" || killer == "YOU");
        var e = new Entry
        {
            killer = string.IsNullOrEmpty(killer) ? Lang.T("оборона", "defense") : killer,
            victim = VictimName(victim),
            time = Time.unscaledTime,
            col = youKill ? new Color(1f, 0.95f, 0.5f) : new Color(0.8f, 0.9f, 1f),
        };
        Entries.Add(e);
        if (Entries.Count > MaxLines) Entries.RemoveAt(0);
    }

    /// <summary>Render the feed. Call from an OnGUI pass; UI.Begin() must already have run.</summary>
    public static void Draw()
    {
        if (Entries.Count == 0) return;
        float now = Time.unscaledTime;
        // Drop expired entries (oldest first).
        for (int i = Entries.Count - 1; i >= 0; i--)
            if (now - Entries[i].time > Life) Entries.RemoveAt(i);
        if (Entries.Count == 0) return;

        var st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight,
            wordWrap = false,
        };
        float w = 380f, h = 22f;
        float x = UI.W - w - 12f;
        float y = 108f; // sits just under the kills + deaths panels (top-right)
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            var e = Entries[i];
            float age = now - e.time;
            float a = age > Life - Fade ? Mathf.Clamp01((Life - age) / Fade) : 1f;
            GUI.color = new Color(e.col.r, e.col.g, e.col.b, a);
            GUI.Label(new Rect(x, y, w, h), $"{e.killer}  ⚔  {e.victim}", st);
            y += h + 2f;
        }
        GUI.color = Color.white;
    }
}
