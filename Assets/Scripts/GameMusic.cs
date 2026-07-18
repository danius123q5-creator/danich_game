using UnityEngine;

/// <summary>
/// Procedural adaptive soundtrack (no audio assets — synthesized from math, like the SFX).
/// Two looping tracks in A-minor: a slow, melancholic CALM theme during the build/prep phase,
/// and a faster, driving COMBAT theme while a zombie wave is on. Cross-fades between them.
/// Spawned with the world; lives only during gameplay.
/// </summary>
public class GameMusic : MonoBehaviour
{
    const int Rate = 44100;
    const float BaseVolume = 0.32f;

    AudioSource _calm, _combat;
    bool _menuMode;   // главное меню: играет только спокойная тема, без боевой логики

    public static void Spawn(Transform parent)
    {
        var go = new GameObject("GameMusic");
        if (parent != null) go.transform.SetParent(parent);
        go.AddComponent<GameMusic>();
    }

    // Музыка ГЛАВНОГО МЕНЮ — спокойная атмосферная тема (та же процедурка).
    // Спавнится из MenuBackground, живёт пока открыто меню. 2026-07-18.
    public static void SpawnMenu(Transform parent)
    {
        var go = new GameObject("MenuMusic");
        if (parent != null) go.transform.SetParent(parent);
        go.AddComponent<GameMusic>()._menuMode = true;
    }

    void Start()
    {
        _calm = MakeSource(BuildCalmTrack());
        _calm.Play();
        if (_menuMode)
        {
            _calm.volume = BaseVolume;   // сразу играем меню-тему
            return;
        }
        _combat = MakeSource(BuildCombatTrack());
        _combat.Play();
    }

