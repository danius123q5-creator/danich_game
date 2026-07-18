using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

// ZombieShooter .gdf Save Editor — a tiny companion tool for "Game Danich Format" saves.
// Opens a slotN.gdf, exposes the readable fields (wave / metal / oil / hp / kills / …) as friendly
// inputs, and keeps the machine data (builds / refineries / mines) verbatim. The raw text box is the
// single source of truth; the friendly fields just poke lines in it. Compiled by Build-SaveEditor.ps1.
static class SaveEditor
{
    static TextBox raw;
    static Label pathLbl, status;
    static string currentPath;
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // key -> control (textbox or checkbox), rebuilt from the raw text.
    static readonly List<Action> pullFromRaw = new List<Action>();

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new Form
        {
            Text = "ZombieShooter — Редактор сейвов (.gdf)",
            ClientSize = new Size(760, 620),
            StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new Size(700, 560),
            BackColor = Color.FromArgb(24, 26, 32)
        };

        var title = new Label { Left = 12, Top = 10, Width = 736, Height = 26, Text = "Редактор сейвов ОБОРОНА ОТ ЗОМБИ",
            ForeColor = Color.FromArgb(150, 220, 120), Font = new Font("Segoe UI", 13, FontStyle.Bold) };
        pathLbl = new Label { Left = 12, Top = 40, Width = 736, Height = 20, Text = "Файл не открыт",
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 8) };

        var openBtn = Button("Открыть .gdf", 12, 66, 120);
        var saveBtn = Button("Сохранить", 140, 66, 110);
        var saveAsBtn = Button("Сохранить как…", 258, 66, 130);
        openBtn.Click += (s, e) => OpenFile();
        saveBtn.Click += (s, e) => Save(currentPath);
        saveAsBtn.Click += (s, e) => Save(null);

        // ---- friendly fields ----
        var fields = new GroupBox { Left = 12, Top = 104, Width = 360, Height = 380, Text = "Поля",
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9) };

        int y = 26;
        AddInt(fields, ref y, "Волна", "wave");
        AddInt(fields, ref y, "Металл", "p1_metal");
        AddInt(fields, ref y, "Нефть", "p1_oil");
        AddInt(fields, ref y, "HP игрока", "p1_hp");
        AddInt(fields, ref y, "Убийства", "p1_kills");
        AddInt(fields, ref y, "Смерти", "p1_deaths");
        AddInt(fields, ref y, "HP раздатчика", "dispenser_hp");
        AddInt(fields, ref y, "Ур. раздатчика", "dispenser_lvl");
        AddBool(fields, ref y, "Бесконечный режим", "infinite");
        AddBool(fields, ref y, "Ночь", "night");

        // ---- raw text (source of truth) ----
        var rawLbl = new Label { Left = 384, Top = 104, Width = 364, Height = 20, Text = "Сырой текст (машинные данные построек — не трогай, если не уверен):",
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 8) };
        raw = new TextBox { Left = 384, Top = 126, Width = 364, Height = 358, Multiline = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 9), AcceptsTab = true };
        raw.TextChanged += (s, e) => PullFields();

        status = new Label { Left = 12, Top = 494, Width = 736, Height = 40, Text = "Открой файл slotN.gdf, чтобы начать. Правь поля слева — они меняют текст справа. Сохрани.",
            ForeColor = Color.FromArgb(180, 200, 180), Font = new Font("Segoe UI", 9) };

        form.Controls.AddRange(new Control[] { title, pathLbl, openBtn, saveBtn, saveAsBtn, fields, rawLbl, raw, status });
        Application.Run(form);
    }

    static Button Button(string text, int x, int y, int w)
    {
        return new Button { Text = text, Left = x, Top = y, Width = w, Height = 30,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66),
            Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    }

    static void AddInt(GroupBox box, ref int y, string label, string key)
    {
        var lab = new Label { Left = 12, Top = y + 3, Width = 150, Height = 22, Text = label, ForeColor = Color.Gainsboro };
        var tb = new TextBox { Left = 168, Top = y, Width = 170, Height = 24, BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.White };
        box.Controls.Add(lab); box.Controls.Add(tb);
        tb.Leave += (s, e) => { SetLine(key, tb.Text.Trim(), " "); };
        tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) SetLine(key, tb.Text.Trim(), " "); };
        pullFromRaw.Add(() => { if (!tb.Focused) tb.Text = GetLine(key); });
        y += 32;
    }

    static void AddBool(GroupBox box, ref int y, string label, string key)
    {
        var cb = new CheckBox { Left = 12, Top = y, Width = 326, Height = 24, Text = label, ForeColor = Color.Gainsboro };
        box.Controls.Add(cb);
        cb.CheckedChanged += (s, e) => { if (cb.Focused) SetLine(key, cb.Checked ? "1" : "0", " "); };
        pullFromRaw.Add(() => { if (!cb.Focused) cb.Checked = GetLine(key) == "1"; });
        y += 30;
    }

    static bool suppressPull;

    static void OpenFile()
    {
        using (var dlg = new OpenFileDialog { Filter = "GDF saves (*.gdf)|*.gdf|Все файлы (*.*)|*.*", InitialDirectory = GuessSavesDir() })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                suppressPull = true;
                raw.Text = File.ReadAllText(dlg.FileName).Replace("\r\n", "\n").Replace("\n", "\r\n");
                suppressPull = false;
                currentPath = dlg.FileName;
                pathLbl.Text = currentPath;
                PullFields();
                status.ForeColor = Color.FromArgb(180, 200, 180);
                status.Text = "Открыто: " + Path.GetFileName(currentPath);
            }
            catch (Exception ex) { Err("Не удалось открыть: " + ex.Message); }
        }
    }

    static void Save(string path)
    {
        if (string.IsNullOrEmpty(raw.Text)) { Err("Нечего сохранять — открой файл."); return; }
        if (string.IsNullOrEmpty(path))
        {
            using (var dlg = new SaveFileDialog { Filter = "GDF saves (*.gdf)|*.gdf", InitialDirectory = GuessSavesDir(),
                FileName = string.IsNullOrEmpty(currentPath) ? "slot0.gdf" : Path.GetFileName(currentPath) })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }
        }
        try
        {
            File.WriteAllText(path, raw.Text.Replace("\r\n", "\n"));
            currentPath = path; pathLbl.Text = path;
            status.ForeColor = Color.FromArgb(150, 220, 120);
            status.Text = "Сохранено: " + Path.GetFileName(path);
        }
        catch (Exception ex) { Err("Не удалось сохранить: " + ex.Message); }
    }

    static void Err(string msg) { status.ForeColor = Color.FromArgb(230, 120, 110); status.Text = msg; }

    // Refresh the friendly fields from the raw text.
    static void PullFields()
    {
        if (suppressPull) return;
        foreach (var pull in pullFromRaw) pull();
    }

    // Read a key's value from the raw text ("key value" or "key=value"). Returns "" if absent.
    static string GetLine(string key)
    {
        foreach (var line in raw.Lines)
        {
            string k, v;
            if (SplitLine(line, out k, out v) && k == key) return v;
        }
        return "";
    }

    // Set a key's value in the raw text, preserving the line's existing separator. Adds the line if
    // it's missing (using defaultSep). Keeps the caret / other lines intact.
    static void SetLine(string key, string value, string defaultSep)
    {
        var lines = new List<string>(raw.Lines);
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string k, v;
            if (SplitLine(lines[i], out k, out v) && k == key)
            {
                string sep = lines[i].Contains("=") ? "=" : " ";
                lines[i] = key + sep + value;
                found = true;
                break;
            }
        }
        if (!found) lines.Add(key + defaultSep + value);

        suppressPull = true;
        int caret = raw.SelectionStart;
        raw.Text = string.Join("\r\n", lines.ToArray());
        raw.SelectionStart = Math.Min(caret, raw.Text.Length);
        suppressPull = false;
        PullFields();
    }

    static bool SplitLine(string line, out string key, out string val)
    {
        key = ""; val = "";
        if (string.IsNullOrEmpty(line)) return false;
        int eq = line.IndexOf('=');
        if (eq >= 0) { key = line.Substring(0, eq).Trim(); val = line.Substring(eq + 1); return true; }
        int sp = line.IndexOf(' ');
        if (sp >= 0) { key = line.Substring(0, sp).Trim(); val = line.Substring(sp + 1).Trim(); return true; }
        key = line.Trim(); return true;
    }

    // Try the likely saves folders: next to this editor, the game install dir, then AppData\LocalLow.
    static string GuessSavesDir()
    {
        try
        {
            string here = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "saves");
            if (Directory.Exists(here)) return here;
            string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieShooter", "saves");
            if (Directory.Exists(install)) return install;
            string low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "..", "LocalLow", "DefaultCompany", "My project (2)", "saves");
            if (Directory.Exists(low)) return Path.GetFullPath(low);
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }
}
