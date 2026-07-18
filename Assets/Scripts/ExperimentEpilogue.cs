using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// POST-CREDITS ARG stinger — plays AFTER the endgame nuke + credits + "THE END",
/// once the screen is already black, right before the return to the main menu.
///
/// The player thinks the game is over. It isn't. An intercepted handler channel
/// bleeds through the static: the quarantine "outbreak" was a proving ground, the
/// player was an expendable subject, and the evac + nuke were a clean-up that was
/// never meant to leave a survivor. The catch — the range is still transmitting.
///
/// Fully self-contained: draws its own full-black canvas, runs on a timer, then
/// calls GameRoot.ExitToMenu(). Does NOT touch gameplay (fires only at the very
/// end). Host / single-player only (spawned from EndgameCinematic, which gates it).
/// This is ARG layer 1 — seeds PROGRAM "HARVEST" · PHASE 2 · SUBJECT 0 for later.
/// </summary>
public class ExperimentEpilogue : MonoBehaviour
{
    public static bool Active;

    public static void Begin()
    {
        if (Active) return;
        if (GameRoot.IsPvp) return;
        if (LanManager.Instance != null && LanManager.Instance.Active && !LanManager.Instance.IsHost) return; // host/SP only
        new GameObject("ExperimentEpilogue").AddComponent<ExperimentEpilogue>();
    }

    // kind: 0 header, 1 CONTROL, 2 CURATOR, 3 system/breadcrumb
    class Line { public int kind; public string ru, en; public Line(int k, string r, string e) { kind = k; ru = r; en = e; } }

    readonly List<Line> script = new List<Line>();
    float t;                         // seconds since spawn
    int shown;                       // lines fully past their typewriter
    float fadeIn = 1.4f;             // opening black-silence beat
    float fadeOut;                   // 0..1 closing fade
    bool finished;

    const float TypeDur = 0.9f;      // seconds to type a line
    const float LineGap = 1.5f;      // pause after a line before the next
    float carrierDropAt = -1f;       // when the transmission cuts to static (before breadcrumb)

    void Awake() { Active = true; }

    void Start()
    {
        int wave = GameManager.Instance != null ? GameManager.Instance.WaveNumber : 0;
        string w = wave > 0 ? wave.ToString() : "██";

        script.Add(new Line(0, "// ПЕРЕХВАТ ЗАЩИЩЁННОГО КАНАЛА //", "// ENCRYPTED CHANNEL — INTERCEPT //"));
        script.Add(new Line(3, "ПОЛИГОН «ЖАТВА» · СЕАНС ЗАКРЫТ", "PROVING GROUND ‘HARVEST’ · SESSION CLOSED"));
        script.Add(new Line(1, "Полигон стерилизован. Изделие уничтожено.", "Range sterilized. The article is destroyed."));
        script.Add(new Line(2, "А оператор?", "And the operator?"));
        script.Add(new Line(1, "Дошёл до волны " + w + ". В параметры не заложено.", "Reached wave " + w + ". Outside the parameters."));
        script.Add(new Line(2, "Расходники не живут дольше двенадцатой.", "Expendables don’t last past the twelfth."));
        script.Add(new Line(1, "Он не расходник. Он — ████████.", "He is not expendable. He is ████████."));
        script.Add(new Line(2, "Эвакуация — прикрытие. Борт не должен был сесть.", "The evac was cover. The bird was never meant to land."));
        script.Add(new Line(1, "Он на борту. Он всё видел.", "He’s aboard. He saw all of it."));
        script.Add(new Line(2, "…тогда почему полигон всё ещё в эфире?", "…then why is the range still on the air?"));
    }

    // Time at which line index i STARTS typing (header/system reveal quicker than dialogue).
    float LineStart(int i)
    {
        float ts = fadeIn;
        for (int k = 0; k < i; k++) ts += TypeDur + LineGap;
        return ts;
    }

    float ScriptEnd() { return LineStart(script.Count); }

    void Update()
    {
        t += Time.deltaTime;

        // How many lines are fully revealed (used for glitch cadence / progression).
        shown = 0;
        for (int i = 0; i < script.Count; i++)
            if (t >= LineStart(i) + TypeDur) shown++;

        // After the last line, the carrier drops to static for a beat, then the
        // breadcrumb burns in, then we fade out to the menu.
        if (carrierDropAt < 0f && t >= ScriptEnd()) carrierDropAt = t;

        // Allow a skip once the intercept has clearly started (never traps the player).
        if (t > 2.5f && (Input.anyKeyDown || Input.GetMouseButtonDown(0)) && carrierDropAt < 0f)
            carrierDropAt = t; // jump to the sign-off

        if (carrierDropAt >= 0f)
        {
            float since = t - carrierDropAt;
            if (since > 3.2f) fadeOut = Mathf.Min(1f, fadeOut + Time.deltaTime / 1.6f);
            if (fadeOut >= 1f && !finished)
            {
                finished = true;
                Active = false;
                // Hand the black screen straight to the PLAYABLE escape — the
                // subject wakes inside the simulation cube. SimEscape starts black
                // and fades in, so there's no seam. (If SimEscape is ever absent,
                // it would just leave a black screen — Begin() is a no-op guard.)
                SimEscape.Begin();
                Destroy(gameObject);
            }
        }
    }

