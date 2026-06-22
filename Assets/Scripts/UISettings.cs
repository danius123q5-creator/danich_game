using UnityEngine;

/// <summary>Player-customizable HUD options, persisted in PlayerPrefs. Read by the
/// GUI scaler (UI) and the HUD (PlayerController); edited on the Settings screen.</summary>
public static class UISettings
{
    public static float Scale = 1f;          // HUD scale multiplier (0.7..1.5)
    public static int AccentIndex = 0;       // accent colour preset
    public static float Crosshair = 1f;      // crosshair size multiplier (0 = hidden, up to 2)
    public static float PanelAlpha = 0.55f;  // HUD panel background opacity

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
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("ui_scale", Scale);
        PlayerPrefs.SetInt("ui_accent", AccentIndex);
        PlayerPrefs.SetFloat("ui_cross", Crosshair);
        PlayerPrefs.SetFloat("ui_alpha", PanelAlpha);
        PlayerPrefs.Save();
    }

    public static void Reset()
    {
        Scale = 1f; AccentIndex = 0; Crosshair = 1f; PanelAlpha = 0.55f;
    }
}