    AudioSource MakeSource(AudioClip clip)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.volume = 0f;
        s.spatialBlend = 0f;   // 2D background music
        s.playOnAwake = false;
        s.priority = 0;        // never culled
        return s;
    }

    void Update()
    {
        if (_menuMode)   // меню: держим спокойную тему, боевую не трогаем
        {
            _calm.volume = Mathf.MoveTowards(_calm.volume, BaseVolume, 0.6f * Time.deltaTime);
            return;
        }
        var gm = GameManager.Instance;
        // Combat music while a wave is actually running (not prep, not the evac cutscene, not PvP idle).
        bool combat = gm != null && !gm.IsPrep && !EndgameCinematic.Active;
        bool silent = EndgameCinematic.Active; // let the cutscene breathe

        float wantCombat = (combat && !silent) ? BaseVolume : 0f;
        float wantCalm = (!combat && !silent) ? BaseVolume : 0f;

        float k = 0.6f * Time.deltaTime; // ~1.5s cross-fade
        _combat.volume = Mathf.MoveTowards(_combat.volume, wantCombat, k);
        _calm.volume = Mathf.MoveTowards(_calm.volume, wantCalm, k);
    }

    // ───────────────────────── synthesis ─────────────────────────

    static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

    // Slow, sparse, a little hopeful-sad: Am – F – C – G pad + soft bass.
    AudioClip BuildCalmTrack()
    {
        const float bpm = 66f;
        float beat = 60f / bpm;
        int bars = 8;
        float dur = bars * 4 * beat;
        var buf = new float[(int)(Rate * dur)];

        int[][] chords =
        {
            new[] { 45, 48, 52 }, // Am  (A C E)
            new[] { 41, 45, 48 }, // F   (F A C)
            new[] { 48, 52, 55 }, // C   (C E G)
            new[] { 43, 47, 50 }, // G   (G B D)
        };

        for (int bar = 0; bar < bars; bar++)
        {
            var ch = chords[bar % 4];
            float t0 = bar * 4 * beat;
            float cdur = 4 * beat;
            foreach (int note in ch) AddTone(buf, Midi(note + 12), t0, cdur, 0.085f, 0); // soft sine pad
            AddTone(buf, Midi(ch[0] - 12), t0, cdur, 0.11f, 3);                           // warm triangle bass
            // a single gentle bell note mid-bar for movement
            AddTone(buf, Midi(ch[2] + 24), t0 + 2 * beat, beat * 1.2f, 0.05f, 0);
        }
        return ToClip(buf, "music_calm");
    }

    // High-energy driving combat theme: full drum kit (four-on-the-floor kick +
    // backbeat snare + sixteenth hats), syncopated octave-accented saw bass, and a
    // build — the second half of the loop layers a square arp + a soaring lead hook,
    // so the track keeps lifting instead of just repeating. Progression Am–F–G–E
    // (i–VI–VII–V) for a tense, forward-pushing drive.
    AudioClip BuildCombatTrack()
    {
        const float bpm = 150f;
        float beat = 60f / bpm;
        int bars = 8;
        float dur = bars * 4 * beat;
        var buf = new float[(int)(Rate * dur)];

        int[] prog = { 45, 41, 43, 40 }; // Am F G E — driving cadence with a V (E) push
        for (int bar = 0; bar < bars; bar++)
        {
            int root = prog[bar % 4];
            float t0 = bar * 4 * beat;
            bool full = (bar % 8) >= 4; // build: second half of the loop layers up

            // ── drums ──
            for (int q = 0; q < 4; q++)              // four-on-the-floor kick
                AddKick(buf, t0 + q * beat, 0.55f);
            if (full) AddKick(buf, t0 + 3.5f * beat, 0.45f); // extra push into the bar-line
            AddHit(buf, t0 + 1 * beat, 0.13f, 0.30f, false); // snare backbeat (2 & 4)
            AddHit(buf, t0 + 3 * beat, 0.13f, 0.30f, false);
            int hats = full ? 16 : 8;                // hats: eighths → sixteenths in the build
            for (int s = 0; s < hats; s++)
            {
                float step = (4f * beat) / hats;
                float amp = (s % 2 == 0) ? 0.055f : 0.032f; // accent the down-beats
                AddHit(buf, t0 + s * step, 0.03f, amp, true);
            }

            // ── syncopated octave bass ──
            for (int e = 0; e < 8; e++)
            {
                int n = root - 12;
                if (e == 3 || e == 6) n = root; // octave jumps give it groove
                AddTone(buf, Midi(n), t0 + e * beat * 0.5f, beat * 0.45f, 0.16f, 2); // saw
            }

            if (full)
            {
                // sixteenth-note minor arpeggio (root, m3, 5, octave)
                int[] arp = { root, root + 3, root + 7, root + 12 };
                for (int s = 0; s < 16; s++)
                    AddTone(buf, Midi(arp[s % 4] + 12), t0 + s * beat * 0.25f, beat * 0.20f, 0.06f, 1);

                // soaring lead hook — one bright note per beat, an octave-plus above
                int[] lead = { root + 12, root + 19, root + 15, root + 17 };
                for (int q = 0; q < 4; q++)
                    AddTone(buf, Midi(lead[q]), t0 + q * beat, beat * 0.9f, 0.09f, 3); // triangle
            }
        }
        return ToClip(buf, "music_combat");
    }

    // Add one oscillator note into the buffer with a short attack/release envelope.
    // type: 0 sine, 1 square, 2 saw, 3 triangle.
    static void AddTone(float[] buf, float freq, float start, float dur, float amp, int type)
    {
        int s0 = (int)(start * Rate);
        int len = (int)(dur * Rate);
        float atk = 0.008f * Rate;
        float rel = 0.06f * Rate;
        for (int i = 0; i < len; i++)
        {
            int idx = s0 + i;
            if (idx < 0 || idx >= buf.Length) continue;
            float ph = freq * ((float)i / Rate);
            float frac = ph - Mathf.Floor(ph);
            float w;
            switch (type)
            {
                case 1: w = frac < 0.5f ? 1f : -1f; break;                 // square
                case 2: w = 2f * frac - 1f; break;                         // saw
                case 3: w = 4f * Mathf.Abs(frac - 0.5f) - 1f; break;       // triangle
                default: w = Mathf.Sin(2f * Mathf.PI * ph); break;         // sine
            }
            float env = 1f;
            if (i < atk) env = i / atk;
            else if (i > len - rel) env = (len - i) / rel;
            buf[idx] += w * amp * env;
        }
    }

    // Deterministic noise source for percussion (seeded → same track every run).
    static System.Random _rng = new System.Random(20260716);

    // Punchy kick: sine with a fast downward pitch sweep (155→45 Hz) + quick decay.
    static void AddKick(float[] buf, float start, float amp)
    {
        int s0 = (int)(start * Rate);
        int len = (int)(0.16f * Rate);
        float phase = 0f;
        for (int i = 0; i < len; i++)
        {
            int idx = s0 + i;
            if (idx < 0 || idx >= buf.Length) continue;
            float t = (float)i / Rate;
            float f = 45f + 110f * Mathf.Exp(-t * 30f); // pitch sweep gives the "thump"
            phase += f / Rate;
            float env = Mathf.Exp(-t * 16f);
            buf[idx] += Mathf.Sin(2f * Mathf.PI * phase) * amp * env;
        }
    }

    // Percussive noise hit. bright=true → crude high-pass + snappier decay for hi-hats;
    // bright=false → fuller body for a snare.
    static void AddHit(float[] buf, float start, float dur, float amp, bool bright)
    {
        int s0 = (int)(start * Rate);
        int len = (int)(dur * Rate);
        if (len < 1) len = 1;
        float prev = 0f;
        float decay = bright ? 20f : 11f;
        for (int i = 0; i < len; i++)
        {
            int idx = s0 + i;
            if (idx < 0 || idx >= buf.Length) continue;
            float n = (float)(_rng.NextDouble() * 2.0 - 1.0);
            if (bright) { float hp = n - prev; prev = n; n = hp; } // high-pass → brighter
            float env = Mathf.Exp(-((float)i / len) * decay);
            buf[idx] += n * amp * env;
        }
    }

    static AudioClip ToClip(float[] buf, string name)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = Mathf.Clamp(buf[i], -0.97f, 0.97f); // guard clipping
        var clip = AudioClip.Create(name, buf.Length, 1, Rate, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