    // Deterministic-ish pseudo-noise for the static grain (no per-frame allocations of state).
    static float Noise(float a, float b) { return Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f, 1f); }

    void OnGUI()
    {
        UI.Begin();
        float w = UI.W, h = UI.H;

        // Full black backdrop — we sit on top of the already-faded endgame.
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Faint static grain + scanlines (analog interference, kept subtle).
        DrawStatic(w, h);

        float phos = 0.72f;                                   // green phosphor tint
        Color cHeader = new Color(0.55f, 0.95f, 0.6f);
        Color cCtrl = new Color(0.62f, 0.85f, 0.62f) * phos + new Color(0.2f, 0.25f, 0.2f);
        Color cCur = new Color(0.85f, 0.86f, 0.6f) * phos + new Color(0.22f, 0.22f, 0.16f);
        Color cSys = new Color(0.9f, 0.5f, 0.4f);

        var head = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        var body = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        var sys = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        float colW = Mathf.Min(1180f, w * 0.8f);
        float x = (w - colW) * 0.5f;
        float y = h * 0.26f;

        bool cut = carrierDropAt >= 0f;

        // Transmission log (hidden once the carrier drops to the sign-off).
        if (!cut)
        {
            for (int i = 0; i < script.Count; i++)
            {
                float ls = LineStart(i);
                if (t < ls) break;
                Line ln = script[i];
                string full = Lang.T(ln.ru, ln.en);
                if (ln.kind == 1 || ln.kind == 2)
                    full = (Lang.T(ln.kind == 1 ? "КОНТРОЛЬ" : "КУРАТОР", ln.kind == 1 ? "CONTROL" : "CURATOR")) + ":  " + full;

                // Typewriter reveal for the active line.
                float rev = Mathf.Clamp01((t - ls) / TypeDur);
                int nchars = Mathf.Clamp(Mathf.RoundToInt(full.Length * rev), 0, full.Length);
                string disp = full.Substring(0, nchars);
                if (rev < 1f && Mathf.Repeat(t * 3f, 1f) < 0.5f) disp += "▌"; // blinking caret

                if (ln.kind == 0) { GUI.color = cHeader; GUI.Label(new Rect(x, y, colW, 40), disp, head); y += 52; }
                else if (ln.kind == 3) { GUI.color = new Color(0.7f, 0.75f, 0.7f); GUI.Label(new Rect(x, y, colW, 40), disp, head); y += 60; }
                else
                {
                    GUI.color = ln.kind == 1 ? cCtrl : cCur;
                    var gc = new GUIContent(disp);
                    float lh = body.CalcHeight(gc, colW);
                    GUI.Label(new Rect(x, y, colW, lh + 8), disp, body);
                    y += lh + 16;
                }
            }
        }
        else
        {
            // Carrier dropped: a beat of pure static, then the sign-off burns in.
            float since = t - carrierDropAt;
            if (since > 1.4f)
            {
                float a = Mathf.Clamp01((since - 1.4f) / 0.6f);
                GUI.color = new Color(cSys.r, cSys.g, cSys.b, a);
                GUI.Label(new Rect(0, h * 0.42f, w, 46), Lang.T("ПРОГРАММА «ЖАТВА» · ФАЗА 2", "PROGRAM ‘HARVEST’ · PHASE 2"), sys);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.color = new Color(0.8f, 0.85f, 0.8f, a);
                GUI.Label(new Rect(0, h * 0.42f + 54, w, 40), Lang.T("СУБЪЕКТ 0 — ЖИВ", "SUBJECT 0 — ALIVE"), sub);
            }
        }
        GUI.color = Color.white;

        // Skip hint (unobtrusive, bottom corner) until the sign-off.
        if (!cut && t > 2.5f)
        {
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
            GUI.color = new Color(0.5f, 0.55f, 0.5f, 0.6f);
            GUI.Label(new Rect(0, h - 40, w - 24, 30), Lang.T("любая клавиша — пропустить", "any key — skip"), hint);
            GUI.color = Color.white;
        }

        // Closing fade to black.
        if (fadeOut > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fadeOut);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    // Sparse static grain + a wandering interference bar. Cheap, no allocations beyond rects.
    void DrawStatic(float w, float h)
    {
        int dots = 70;
        for (int i = 0; i < dots; i++)
        {
            float n = Noise(i * 0.37f, Mathf.Floor(t * 18f));
            float m = Noise(i * 1.13f + 5f, Mathf.Floor(t * 18f) + 3f);
            float g = 0.10f + 0.16f * Noise(i, Mathf.Floor(t * 30f));
            GUI.color = new Color(0.7f, 0.9f, 0.7f, g * 0.5f);
            GUI.DrawTexture(new Rect(n * w, m * h, 2f, 2f), Texture2D.whiteTexture);
        }
        // horizontal tracking bar drifting down the screen
        float by = Mathf.Repeat(t * 90f, h);
        GUI.color = new Color(0.6f, 0.9f, 0.6f, 0.05f);
        GUI.DrawTexture(new Rect(0, by, w, 22f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
