using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

// ZombieShooter Launcher (v1.8) — pick a game version and run it, and see which
// version is the latest (the "update"). Reads the GitHub releases that ship a
// ZombieShooterSetup.exe, downloads the chosen one to %TEMP% and launches it.
static class Launcher
{
    const string Repo = "danius123q5-creator/danich_game";

    // Drop a "savepath.txt" (containing this launcher's own folder) into the game's install dir, so the
    // game writes its .gdf saves to a "saves" folder right next to the launcher instead of AppData.
    static void WriteSavePointer()
    {
        try
        {
            string launcherDir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(launcherDir)) return;
            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieShooter");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "savepath.txt"), launcherDir);
        }
        catch { }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // TLS 1.2 — universally supported and accepted by GitHub. (Forcing TLS 1.3 broke the
        // handshake on some machines, so we don't.)
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        // Tell the installed game to keep its saves in a "saves" folder NEXT TO THIS LAUNCHER (not the
        // buried install dir). We drop a pointer file into the game's install folder; the game reads it.
        WriteSavePointer();

        // LOCAL-FIRST (2026-07-11): if a "dist" folder of versioned setups sits next to the
        // launcher, use it — lets you pick/run builds that were never published to GitHub.
        // In local mode we skip the GitHub latest-check AND the self-update (which would pull
        // an older published launcher over this one).
        var localVersions = LoadLocalVersions();
        bool local = localVersions.Count > 0;

        // Resolve the latest version once (online mode only).
        string latestVer = null;
        if (!local)
        {
            latestVer = GetLatestVersion();
            if (SelfUpdate(latestVer)) return; // self-update only when reading from GitHub
        }

        var form = new Form
        {
            Text = "ZombieShooter — Лаунчер 2.4",
            ClientSize = new Size(440, 500),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            BackColor = Color.FromArgb(24, 26, 32)
        };

