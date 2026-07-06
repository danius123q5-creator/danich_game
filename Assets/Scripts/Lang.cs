using UnityEngine;

/// <summary>Localization: a global RU/EN toggle (persisted). Wrap any user-facing string as
/// Lang.T("русский", "english"); it returns the right one for the current language. The setting
/// lives in the pause/main-menu settings screen. One build, switchable — no separate exe.</summary>
public static class Lang
{
    public static bool EN;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Load() { EN = PlayerPrefs.GetInt("lang_en", 0) == 1; }

    public static void Set(bool en)
    {
        EN = en;
        PlayerPrefs.SetInt("lang_en", en ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(!EN);

    /// <summary>Pick the string for the current language.</summary>
    public static string T(string ru, string en) => EN ? en : ru;
}
