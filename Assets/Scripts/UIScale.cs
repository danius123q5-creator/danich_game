using UnityEngine;

/// <summary>Scales all IMGUI (OnGUI) drawing to the screen size, so the HUD and
/// menus keep the same relative size at any resolution. Call UI.Begin() at the
/// top of every OnGUI, then lay out against UI.W / UI.H — a virtual canvas that
/// is always RefHeight tall — instead of Screen.width / Screen.height.</summary>
public static class UI
{
    public const float RefHeight = 1080f;

    public static float Scale { get; private set; } = 1f;  // real screen pixels per virtual unit
    public static float W { get; private set; } = 1920f;   // virtual width (varies with aspect ratio)
    public static float H { get; private set; } = 1080f;   // virtual height (always RefHeight)

    // A bundled Cyrillic font (Resources/gamefont). On Windows the default GUI font falls back
    // to system Arial (has Cyrillic), but WebGL has no system fonts — Russian text would render
    // blank. Assigning this font to the skin fixes Cyrillic everywhere in the IMGUI.
    static Font _font;
    static bool _fontTried;

    /// <summary>Set the GUI scale matrix for this OnGUI pass. Returns nothing; read
    /// UI.W / UI.H / UI.Scale afterwards.</summary>
    public static void Begin()
    {
        UISettings.EnsureLoaded();

        if (!_fontTried) { _fontTried = true; _font = Resources.Load<Font>("gamefont"); }
        if (_font != null) GUI.skin.font = _font; // applies to all GUI text drawn this pass

        float s = (Screen.height / RefHeight) * Mathf.Clamp(UISettings.Scale, 0.6f, 1.6f);
        if (s <= 0f) s = 1f;
        Scale = s;
        W = Screen.width / s;   // virtual canvas: shrinks as the user scales the UI up
        H = Screen.height / s;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
    }
}