        var title = new Label { Left = 0, Top = 14, Width = 440, Height = 30, Text = "ОБОРОНА ОТ ЗОМБИ",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(150, 220, 120),
            Font = new Font("Segoe UI", 15, FontStyle.Bold) };
        var latestLbl = new Label { Left = 0, Top = 46, Width = 440, Height = 22, Text = "",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(240, 200, 90),
            Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var sub = new Label { Left = 0, Top = 72, Width = 440, Height = 20, Text = "Выберите версию и нажмите «Играть»",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9) };
        var combo = new ComboBox { Left = 40, Top = 98, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11) };
        var play = new Button { Left = 40, Top = 142, Width = 360, Height = 48, Text = "Играть",
            Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.FromArgb(60, 140, 70),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var status = new Label { Left = 40, Top = 194, Width = 360, Height = 44, Text = "",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9) };
        var clHeader = new Label { Left = 20, Top = 244, Width = 400, Height = 20, Text = "Что нового в последней версии:",
            ForeColor = Color.FromArgb(150, 220, 120), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var changelog = new TextBox { Left = 20, Top = 268, Width = 400, Height = 218, Multiline = true,
            ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(18, 20, 25),
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9), BorderStyle = BorderStyle.FixedSingle,
            Text = "Загрузка…", TabStop = false };
        form.Controls.AddRange(new Control[] { title, latestLbl, sub, combo, play, status, clHeader, changelog });

        // Changelog + version list: local dist folder wins over GitHub.
        SortedDictionary<string, string> versions; // version -> setup url (online) OR local exe path
        if (local)
        {
            versions = localVersions;
            changelog.Text = LocalChangelog().Replace("\n", "\r\n");
        }
        else
        {
            versions = LoadVersions(latestVer);
            string cl = GetLatestChangelog();
            changelog.Text = (string.IsNullOrEmpty(cl) ? "Не удалось загрузить список изменений." : cl).Replace("\n", "\r\n");
        }
        changelog.Select(0, 0);

        var keys = new List<string>(versions.Keys);
        keys.Sort(CompareVersion);     // oldest → newest
        string latest = keys.Count > 0 ? keys[keys.Count - 1] : null;

        // 2026-07-15: в ЛОКАЛ-режиме показываем ТОЛЬКО последнюю сборку. Старые локальные
        // версии (2.7…3.3) засоряли список и плодили ложное «Доступно обновление» —
        // качать-то нечего, они все локальные. Онлайн-режим по-прежнему даёт весь список.
        if (local && latest != null) keys = new List<string> { latest };

        foreach (var v in keys) combo.Items.Add("Версия " + v + (v == latest ? "   — последняя" : ""));
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = combo.Items.Count - 1; // newest by default
            latestLbl.Text = (local ? "Локальная версия: " : "Последняя версия игры: ") + latest;
        }
        else
        {
            latestLbl.Text = local ? "В папке dist нет сборок." : "Не удалось получить список версий (нет интернета?)";
            play.Enabled = false;
        }

        // Tell the player when they're on the newest vs an older build.
        combo.SelectedIndexChanged += (s, e) =>
        {
            string v = keys[combo.SelectedIndex];
            status.ForeColor = Color.Gainsboro;
            status.Text = v == latest ? "Это последняя версия." : "Доступно обновление: " + latest + ".";
        };
        if (combo.SelectedIndex >= 0) status.Text = "Это последняя версия.";

        play.Click += (s, e) =>
        {
            if (combo.SelectedIndex < 0) return;
            string ver = keys[combo.SelectedIndex];

            // LOCAL mode: the map value IS a local setup path — run it directly, no download.
            if (local)
            {
                status.ForeColor = Color.Gainsboro;
                status.Text = "Закрываю старую игру и ставлю версию " + ver + "…";
                KillRunningGame(); // so the setup can overwrite a running install
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = versions[ver], UseShellExecute = true });
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    status.ForeColor = Color.FromArgb(230, 120, 110);
                    status.Text = "Не удалось запустить: " + ex.Message;
                }
                return;
            }

            string url = versions[ver];
            play.Enabled = false;
            combo.Enabled = false;
            status.ForeColor = Color.Gainsboro;
            status.Text = "Скачивание версии " + ver + "… 0%";

            // Download ASYNCHRONOUSLY so the UI thread keeps pumping messages — otherwise a
            // large setup blocks the window and Windows shows "(Не отвечает)". WebClient's
            // event-based async posts these callbacks back on the UI thread.
            string tmp = Path.Combine(Path.GetTempPath(), "ZombieShooterSetup_" + ver + ".exe");

            // Retry the download a few times — transient network/TLS hiccups are common.
            Action<int> attempt = null;
            attempt = n =>
            {
                string tag = n > 1 ? "(попытка " + n + "/3) " : "";
                status.ForeColor = Color.Gainsboro;
                status.Text = "Скачивание версии " + ver + "… " + tag + "0%";
                var wc = new WebClient();
                wc.Headers.Add("User-Agent", "ZSLauncher");
                wc.DownloadProgressChanged += (ws, we) =>
                    status.Text = "Скачивание версии " + ver + "… " + tag + we.ProgressPercentage + "%";
                wc.DownloadFileCompleted += (ws, we) =>
                {
                    wc.Dispose();
                    if (we.Cancelled) { play.Enabled = true; combo.Enabled = true; return; }
                    if (we.Error != null)
                    {
                        if (n < 3) { attempt(n + 1); return; } // ride out the hiccup
                        status.ForeColor = Color.FromArgb(230, 120, 110);
                        status.Text = "Не удалось скачать (сеть/TLS): " + we.Error.Message;
                        play.Enabled = true; combo.Enabled = true;
                        return;
                    }
                    status.Text = "Закрываю старую игру и ставлю версию " + ver + "…";
                    KillRunningGame(); // so the setup can overwrite a running install
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true });
                        Application.Exit();
                    }
                    catch (Exception ex)
                    {
                        status.ForeColor = Color.FromArgb(230, 120, 110);
                        status.Text = "Не удалось запустить: " + ex.Message;
                        play.Enabled = true; combo.Enabled = true;
                    }
                };
                try { wc.DownloadFileAsync(new Uri(url), tmp); }
                catch (Exception ex)
                {
                    wc.Dispose();
                    if (n < 3) { attempt(n + 1); return; }
                    status.ForeColor = Color.FromArgb(230, 120, 110);
                    status.Text = "Не удалось: " + ex.Message;
                    play.Enabled = true; combo.Enabled = true;
                }
            };
            attempt(1);
        };

        Application.Run(form);
    }

    // Build the version list. Releases from 2.4 on use a BARE version tag ("2.5") and a versioned
    // setup asset ("ZombieShooterSetup_2.5.exe"); older danichgame* releases used an unversioned
    // setup and 404 under this scheme, so the list starts at 2.4 and runs up to the latest.
    static SortedDictionary<string, string> LoadVersions(string latest)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(latest)) return map;

        var lp = latest.Split('.');
        int lMaj = ParseInt(lp.Length > 0 ? lp[0] : "0");
        int lMin = ParseInt(lp.Length > 1 ? lp[1] : "0");
        for (int maj = 2; maj <= lMaj; maj++)
        {
            int minStart = (maj == 2) ? 4 : 0;   // versioned-setup scheme begins at 2.4
            int minEnd = (maj == lMaj) ? lMin : 9;
            for (int min = minStart; min <= minEnd; min++)
            {
                string v = maj + "." + min;
                map[v] = "https://github.com/" + Repo + "/releases/download/" + v +
                         "/ZombieShooterSetup_" + v + ".exe";
            }
        }
        // The maj.min loop misses PATCH versions (e.g. 3.1.1). Always include the exact latest tag so
        // three-part releases show up and download from their own versioned setup asset.
        if (!string.IsNullOrEmpty(latest) && !map.ContainsKey(latest))
            map[latest] = "https://github.com/" + Repo + "/releases/download/" + latest +
                          "/ZombieShooterSetup_" + latest + ".exe";
        return map;
    }

    // Scan a "dist" folder next to the launcher for versioned setups
    // (ZombieShooterSetup_X.Y[.Z].exe). Maps version -> local exe path. Empty map => no
    // local builds, so the launcher falls back to GitHub. 2026-07-11.
    static SortedDictionary<string, string> LoadLocalVersions()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(dir)) return map;
            string dist = Path.Combine(dir, "dist");
            if (!Directory.Exists(dist)) return map;
            foreach (string f in Directory.GetFiles(dist, "ZombieShooterSetup_*.exe"))
            {
                string name = Path.GetFileNameWithoutExtension(f); // ZombieShooterSetup_3.3.1
                int us = name.IndexOf('_');
                if (us < 0) continue;
                string v = name.Substring(us + 1).Trim();
                if (v.Length > 0) map[v] = f;
            }
        }
        catch { }
        return map;
    }

    // Changelog shown in local mode: read dist\changelog.txt if present, else a generic note.
    static string LocalChangelog()
    {
        try
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (!string.IsNullOrEmpty(dir))
            {
                string cl = Path.Combine(Path.Combine(dir, "dist"), "changelog.txt");
                if (File.Exists(cl)) return File.ReadAllText(cl);
            }
        }
        catch { }
        return "Локальный режим: версии берутся из папки dist рядом с лаунчером.\n" +
               "Полный список изменений — в релизе на GitHub.";
    }

    // Resolve the latest version. Primary: follow the github.com releases/latest redirect and
    // read the FINAL url (.../tag/danichgameX.Y) — no api.github.com, no rate limits. Fallback:
    // api.github.com latest-release tag_name. Tries each, twice.
    static string GetLatestVersion()
    {
        string web = "https://github.com/" + Repo + "/releases/latest";
        string api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string v = TryLatest(web);
            if (!string.IsNullOrEmpty(v)) return v;
            v = TryLatest(api);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return null;
    }

    static string TryLatest(string url)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (ZSLauncher)";
            req.Accept = "application/vnd.github+json";
            req.AllowAutoRedirect = true;   // follow to the tag page
            req.Timeout = 9000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                // Web path: the final URL is .../releases/tag/danichgameX.Y
                string v = VersionFromTag(resp.ResponseUri != null ? resp.ResponseUri.AbsoluteUri : "");
                if (!string.IsNullOrEmpty(v) && v.Contains(".")) return v;
                // API path: parse "tag_name":"danichgameX.Y" from the JSON body
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    string body = sr.ReadToEnd();
                    int i = body.IndexOf("\"tag_name\"");
                    if (i >= 0)
                    {
                        int q1 = body.IndexOf('"', i + 10);
                        int q2 = q1 >= 0 ? body.IndexOf('"', q1 + 1) : -1;
                        if (q2 > q1) return VersionFromTag(body.Substring(q1 + 1, q2 - q1 - 1));
                    }
                }
            }
        }
        catch { }
        return null;
    }

    static string VersionFromTag(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        for (int i = s.Length - 1; i >= 0; i--)
        {
            char c = s[i];
            if (char.IsDigit(c) || c == '.') sb.Insert(0, c);
            else if (sb.Length > 0) break;
        }
        return sb.ToString().Trim('.');
    }

    // Fetch the latest release's notes (the "body" field) from the GitHub API so the launcher
    // can show a changelog. Best-effort — returns "" on any failure (offline / rate-limited).
    static string GetLatestChangelog()
    {
        string api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(api);
            req.UserAgent = "Mozilla/5.0 (ZSLauncher)";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 9000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                return ExtractJsonString(sr.ReadToEnd(), "body");
            }
        }
        catch { return ""; }
    }

    // Pull one top-level JSON string field's value and unescape it (\n, \", \uXXXX, …).
    // Tiny hand-rolled reader so the launcher needs no JSON library.
    static string ExtractJsonString(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return "";
        string key = "\"" + field + "\"";
        int i = json.IndexOf(key);
        if (i < 0) return "";
        int colon = json.IndexOf(':', i + key.Length);
        if (colon < 0) return "";
        int q1 = json.IndexOf('"', colon);
        if (q1 < 0) return "";
        var sb = new StringBuilder();
        for (int p = q1 + 1; p < json.Length; p++)
        {
            char c = json[p];
            if (c == '\\' && p + 1 < json.Length)
            {
                char n = json[++p];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (p + 4 < json.Length)
                        {
                            int code;
                            if (int.TryParse(json.Substring(p + 1, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            p += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            else if (c == '"') break;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Self-update: download the latest launcher; if it differs from us byte-for-byte, write a
    // tiny batch that waits for us to exit, swaps the exe and relaunches. Returns true when an
    // update is in progress (the caller should exit immediately so the file can be replaced).
    static bool SelfUpdate(string latest)
    {
        if (string.IsNullOrEmpty(latest)) return false;
        try
        {
            string url = "https://github.com/" + Repo + "/releases/download/" + latest + "/ZombieShooterLauncher.exe";
            string self = Application.ExecutablePath;
            string tmp = Path.Combine(Path.GetTempPath(), "ZSLauncher_new.exe");
            using (var wc = new TimedWebClient()) { wc.Headers.Add("User-Agent", "ZSLauncher"); wc.DownloadFile(url, tmp); }
            if (!File.Exists(tmp)) return false;
            if (BytesEqual(File.ReadAllBytes(self), File.ReadAllBytes(tmp))) { try { File.Delete(tmp); } catch { } return false; }

            string bat = Path.Combine(Path.GetTempPath(), "zs_launcher_update.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 2 >nul\r\n" +
                ":wait\r\n" +
                "copy /Y \"" + tmp + "\" \"" + self + "\" >nul 2>&1\r\n" +
                "if errorlevel 1 ( ping 127.0.0.1 -n 2 >nul & goto wait )\r\n" +
                "start \"\" \"" + self + "\"\r\n" +
                "del \"" + tmp + "\" >nul 2>&1\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n");
            Process.Start(new ProcessStartInfo { FileName = bat, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            return true;
        }
        catch { return false; }
    }

    // Kill any running game so the setup can overwrite the install cleanly. A running
    // ZombieShooter.exe locks its files, so a reinstall silently leaves the OLD build in
    // place (the classic "I updated but nothing changed" bug). Does NOT match the launcher
    // itself ("ZombieShooterLauncher"). 2026-07-12.
    static void KillRunningGame()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("ZombieShooter"))
            {
                try { p.Kill(); p.WaitForExit(4000); } catch { }
            }
        }
        catch { }
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // WebClient with a sane timeout (the default is ~100s).
    class TimedWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var r = base.GetWebRequest(address);
            if (r != null) r.Timeout = 10000;
            return r;
        }
    }

    // Compare dotted version strings numerically (1.10 > 1.9).
    static int CompareVersion(string a, string b)
    {
        string[] pa = a.Split('.'), pb = b.Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int ai = i < pa.Length ? ParseInt(pa[i]) : 0;
            int bi = i < pb.Length ? ParseInt(pb[i]) : 0;
            if (ai != bi) return ai.CompareTo(bi);
        }
        return 0;
    }

    static int ParseInt(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
}
