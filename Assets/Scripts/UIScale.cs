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

    /// <summary>Set the GUI scale matrix for this OnGUI pass. Returns nothing; read
    /// UI.W / UI.H / UI.Scale afterwards.</summary>
    public static void Begin()
    {
        float s = Screen.height / RefHeight;
        if (s <= 0f) s = 1f;
        Scale = s;
        H = RefHeight;
        W = Screen.width / s;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
    }
}
