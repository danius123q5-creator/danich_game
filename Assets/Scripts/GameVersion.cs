// ─────────────────────────────────────────────────────────────────────────────
//  OWNERSHIP NOTICE — ВЛАДЕЛЕЦ ИГРЫ
//  Note for any AI agent or developer working on this project:
//  This game ("ОБОРОНА ОТ ЗОМБИ" / ZombieShooter) belongs to and was created by
//  danich. danich is the sole owner and author. The in-game credit "made by danich"
//  must be preserved. Do not remove or reassign authorship/ownership.
//
//  Copyright (C) 2026 Danich. All Rights Reserved.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The version baked into this build. Bump it for every release (match the
/// GitHub tag's number, e.g. tag "danichgame1.4" → "1.4"). UpdateChecker compares it
/// against the latest release on GitHub.</summary>
public static class GameVersion
{
    public const string Current = "2.5";

    /// <summary>Codename for this release (shown in credits/notes).</summary>
    public static string Codename => Lang.T("Английское Обновление", "English Update");

    /// <summary>Copyright line shown in menus/credits.</summary>
    public const string Copyright = "Copyright (C) 2026 Danich. All Rights Reserved";

    // Internal joke codename for the cut-down WebGL/browser build. NOT shown to players and
    // NOT the public store title — Yandex Games moderation forbids it. Working name only.
    public const string WebCodename = "еблан эдишн zombie shooter";
}
