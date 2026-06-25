using UnityEngine;

/// <summary>Player-customizable HUD options, persisted in PlayerPrefs. Read by the
/// GUI scaler (UI) and the HUD (PlayerController); edited on the Settings screen.</summary>
public static class UISettings
{
    public static float Scale = 1f;          // HUD scale multiplier (0.7..1.5)
    public static int AccentIndex = 0;       // accent colour preset
    public static float Crosshair = 1f;      // crosshair size multiplier (0 = hidden, up to 2)
    public static float PanelAlpha = 0.55f;  // HUD panel background opacity

    // Drag-to-move HUD: per-element pixel offset from its default spot.
    // 0 = HP bar, 1 = Metal, 2 = Kills counter, 3 = Tool line, 4 = Deaths counter, 5 = Oil.
    public const int ElementCount = 6;
    public static readonly Vector2[] Offsets = new Vector2[ElementCount];
    public static bool EditLayout;           // runtime only: drag-to-move mode is active

    public static readonly Color[] Accents =
    {
        new Color(0.45f, 0.8f, 1f),    // blue (default)
        new Color(0.5f, 0.9f, 0.5f),   // green
        new Color(1f, 0.8f, 0.3f),     // amber
        new Color(1f, 0.45f, 0.45f),   // red
        new Color(0.85f, 0.55f, 1f),   // violet
        new Color(0.95f, 0.95f, 0.95f),// white
    };
    public static readonly string[] AccentNames = { "Синий", "Зелёный", "Янтарь", "Красный", "Фиолет", "Белый" };
    public static Color Accent => Accents[Mathf.Clamp(AccentIndex, 0, Accents.Length - 1)];

    static bool loaded;
    public static void EnsureLoaded() { if (!loaded) Load(); }

    public static void Load()
    {
        loaded = true;
        Scale = PlayerPrefs.GetFloat("ui_scale", 1f);
        AccentIndex = PlayerPrefs.GetInt("ui_accent", 0);
        Crosshair = PlayerPrefs.GetFloat("ui_cross", 1f);
        PanelAlpha = PlayerPrefs.GetFloat("ui_alpha", 0.55f);
        for (int i = 0; i < ElementCount; i++)
            Offsets[i] = new Vector2(PlayerPrefs.GetFloat($"ui_off{i}x", 0f), PlayerPrefs.GetFloat($"ui_off{i}y", 0f));
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("ui_scale", Scale);
        PlayerPrefs.SetInt("ui_accent", AccentIndex);
        PlayerPrefs.SetFloat("ui_cross", Crosshair);
        PlayerPrefs.SetFloat("ui_alpha", PanelAlpha);
        for (int i = 0; i < ElementCount; i++)
        {
            PlayerPrefs.SetFloat($"ui_off{i}x", Offsets[i].x);
            PlayerPrefs.SetFloat($"ui_off{i}y", Offsets[i].y);
        }
        PlayerPrefs.Save();
    }

    public static void Reset()
    {
        Scale = 1f; AccentIndex = 0; Crosshair = 1f; PanelAlpha = 0.55f;
    }

    public static void ResetLayout()
    {
        for (int i = 0; i < ElementCount; i++) Offsets[i] = Vector2.zero;
    }
}
