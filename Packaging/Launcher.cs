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

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // TLS 1.2 — universally supported and accepted by GitHub. (Forcing TLS 1.3 broke the
        // handshake on some machines, so we don't.)
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        var form = new Form
        {
            Text = "ZombieShooter — Лаунчер 1.8",
            ClientSize = new Size(440, 270),
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
        var status = new Label { Left = 40, Top = 200, Width = 360, Height = 60, Text = "",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9) };
        form.Controls.AddRange(new Control[] { title, latestLbl, sub, combo, play, status });

        var versions = LoadVersions(); // version -> setup url, only releases that ship a build
        var keys = new List<string>(versions.Keys);
        keys.Sort(CompareVersion);     // oldest → newest
        string latest = keys.Count > 0 ? keys[keys.Count - 1] : null;

        foreach (var v in keys) combo.Items.Add("Версия " + v + (v == latest ? "   — последняя" : ""));
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = combo.Items.Count - 1; // newest by default
            latestLbl.Text = "Последняя версия игры: " + latest;
        }
        else
        {
            latestLbl.Text = "Не удалось получить список версий (нет интернета?)";
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
                    status.Text = "Запуск установщика версии " + ver + "…";
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

    // Build the version list from the github.com releases area (NOT api.github.com, which
    // rate-limits): resolve the latest via the releases/latest redirect, then list every
    // version up to it with a direct releases/download URL.
    static SortedDictionary<string, string> LoadVersions()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string latest = GetLatestVersion();
        if (string.IsNullOrEmpty(latest)) return map;

        var lp = latest.Split('.');
        int lMaj = ParseInt(lp.Length > 0 ? lp[0] : "0");
        int lMin = ParseInt(lp.Length > 1 ? lp[1] : "0");
        for (int maj = 1; maj <= lMaj; maj++)
        {
            int minStart = (maj == 1) ? 1 : 0;  // first shipped setup is 1.1
            int minEnd = (maj == lMaj) ? lMin : 9;
            for (int min = minStart; min <= minEnd; min++)
            {
                string v = maj + "." + min;
                map[v] = "https://github.com/" + Repo + "/releases/download/danichgame" + v + "/ZombieShooterSetup.exe";
            }
        }
        return map;
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
