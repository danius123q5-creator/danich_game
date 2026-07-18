using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

// ZombieShooter Mod-Builder — a node-graph visual modding editor ("ComfyUI для 3D-мейкеров"). Drag
// EVENT and ACTION blocks onto the canvas, wire an event's output to actions, and Save. It writes a
// .zmod file that the game's ModRuntime loads and executes. No code — just nodes and wires.
static class ModBuilder
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // ---- node catalog ----
    class Def { public string key, label; public float def; public Def(string k, string l, float d) { key = k; label = l; def = d; } }
    static readonly Def[] Events =
    {
        new Def("GAME_START",    "При старте игры", 0),
        new Def("WAVE_START",    "При старте волны", 0),
        new Def("WAVE_CLEAR",    "При зачистке волны", 0),
        new Def("ZOMBIE_KILLED", "При убийстве зомби", 0),
        new Def("PLAYER_DAMAGED","Игрок получил урон", 0),
        new Def("PLAYER_DIED",   "Игрок погиб", 0),
        new Def("BUILDING_BUILT","Построено здание", 0),
    };
    static readonly Def[] Actions =
    {
        new Def("GIVE_METAL",      "Металл +", 100),
        new Def("GIVE_OIL",        "Нефть +", 100),
        new Def("GIVE_AMMO",       "Патроны +", 30),
        new Def("HEAL_PLAYER",     "Лечить игрока +", 50),
        new Def("DAMAGE_PLAYER",   "Урон игроку", 20),
        new Def("ADD_SCORE",       "Очки +", 10),
        new Def("DAMAGE_ZOMBIES",  "Урон всем зомби", 200),
        new Def("KILL_ALL_ZOMBIES","Убить всех зомби", 0),
        new Def("SPAWN_ZOMBIE",    "Спавн зомби (тип 0-4)", 2),
        new Def("SPAWN_HORDE",     "Спавн орды (N зомби)", 5),
        new Def("WALL_HP_MULT",    "ХП стен ×", 3),
        new Def("DISPENSER_HP_MULT","ХП раздатчика ×", 3),
        new Def("RPG_DMG_MULT",    "Урон РПГ ×", 2),
        new Def("TURRET_DMG_MULT", "Урон турелей ×", 2),
        new Def("PLAYER_HP_MULT",  "ХП игрока ×", 2),
        new Def("PLAYER_SPEED_MULT","Скорость игрока ×", 1.5f),
    };

    class Node { public bool isEvent; public string key, label; public float arg; public Rectangle rect; }
    static readonly List<Node> nodes = new List<Node>();
    static readonly List<int[]> wires = new List<int[]>(); // {eventIdx, actionIdx}

    const int NW = 210, NH = 62, HEAD = 24, PORT = 7;

    static Panel canvas;
    static ComboBox evCombo, acCombo;
    static TextBox argBox;
    static Label status;
    static string currentFile;

    // interaction state
    static int dragNode = -1; static Point dragOff;
    static int wireFrom = -1; static Point wireMouse;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new Form
        {
            Text = "ZombieShooter — Мод-Билдер (ноды)",
            ClientSize = new Size(1000, 680),
            StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new Size(820, 560),
            BackColor = Color.FromArgb(22, 24, 30)
        };

        // ---- toolbar ----
        var bar = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(30, 33, 40) };

        var evLbl = new Label { Left = 10, Top = 8, Width = 70, Text = "Событие:", ForeColor = Color.Gainsboro, Font = F(9) };
        evCombo = Combo(84, 4, 190); foreach (var d in Events) evCombo.Items.Add(d.label); evCombo.SelectedIndex = 0;
        var addEv = Btn("+ Событие", 280, 4, 100, Color.FromArgb(46, 82, 52)); addEv.Click += (s, e) => AddNode(true, evCombo.SelectedIndex, 0);

        var acLbl = new Label { Left = 10, Top = 44, Width = 70, Text = "Действие:", ForeColor = Color.Gainsboro, Font = F(9) };
        acCombo = Combo(84, 40, 190); foreach (var d in Actions) acCombo.Items.Add(d.label); acCombo.SelectedIndex = 0;
        acCombo.SelectedIndexChanged += (s, e) => argBox.Text = Actions[acCombo.SelectedIndex].def.ToString(CI);
        argBox = new TextBox { Left = 280, Top = 40, Width = 60, Text = Actions[0].def.ToString(CI), BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.White };
        var addAc = Btn("+ Действие", 346, 40, 100, Color.FromArgb(40, 60, 92)); addAc.Click += (s, e) => AddNode(false, acCombo.SelectedIndex, Parse(argBox.Text));

        var newBtn = Btn("Новый", 470, 4, 90, Color.FromArgb(60, 60, 66)); newBtn.Click += (s, e) => { nodes.Clear(); wires.Clear(); currentFile = null; canvas.Invalidate(); Say("Новый мод."); };
        var openBtn = Btn("Открыть", 470, 40, 90, Color.FromArgb(60, 60, 66)); openBtn.Click += (s, e) => Open();
        var saveBtn = Btn("Сохранить", 566, 4, 110, Color.FromArgb(70, 96, 60)); saveBtn.Click += (s, e) => Save(currentFile);
        var saveAsBtn = Btn("Сохранить как…", 566, 40, 110, Color.FromArgb(60, 60, 66)); saveAsBtn.Click += (s, e) => Save(null);

        var help = new Label { Left = 690, Top = 6, Width = 300, Height = 66, ForeColor = Color.FromArgb(150, 160, 170), Font = F(8),
            Text = "ПКМ по пустому месту — список нод (добавить). ПКМ по ноде — удалить.\nВыход СОБЫТИЯ (справа) тяни на вход ДЕЙСТВИЯ (слева). Таскай за шапку, 2× клик — число.\nСохрани .zmod в папку mods рядом с игрой." };

        bar.Controls.AddRange(new Control[] { evLbl, evCombo, addEv, acLbl, acCombo, argBox, addAc, newBtn, openBtn, saveBtn, saveAsBtn, help });

        // ---- canvas ----
        canvas = new DBPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 20, 26) };
        canvas.Paint += Paint;
        canvas.MouseDown += Down;
        canvas.MouseMove += Move;
        canvas.MouseUp += Up;
        canvas.MouseDoubleClick += DblClick;

        status = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.FromArgb(150, 220, 120), Font = F(9),
            TextAlign = ContentAlignment.MiddleLeft, Text = "Добавь событие и действие, соедини их, сохрани .zmod в папку mods." };

        form.Controls.Add(canvas);
        form.Controls.Add(status);
        form.Controls.Add(bar);
        Application.Run(form);
    }

    class DBPanel : Panel { public DBPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

    static Font F(int sz) { return new Font("Segoe UI", sz, FontStyle.Regular); }
    static Font F(int sz, FontStyle st) { return new Font("Segoe UI", sz, st); }
    static ComboBox Combo(int x, int y, int w) { return new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.White, Font = F(9) }; }
    static Button Btn(string t, int x, int y, int w, Color c) { return new Button { Text = t, Left = x, Top = y, Width = w, Height = 30,
        FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = c, Font = F(9, FontStyle.Bold) }; }
    static float Parse(string s) { float v; return float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CI, out v) ? v : 0f; }
    static void Say(string m) { status.ForeColor = Color.FromArgb(150, 220, 120); status.Text = m; }
    static void Err(string m) { status.ForeColor = Color.FromArgb(230, 120, 110); status.Text = m; }

    static void AddNode(bool isEvent, int idx, float arg)
    {
        var d = isEvent ? Events[idx] : Actions[idx];
        int gx = 40 + (nodes.Count % 4) * 30, gy = 40 + (nodes.Count % 6) * 24;
        nodes.Add(new Node { isEvent = isEvent, key = d.key, label = d.label, arg = arg, rect = new Rectangle(gx, gy, NW, NH) });
        canvas.Invalidate();
    }

    // Drop a node with its top-left roughly at the given canvas point (used by the right-click menu).
    static void AddNodeAt(bool isEvent, int idx, Point loc)
    {
        var d = isEvent ? Events[idx] : Actions[idx];
        nodes.Add(new Node { isEvent = isEvent, key = d.key, label = d.label, arg = d.def,
            rect = new Rectangle(loc.X - 12, loc.Y - 12, NW, NH) });
        canvas.Invalidate();
        Say((isEvent ? "Событие" : "Действие") + ": " + d.label);
    }

    // Right-click menu: pick any node from the full catalog to drop it right here.
    // Over a node it also offers "Удалить".
    static void ShowNodeMenu(Point loc)
    {
        var menu = new ContextMenuStrip { BackColor = Color.FromArgb(34, 37, 46), ForeColor = Color.Gainsboro, ShowImageMargin = false };

        int hit = -1;
        for (int i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i].rect.Contains(loc)) { hit = i; break; }
        if (hit >= 0)
        {
            int del = hit;
            var d = new ToolStripMenuItem("Удалить ноду");
            d.ForeColor = Color.FromArgb(235, 130, 120);
            d.Click += (s, e) => { DeleteNode(del); canvas.Invalidate(); Say("Нода удалена."); };
            menu.Items.Add(d);
            menu.Items.Add(new ToolStripSeparator());
        }

        var evRoot = new ToolStripMenuItem("＋ Событие");
        evRoot.ForeColor = Color.FromArgb(150, 220, 150);
        for (int i = 0; i < Events.Length; i++)
        {
            int idx = i; var it = new ToolStripMenuItem(Events[i].label);
            it.ForeColor = Color.Gainsboro;
            it.Click += (s, e) => AddNodeAt(true, idx, loc);
            evRoot.DropDownItems.Add(it);
        }
        var acRoot = new ToolStripMenuItem("＋ Действие");
        acRoot.ForeColor = Color.FromArgb(150, 190, 235);
        for (int i = 0; i < Actions.Length; i++)
        {
            int idx = i; var it = new ToolStripMenuItem(Actions[i].label);
            it.ForeColor = Color.Gainsboro;
            it.Click += (s, e) => AddNodeAt(false, idx, loc);
            acRoot.DropDownItems.Add(it);
        }
        menu.Items.Add(evRoot);
        menu.Items.Add(acRoot);

        menu.Show(canvas, loc);
    }

    // ---- ports ----
    static Point OutPort(Node n) { return new Point(n.rect.Right, n.rect.Top + HEAD + (NH - HEAD) / 2); }
    static Point InPort(Node n) { return new Point(n.rect.Left, n.rect.Top + HEAD + (NH - HEAD) / 2); }
    static bool Near(Point a, Point b, int r) { return (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= r * r; }

    static void Down(object s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            ShowNodeMenu(e.Location);
            return;
        }
        // start a wire from an event output port?
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].isEvent && Near(e.Location, OutPort(nodes[i]), PORT + 4)) { wireFrom = i; wireMouse = e.Location; return; }
        // else grab a node to drag (topmost first)
        for (int i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i].rect.Contains(e.Location)) { dragNode = i; dragOff = new Point(e.X - nodes[i].rect.X, e.Y - nodes[i].rect.Y); return; }
    }

    static void Move(object s, MouseEventArgs e)
    {
        if (dragNode >= 0) { var n = nodes[dragNode]; n.rect.X = e.X - dragOff.X; n.rect.Y = e.Y - dragOff.Y; canvas.Invalidate(); }
        else if (wireFrom >= 0) { wireMouse = e.Location; canvas.Invalidate(); }
    }

    static void Up(object s, MouseEventArgs e)
    {
        if (wireFrom >= 0)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (!nodes[i].isEvent && Near(e.Location, InPort(nodes[i]), PORT + 5))
                {
                    if (!HasWire(wireFrom, i)) wires.Add(new[] { wireFrom, i });
                    break;
                }
            wireFrom = -1; canvas.Invalidate();
        }
        dragNode = -1;
    }

    static bool HasWire(int a, int b) { foreach (var w in wires) if (w[0] == a && w[1] == b) return true; return false; }

    // Double-click an ACTION node to edit its number.
    static void DblClick(object s, MouseEventArgs e)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
            if (!nodes[i].isEvent && nodes[i].rect.Contains(e.Location))
            {
                string r = Prompt("Значение действия", nodes[i].arg.ToString(CI));
                if (r != null) { nodes[i].arg = Parse(r); canvas.Invalidate(); }
                return;
            }
    }

    static string Prompt(string title, string init)
    {
        using (var f = new Form { Text = title, ClientSize = new Size(250, 96), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Color.FromArgb(30, 33, 40) })
        {
            var tb = new TextBox { Left = 16, Top = 18, Width = 218, Text = init, BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.White, Font = F(11) };
            var ok = new Button { Text = "OK", Left = 72, Top = 54, Width = 106, Height = 28, DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(70, 96, 60), Font = F(9, FontStyle.Bold) };
            f.Controls.Add(tb); f.Controls.Add(ok); f.AcceptButton = ok;
            tb.Select(); tb.SelectAll();
            return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }
    }

    static void DeleteNode(int idx)
    {
        nodes.RemoveAt(idx);
        var kept = new List<int[]>();
        foreach (var w in wires)
        {
            if (w[0] == idx || w[1] == idx) continue;
            kept.Add(new[] { w[0] > idx ? w[0] - 1 : w[0], w[1] > idx ? w[1] - 1 : w[1] });
        }
        wires.Clear(); wires.AddRange(kept);
    }

    // ---- paint ----
    static void Paint(object s, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var wirePen = new Pen(Color.FromArgb(210, 180, 90), 2.5f))
            foreach (var w in wires)
                if (w[0] < nodes.Count && w[1] < nodes.Count) DrawWire(g, wirePen, OutPort(nodes[w[0]]), InPort(nodes[w[1]]));
        if (wireFrom >= 0 && wireFrom < nodes.Count)
            using (var tmp = new Pen(Color.FromArgb(150, 210, 180, 90), 2f)) DrawWire(g, tmp, OutPort(nodes[wireFrom]), wireMouse);

        foreach (var n in nodes) DrawNode(g, n);
    }

    static void DrawWire(Graphics g, Pen p, Point a, Point b)
    {
        int dx = Math.Max(40, Math.Abs(b.X - a.X) / 2);
        g.DrawBezier(p, a, new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b);
    }

    static void DrawNode(Graphics g, Node n)
    {
        Color head = n.isEvent ? Color.FromArgb(56, 110, 66) : Color.FromArgb(52, 84, 130);
        using (var body = new SolidBrush(Color.FromArgb(38, 41, 50)))
        using (var hb = new SolidBrush(head))
        using (var border = new Pen(Color.FromArgb(70, 76, 90)))
        {
            g.FillRectangle(body, n.rect);
            g.FillRectangle(hb, new Rectangle(n.rect.X, n.rect.Y, n.rect.Width, HEAD));
            g.DrawRectangle(border, n.rect);
            TextRenderer.DrawText(g, n.isEvent ? "СОБЫТИЕ" : "ДЕЙСТВИЕ", F(8, FontStyle.Bold),
                new Rectangle(n.rect.X + 6, n.rect.Y + 3, n.rect.Width - 12, 18), Color.White, TextFormatFlags.Left);
            string txt = n.isEvent ? n.label : (n.label + n.arg.ToString(CI));
            TextRenderer.DrawText(g, txt, F(9),
                new Rectangle(n.rect.X + 8, n.rect.Y + HEAD + 4, n.rect.Width - 16, NH - HEAD - 8), Color.Gainsboro, TextFormatFlags.Left | TextFormatFlags.WordEllipsis);
        }
        using (var port = new SolidBrush(Color.FromArgb(230, 200, 110)))
        {
            if (n.isEvent) { var p = OutPort(n); g.FillEllipse(port, p.X - PORT, p.Y - PORT, PORT * 2, PORT * 2); }
            else { var p = InPort(n); g.FillEllipse(port, p.X - PORT, p.Y - PORT, PORT * 2, PORT * 2); }
        }
    }

    // ---- save / open ----
    static string ModsDir()
    {
        try { string d = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mods"); Directory.CreateDirectory(d); return d; }
        catch { return Environment.GetFolderPath(Environment.SpecialFolder.Desktop); }
    }

    static void Save(string path)
    {
        if (nodes.Count == 0) { Err("Пусто — добавь ноды."); return; }
        if (string.IsNullOrEmpty(path))
        {
            using (var dlg = new SaveFileDialog { Filter = "ZombieShooter mod (*.zmod)|*.zmod", InitialDirectory = ModsDir(), FileName = "mymod.zmod" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }
        }
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("ZMOD1");
            // layout (comments — the game ignores these; we use them to reload the graph)
            foreach (var n in nodes)
                sb.AppendLine("# node " + (n.isEvent ? "E" : "A") + " " + n.key + " " + n.arg.ToString(CI) + " " + n.rect.X + " " + n.rect.Y);
            foreach (var w in wires) sb.AppendLine("# wire " + w[0] + " " + w[1]);
            // compiled rules: one line per EVENT node with its wired actions
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].isEvent) continue;
                var line = new StringBuilder(nodes[i].key);
                foreach (var w in wires)
                    if (w[0] == i && w[1] < nodes.Count && !nodes[w[1]].isEvent)
                        line.Append(' ').Append(nodes[w[1]].key).Append(':').Append(nodes[w[1]].arg.ToString(CI));
                if (line.ToString().Contains(" ")) sb.AppendLine(line.ToString()); // only events with ≥1 action
            }
            File.WriteAllText(path, sb.ToString());
            currentFile = path;
            Say("Сохранено: " + Path.GetFileName(path) + "  (папка mods — рядом с игрой)");
        }
        catch (Exception ex) { Err("Не удалось сохранить: " + ex.Message); }
    }

    static void Open()
    {
        using (var dlg = new OpenFileDialog { Filter = "ZombieShooter mod (*.zmod)|*.zmod|Все файлы (*.*)|*.*", InitialDirectory = ModsDir() })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                nodes.Clear(); wires.Clear();
                foreach (var raw in File.ReadAllLines(dlg.FileName))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("# node "))
                    {
                        var t = line.Substring(7).Split(' ');
                        if (t.Length >= 5)
                            nodes.Add(new Node { isEvent = t[0] == "E", key = t[1], label = LabelFor(t[0] == "E", t[1]),
                                arg = Parse(t[2]), rect = new Rectangle(int.Parse(t[3]), int.Parse(t[4]), NW, NH) });
                    }
                    else if (line.StartsWith("# wire "))
                    {
                        var t = line.Substring(7).Split(' ');
                        if (t.Length >= 2) wires.Add(new[] { int.Parse(t[0]), int.Parse(t[1]) });
                    }
                }
                currentFile = dlg.FileName;
                canvas.Invalidate();
                Say("Открыто: " + Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex) { Err("Не удалось открыть: " + ex.Message); }
        }
    }

    static string LabelFor(bool isEvent, string key)
    {
        foreach (var d in isEvent ? Events : Actions) if (d.key == key) return d.label;
        return key;
    }
}
