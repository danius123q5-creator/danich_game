using UnityEngine;

/// <summary>End-User License Agreement for "ОБОРОНА ОТ ЗОМБИ" / ZombieShooter.
/// The game ships under this EULA (personal, non-commercial play; no resale, no
/// reverse-engineering; ownership stays with danich). A scrollable viewer is opened
/// from the Settings menu; GameRoot calls <see cref="Draw"/> from its OnGUI.</summary>
public static class Eula
{
    public static bool Shown;
    static Vector2 scroll;

    // Kept as a property so it re-evaluates when the player flips the language toggle.
    public static string Text => Lang.T(RU, EN);

    const string RU =
@"ЛИЦЕНЗИОННОЕ СОГЛАШЕНИЕ С КОНЕЧНЫМ ПОЛЬЗОВАТЕЛЕМ (EULA)
Игра «ОБОРОНА ОТ ЗОМБИ»
© 2026 Danich. Все права защищены.

Внимательно прочитайте настоящее Соглашение. Устанавливая, копируя или запуская
игру, вы принимаете все его условия. Если вы не согласны — не используйте игру.

1. ПРЕДОСТАВЛЕНИЕ ЛИЦЕНЗИИ.
Автор («Danich») предоставляет вам неисключительное, безвозмездное и отзывное
право установить и использовать эту игру исключительно для личного,
некоммерческого развлечения на принадлежащих вам устройствах.

2. ПРАВА СОБСТВЕННОСТИ.
Игра, её программный код, графика, звук, дизайн и название принадлежат Danich и
охраняются законом. Вы получаете право ИГРАТЬ, но не право собственности.
Указание авторства «сделано danich» является неотъемлемой частью игры.

3. ОГРАНИЧЕНИЯ. Запрещается:
   (а) продавать, сдавать в аренду, сублицензировать или распространять игру за плату;
   (б) декомпилировать, дизассемблировать, модифицировать или иным способом
       извлекать исходный код, кроме случаев, прямо разрешённых законом;
   (в) удалять или изменять указания об авторстве и уведомления о правах;
   (г) выдавать игру или её части за свою собственную разработку.

4. ОБНОВЛЕНИЯ.
Игра может загружать обновления через официальный лаунчер. Эти обновления
подпадают под действие настоящего Соглашения.

5. ОТКАЗ ОТ ГАРАНТИЙ.
Игра предоставляется «КАК ЕСТЬ», без каких-либо гарантий, явных или
подразумеваемых. Вы используете её на свой собственный риск.

6. ОГРАНИЧЕНИЕ ОТВЕТСТВЕННОСТИ.
Автор не несёт ответственности за любой прямой или косвенный ущерб, возникший
в связи с использованием или невозможностью использования игры.

7. ПРЕКРАЩЕНИЕ.
Настоящая лицензия прекращается автоматически при нарушении вами любого из её
условий. После прекращения вы обязаны удалить все копии игры.

Все права, прямо не предоставленные вам, остаются за Danich.";

    const string EN =
@"END-USER LICENSE AGREEMENT (EULA)
Game ""ZOMBIE DEFENSE"" (ОБОРОНА ОТ ЗОМБИ)
© 2026 Danich. All Rights Reserved.

Please read this Agreement carefully. By installing, copying or running the game
you accept all of its terms. If you do not agree — do not use the game.

1. GRANT OF LICENSE.
The author (""Danich"") grants you a non-exclusive, royalty-free, revocable right
to install and use this game solely for personal, non-commercial entertainment
on devices you own.

2. OWNERSHIP.
The game, its source code, graphics, sound, design and title belong to Danich and
are protected by law. You receive the right to PLAY, not ownership. The ""made by
danich"" credit is an integral part of the game.

3. RESTRICTIONS. You may not:
   (a) sell, rent, sublicense or distribute the game for a fee;
   (b) decompile, disassemble, modify or otherwise extract the source code,
       except where expressly permitted by law;
   (c) remove or alter any authorship credit or rights notice;
   (d) pass the game or any part of it off as your own work.

4. UPDATES.
The game may download updates through the official launcher. Such updates are
covered by this Agreement.

5. DISCLAIMER OF WARRANTY.
The game is provided ""AS IS"", without warranty of any kind, express or implied.
You use it at your own risk.

6. LIMITATION OF LIABILITY.
The author is not liable for any direct or indirect damages arising from the use
or inability to use the game.

7. TERMINATION.
This license terminates automatically if you breach any of its terms. Upon
termination you must delete all copies of the game.

All rights not expressly granted to you are reserved by Danich.";

    /// <summary>Full-screen scrollable EULA overlay. Call from OnGUI while <see cref="Shown"/>.
    /// Returns nothing; sets Shown=false when the player closes it.</summary>
    public static void Draw()
    {
        if (!Shown) return;

        // Dim the screen behind the panel.
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(new Rect(0f, 0f, UI.W, UI.H), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float w = Mathf.Min(760f, UI.W - 60f), h = Mathf.Min(560f, UI.H - 80f);
        float x = (UI.W - w) * 0.5f, y = (UI.H - h) * 0.5f;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0.6f, 0.9f, 0.5f);
        GUI.Label(new Rect(x, y, w, 34f), Lang.T("ЛИЦЕНЗИЯ (EULA)", "LICENSE (EULA)"), title);
        GUI.color = Color.white;

        var body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = false };
        float contentW = w - 24f;
        float textH = body.CalcHeight(new GUIContent(Text), contentW);
        Rect view = new Rect(x, y + 42f, w, h - 96f);
        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, contentW - 20f, textH));
        GUI.Label(new Rect(4f, 0f, contentW - 20f, textH), Text, body);
        GUI.EndScrollView();

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        if (GUI.Button(new Rect(x + w * 0.5f - 90f, y + h - 46f, 180f, 40f), Lang.T("Закрыть", "Close"), btn))
        {
            Shown = false;
            scroll = Vector2.zero;
        }
    }
}
