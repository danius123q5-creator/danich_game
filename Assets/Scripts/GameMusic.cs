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

    public static void Spawn(Transform parent)
    {
        var go = new GameObject("GameMusic");
        if (parent != null) go.transform.SetParent(parent);
        go.AddComponent<GameMusic>();
    }

    void Start()
    {
        _calm = MakeSource(BuildCalmTrack());
        _combat = MakeSource(BuildCombatTrack());
        _calm.Play();
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

    // Faster, darker, driving: pulsing saw bass + square arpeggio + a low thump on the beat.
    AudioClip BuildCombatTrack()
    {
        const float bpm = 142f;
        float beat = 60f / bpm;
        int bars = 8;
        float dur = bars * 4 * beat;
        var buf = new float[(int)(Rate * dur)];

        int[] prog = { 45, 45, 41, 43 }; // Am Am F G — tense, repeating
        for (int bar = 0; bar < bars; bar++)
        {
            int root = prog[bar % 4];
            float t0 = bar * 4 * beat;

            // driving eighth-note bass
            for (int e = 0; e < 8; e++)
                AddTone(buf, Midi(root - 12), t0 + e * beat * 0.5f, beat * 0.42f, 0.15f, 2);

            // sixteenth-note minor arpeggio (root, m3, 5, octave)
            int[] arp = { root, root + 3, root + 7, root + 12 };
            for (int s = 0; s < 16; s++)
                AddTone(buf, Midi(arp[s % 4] + 12), t0 + s * beat * 0.25f, beat * 0.20f, 0.06f, 1);

            // low thump on each beat for drive
            for (int q = 0; q < 4; q++)
                AddTone(buf, 55f, t0 + q * beat, 0.11f, 0.22f, 0);
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

    static AudioClip ToClip(float[] buf, string name)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = Mathf.Clamp(buf[i], -0.97f, 0.97f); // guard clipping
        var clip = AudioClip.Create(name, buf.Length, 1, Rate, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
