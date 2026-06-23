using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// ZombieShooter Launcher (v1.8) — pick a game version (1.0–1.8) and run it.
// Reads the GitHub releases that have a ZombieShooterSetup.exe asset, downloads the
// chosen one to %TEMP% and launches it (the setup unpacks + starts the game).
static class Launcher
{
    const string Repo = "danius123q5-creator/danich_game";
    const string Api = "https://api.github.com/repos/" + Repo + "/releases?per_page=50";

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        var form = new Form
        {
            Text = "ZombieShooter — Лаунчер 1.8",
            ClientSize = new Size(440, 250),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            BackColor = Color.FromArgb(24, 26, 32)
        };

        var title = new Label { Left = 0, Top = 16, Width = 440, Height = 32, Text = "ОБОРОНА ОТ ЗОМБИ",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(150, 220, 120),
            Font = new Font("Segoe UI", 15, FontStyle.Bold) };
        var sub = new Label { Left = 0, Top = 52, Width = 440, Height = 22, Text = "Выберите версию и нажмите «Играть»",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10) };
        var combo = new ComboBox { Left = 40, Top = 84, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11) };
        var play = new Button { Left = 40, Top = 128, Width = 360, Height = 48, Text = "Играть",
            Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.FromArgb(60, 140, 70),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var status = new Label { Left = 40, Top = 188, Width = 360, Height = 44, Text = "",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9) };
        form.Controls.AddRange(new Control[] { title, sub, combo, play, status });

        var versions = LoadVersions(); // version -> setup url, only releases that actually have a build
        foreach (var v in versions.Keys) combo.Items.Add("Версия " + v);
        if (combo.Items.Count > 0) combo.SelectedIndex = combo.Items.Count - 1; // newest by default
        else { status.Text = "Не удалось получить список версий (нет интернета?)"; play.Enabled = false; }

        play.Click += (s, e) =>
        {
            if (combo.SelectedItem == null) return;
            string ver = combo.SelectedItem.ToString().Replace("Версия ", "");
            string url; if (!versions.TryGetValue(ver, out url)) return;
            play.Enabled = false;
            status.Text = "Скачивание версии " + ver + "…";
            Application.DoEvents();
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ZombieShooterSetup_" + ver + ".exe");
                using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "ZSLauncher"); wc.DownloadFile(url, tmp); }
                Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex) { status.Text = "Не удалось: " + ex.Message; play.Enabled = true; }
        };

        Application.Run(form);
    }

    static SortedDictionary<string, string> LoadVersions()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "ZSLauncher");
                string json = wc.DownloadString(Api);
                var tags = Regex.Matches(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i].Groups[1].Value;
                    int start = tags[i].Index;
                    int end = (i + 1 < tags.Count) ? tags[i + 1].Index : json.Length;
                    // a release "has a build" if its JSON chunk mentions the setup asset
                    if (json.Substring(start, end - start).Contains("ZombieShooterSetup.exe"))
                    {
                        string ver = Regex.Replace(tag, "[^0-9.]", ""); // "danichgame1.7" -> "1.7"
                        map[ver] = "https://github.com/" + Repo + "/releases/download/" + tag + "/ZombieShooterSetup.exe";
                    }
                }
            }
        }
        catch { }
        return map;
    }
}
