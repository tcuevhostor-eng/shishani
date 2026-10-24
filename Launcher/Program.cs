using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms;

namespace ShishaniRP;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (Bootstrap.InstallIfNeeded()) return;
            Application.Run(new LauncherWindow());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SHISHANI RP — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class Bootstrap
{
    internal static bool InstallIfNeeded()
    {
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown");
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SHISHANI_RP");
        string installed = Path.Combine(home, "SHISHANI_RP.exe");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(home);
        if (!File.Exists(installed) || !FilesEqual(source, installed))
            File.Copy(source, installed, true);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        dynamic? shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
        if (shell == null) throw new Exception("Cannot create Windows shortcut");
        dynamic shortcut = shell.CreateShortcut(Path.Combine(desktop, "ШиШаНи.lnk"));
        shortcut.TargetPath = installed;
        shortcut.WorkingDirectory = home;
        shortcut.IconLocation = installed + ",0";
        shortcut.Description = "SHISHANI RP Launcher";
        shortcut.Save();
        Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
        return true;
    }

    private static bool FilesEqual(string a, string b)
    {
        var x = new FileInfo(a); var y = new FileInfo(b);
        return x.Length == y.Length && x.LastWriteTimeUtc <= y.LastWriteTimeUtc;
    }
}

internal sealed class PlayerState
{
    public string Nick { get; set; } = "";
    public string GamePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SHISHANI_RP", "Client");
    public string InstalledVersion { get; set; } = "";
    public string InstalledPath { get; set; } = "";
}

internal sealed class LauncherWindow : Form
{
    private static readonly Color Paper = Color.FromArgb(246, 247, 249);
    private static readonly Color Ink = Color.FromArgb(29, 34, 43);
    private static readonly Color Muted = Color.FromArgb(111, 120, 132);
    private static readonly Color Border = Color.FromArgb(226, 230, 235);
    private Label online = new();
    private Label status = new();
    private Label version = new();
    private ProgressBar progress = new();
    private TextBox nick = new();
    private TextBox gamePath = new();
    private Button play = new();
    private Button check = new();
    private readonly System.Windows.Forms.Timer onlineTimer = new() { Interval = 30000 };
    private PlayerState state;
    private ReleaseInfo? latest;
    private bool busy;

    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SHISHANI_RP", "launcher-state.json");

    internal LauncherWindow()
    {
        state = LoadState();
        Text = "ШиШаНи — Лаунчер";
        ClientSize = new Size(1100, 686);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Paper;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.None;
        BuildInterface();
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; MessageBox.Show("Дождись окончания установки файлов.", "ШиШаНи"); return; }
            SaveState();
        };
        Shown += async (_, _) =>
        {
            await RefreshOnlineAsync();
            onlineTimer.Start();
            await RefreshVersionAsync();
        };
        onlineTimer.Tick += async (_, _) => await RefreshOnlineAsync();
    }

    private static PlayerState LoadState()
    {
        try { if (File.Exists(StateFile)) return JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(StateFile)) ?? new(); }
        catch { }
        return new();
    }

    private void SaveState()
    {
        state.Nick = nick.Text.Trim();
        state.GamePath = gamePath.Text.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
        }
        catch { }
    }

    private static Label TextLabel(Control parent, string text, int x, int y, int w, int h,
        float size = 10, bool bold = false, Color? color = null)
    {
        var label = new Label
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            BackColor = Color.Transparent, ForeColor = color ?? Ink,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };
        parent.Controls.Add(label);
        return label;
    }

    private static Panel WhitePanel(Control parent, int x, int y, int w, int h)
    {
        var panel = new Panel { BackColor = Color.White, Location = new Point(x, y), Size = new Size(w, h) };
        parent.Controls.Add(panel);
        return panel;
    }

    private static Button MakeButton(Control parent, string text, int x, int y, int w, int h, Color bg, Color fg)
    {
        var btn = new Button
        {
            Text = text, BackColor = bg, ForeColor = fg, FlatStyle = FlatStyle.Flat,
            Location = new Point(x, y), Size = new Size(w, h),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
        btn.FlatAppearance.BorderSize = 0;
        parent.Controls.Add(btn);
        return btn;
    }


    private readonly Color snow = Color.FromArgb(246, 247, 250);
    private readonly Color ink2 = Color.FromArgb(22, 27, 37);
    private readonly Color steel = Color.FromArgb(116, 126, 141);
    private readonly Color hairline = Color.FromArgb(230, 233, 239);
    private Panel? headerArea;
    private Panel? installationPanel;
    private Label? updateHint;
    private Label? onlineCaption;

    private static ModernCard Card(Control parent, int x, int y, int w, int h, Color fill, int curve=20, Color? border=null)
    {
        var card = new ModernCard { Location = new Point(x,y), Size=new Size(w,h), Fill=fill, Corner=curve, Outline=border };
        parent.Controls.Add(card);
        return card;
    }

    private static Label LabelAt(Control parent, string value, int x, int y, int w, int h,
        float size=10, bool heavy=false, Color? color=null, ContentAlignment align=ContentAlignment.MiddleLeft)
    {
        var lbl = new Label {
            Text=value, Location=new Point(x,y), Size=new Size(w,h),
            BackColor=Color.Transparent,
            ForeColor=color ?? Color.FromArgb(22, 27, 37),
            Font=new Font("Segoe UI", size, heavy ? FontStyle.Bold : FontStyle.Regular),
            TextAlign=align, AutoEllipsis=true
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static ModernButton ActionButton(Control parent, string value, int x, int y, int w, int h,
        Color fill, Color fore, int radius=15, Color? line=null)
    {
        var b = new ModernButton {
            Text=value, Location=new Point(x,y), Size=new Size(w,h),
            Fill=fill, ForeColor=fore, Border=line, Radius=radius,
            Font=new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            Cursor=Cursors.Hand
        };
        parent.Controls.Add(b);
        return b;
    }

    private void BuildInterface()
    {
        SuspendLayout();
        ClientSize = new Size(1160, 740);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = snow;
        DoubleBuffered = true;
        Padding = new Padding(0);
        MinimumSize = Size;
        MaximumSize = Size;
        gamePath.Text = state.GamePath;

        headerArea = new Panel { Location=new Point(0,0), Size=new Size(1160,86), BackColor=Color.White };
        Controls.Add(headerArea);
        headerArea.MouseDown += DragWindow;
        headerArea.Paint += (_,e) => {
            using var edge = new Pen(hairline);
            e.Graphics.DrawLine(edge,0,85,1160,85);
        };

        var logoTile = Card(headerArea, 30, 21, 43, 43, ink2, 13);
        LabelAt(logoTile, "Ш", 4, -2, 36, 42, 23, true, Color.White, ContentAlignment.MiddleCenter);
        var productName = LabelAt(headerArea, "ШиШаНи", 86, 19, 233, 29, 18, true);
        productName.MouseDown += DragWindow;
        var subtitle = LabelAt(headerArea, "ROLEPLAY  /  SA-MP 0.3.7", 88, 48, 290, 20, 8, true, steel);
        subtitle.MouseDown += DragWindow;

        var topIndicator = Card(headerArea, 793, 27, 169, 32, Color.FromArgb(242,245,247), 16);
        LabelAt(topIndicator, "●", 9, 2, 18, 27, 11, true, Color.FromArgb(51,172,108));
        LabelAt(topIndicator, "СЕРВЕР 1", 31, 2, 124, 27, 9, true, ink2);
        var settings = ActionButton(headerArea, "⚙", 975, 20, 52, 46, Color.White, ink2, 14, hairline);
        settings.Font = new Font("Segoe UI Symbol", 19, FontStyle.Regular);
        settings.Click += (_,_) => OpenSettings();
        var minimize = ActionButton(headerArea, "—", 1034, 20, 44, 46, Color.White, steel, 14);
        minimize.Font = new Font("Segoe UI", 17);
        minimize.Click += (_,_) => WindowState=FormWindowState.Minimized;
        var close = ActionButton(headerArea, "×", 1086, 20, 44, 46, Color.White, ink2, 14);
        close.Font = new Font("Segoe UI", 22);
        close.Click += (_,_) => Close();

        // The illustrated hero is drawn with vectors so the EXE doesn't depend on external pictures.
        var hero = new HeroCard { Location=new Point(28,105), Size=new Size(1104,310) };
        Controls.Add(hero);
        LabelAt(hero, "SHISHANI  /  MULTIPLAYER PROJECT", 37, 36, 502, 30, 10.5f, true, steel);
        LabelAt(hero, "Твой город.", 34, 78, 665, 71, 38, true, ink2);
        LabelAt(hero, "Твоя история.", 34, 141, 666, 70, 38, true, ink2);
        LabelAt(hero, "Открой новый мир, найди друзей и начни свою историю.", 38, 231, 625, 28, 11, false, steel);
        var edition = Card(hero, 38, 269, 164, 25, Color.White, 12, hairline);
        LabelAt(edition, "●  SHISHANI 2026", 13, 1, 148, 23, 8.5f, true, ink2);

        // A three-column dashboard: live server, nickname, and files.
        var server = Card(this, 28, 435, 350, 143, Color.White, 21, hairline);
        LabelAt(server, "СЕРВЕР", 21, 15, 220, 23, 9, true, steel);
        LabelAt(server, "Сервер 1", 21, 45, 203, 39, 20, true, ink2);
        var liveDot = LabelAt(server, "●", 21, 96, 14, 20, 10, true, Color.FromArgb(45, 179, 110));
        onlineCaption = LabelAt(server, "Онлайн игроков", 40, 96, 170, 20, 10, false, steel);
        online = LabelAt(server, "•••", 221, 87, 108, 34, 15, true, ink2, ContentAlignment.MiddleRight);

        var account = Card(this, 394, 435, 350, 143, Color.White, 21, hairline);
        LabelAt(account, "ИГРОВОЙ НИК", 21, 15, 220, 23, 9, true, steel);
        var nameWrap = Card(account, 19, 50, 310, 49, Color.FromArgb(249,250,252), 13, hairline);
        nick = new TextBox {
            BorderStyle=BorderStyle.None, Location=new Point(13,14), Width=280,
            Font=new Font("Segoe UI Semibold", 12), BackColor=Color.FromArgb(249,250,252),
            ForeColor=ink2, Text=state.Nick, MaxLength=24
        };
        nameWrap.Controls.Add(nick);
        LabelAt(account, "Например: Khavaji_Tsuev", 21, 107, 283, 19, 9, false, steel);
        nick.TextChanged += (_,_) => SaveState();

        var build = Card(this, 760, 435, 372, 143, Color.White, 21, hairline);
        LabelAt(build, "КЛИЕНТ ИГРЫ", 22, 15, 223, 23, 9, true, steel);
        version = LabelAt(build, "Проверка...", 22, 47, 337, 34, 15.5f, true, ink2);
        updateHint = LabelAt(build, "Актуальная версия с GitHub", 22, 99, 325, 24, 10, false, steel);
        gamePath.Text = state.GamePath;

        // Primary installation area.
        installationPanel = Card(this, 28, 598, 1104, 118, Color.White, 22, hairline);
        LabelAt(installationPanel, "ГОТОВ К ИГРЕ?", 22, 14, 310, 25, 10, true, ink2);
        status = LabelAt(installationPanel, "Проверяем обновления...", 22, 38, 658, 28, 10, false, steel);
        progress = new ProgressBar {
            Location=new Point(22,83), Width=644, Height=11,
            Minimum=0,Maximum=100,Value=0,Style=ProgressBarStyle.Continuous
        };
        installationPanel.Controls.Add(progress);
        check = ActionButton(installationPanel,"↻  Проверить", 687, 27, 150, 59,
            Color.FromArgb(242,245,249), ink2, 15, hairline);
        check.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        check.Click += async (_,_) => await InstallAsync(false, true);
        play = ActionButton(installationPanel,"УСТАНОВИТЬ  →", 850, 27, 232, 59, ink2, Color.White, 15);
        play.Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold);
        play.Click += async (_,_) => await InstallAsync(true, false);

        Shown += (_,_) => {
            BringToFront();
            UpdateActionState();
        };
        ShowVersion();
        ResumeLayout(true);
    }

    private void OpenSettings()
    {
        if (busy) return;
        using var modal = new Form {
            Text="ШиШаНи — Путь к игре", Size=new Size(598, 269),
            StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false, MinimizeBox=false, BackColor=Color.White,
            Font=new Font("Segoe UI", 10), ShowIcon=false
        };
        LabelAt(modal, "Настройки установки", 23, 19, 530, 36, 19, true);
        LabelAt(modal, "Папка, куда лаунчер устанавливает файлы игры", 23, 58, 538, 29, 10, false, steel);
        var entry = new TextBox {
            Location=new Point(24,107), Width=414, Font=new Font("Segoe UI", 10),
            Text=gamePath.Text, BorderStyle=BorderStyle.FixedSingle
        };
        modal.Controls.Add(entry);
        var browse = ActionButton(modal, "Обзор...", 445, 101, 123, 42, Color.FromArgb(239,241,246), ink2, 12);
        browse.Click += (_,_) => {
            using var picker = new FolderBrowserDialog { Description="Выбери папку игры" };
            if (Directory.Exists(entry.Text)) picker.SelectedPath=entry.Text;
            if (picker.ShowDialog(modal)==DialogResult.OK) entry.Text=picker.SelectedPath;
        };
        var save = ActionButton(modal, "СОХРАНИТЬ", 407, 165, 160, 49, ink2, Color.White, 12);
        save.Click += (_,_) => {
            if (string.IsNullOrWhiteSpace(entry.Text)) {
                MessageBox.Show("Выбери папку для установки.", "ШиШаНи"); return;
            }
            string clean;
            try {
                clean = Path.GetFullPath(entry.Text);
                if (Path.GetPathRoot(clean) == clean) throw new Exception();
            }
            catch { MessageBox.Show("Укажи корректный путь к папке (не корень диска).","ШиШаНи"); return; }
            gamePath.Text=clean;
            SaveState();
            ShowVersion();
            modal.DialogResult=DialogResult.OK;
        };
        modal.AcceptButton=save;
        modal.ShowDialog(this);
    }

    private void UpdateActionState()
    {
        if (busy) return;
        bool hasClient = !string.IsNullOrWhiteSpace(state.InstalledVersion)
            && Directory.Exists(gamePath.Text)
            && File.Exists(Path.Combine(gamePath.Text,"samp.exe"))
            && File.Exists(Path.Combine(gamePath.Text,"gta_sa.exe"));
        if (latest != null && Current(latest)) {
            play.Text="ИГРАТЬ  →";
            return;
        }
        play.Text = hasClient ? "ОБНОВИТЬ  →" : "УСТАНОВИТЬ  →";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wp, int lp);
    private void DragWindow(object? _, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, 0);
    }

    private bool Current(ReleaseInfo rel) =>
        rel.Version == state.InstalledVersion
        && !string.IsNullOrWhiteSpace(state.InstalledPath)
        && !string.IsNullOrWhiteSpace(gamePath.Text)
        && Path.GetFullPath(gamePath.Text.Trim()).Equals(Path.GetFullPath(state.InstalledPath), StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(gamePath.Text.Trim(), "gta_sa.exe"))
        && File.Exists(Path.Combine(gamePath.Text.Trim(), "samp.exe"));

    private void ShowVersion()
    {
        version.Text = string.IsNullOrEmpty(state.InstalledVersion) ? "Сборка ещё не установлена"
            : "Установленная версия: " + state.InstalledVersion;
    }

    private async Task RefreshVersionAsync()
    {
        if (busy) return;
        try
        {
            status.Text = "Проверка обновлений на GitHub...";
            latest = await ReleaseService.FindAsync();
            status.Text = Current(latest) ? "Последняя версия установлена." : "Доступна версия " + latest.Version;
            UpdateActionState();
        }
        catch (Exception e)
        {
            status.Text = "Не удалось проверить обновления: " + e.Message;
        }
    }

    private async Task InstallAsync(bool runGame, bool force)
    {
        if (busy) return;
        string folder;
        try
        {
            folder = Path.GetFullPath(gamePath.Text.Trim());
            if (folder.Equals(Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Нельзя устанавливать файлы в корень диска.");
            if (runGame && !System.Text.RegularExpressions.Regex.IsMatch(nick.Text.Trim(), @"^[A-Za-z0-9_]{3,24}$"))
                throw new Exception("Введи ник латиницей, например Khavaji_Tsuev.");
        }
        catch (Exception e) { MessageBox.Show(e.Message, "ШиШаНи"); return; }

        busy = true;
        play.Enabled = false;
        check.Enabled = false;
        try
        {
            SaveState();
            status.Text = "Проверка доступной версии...";
            latest = await ReleaseService.FindAsync();
            if (force || !Current(latest))
            {
                if (Process.GetProcessesByName("gta_sa").Length != 0)
                    throw new Exception("Закрой GTA San Andreas перед обновлением.");
                Directory.CreateDirectory(folder);
                var reporter = new Progress<InstallStatus>(p =>
                {
                    progress.Value = Math.Clamp(p.Percent, 0, 100);
                    status.Text = p.Message;
                });
                await ReleaseService.InstallAsync(latest, folder, reporter);
                state.InstalledVersion = latest.Version;
                state.InstalledPath = folder;
                SaveState();
                ShowVersion();
            }
            progress.Value = 100;
            status.Text = "Готово. Установлена версия " + latest.Version;
            UpdateActionState();
            if (runGame)
            {
                string gta = Path.Combine(folder, "gta_sa.exe");
                string samp = Path.Combine(folder, "samp.exe");
                if (!File.Exists(gta) || !File.Exists(samp))
                    throw new Exception("В сборке не найдены gta_sa.exe и samp.exe. Проверь содержимое client.zip.");
                Process.Start(new ProcessStartInfo(samp,
                    "-h 185.207.214.14 -p 2909 -n " + nick.Text.Trim())
                { WorkingDirectory = folder, UseShellExecute = true });
                status.Text = "Запущен SA-MP — Сервер 1";
            }
        }
        catch (Exception ex)
        {
            status.Text = "Ошибка установки / подключения";
            MessageBox.Show(ex.Message, "SHISHANI RP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try
            {
                string log = Path.Combine(Path.GetDirectoryName(StateFile)!, "launcher.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n");
            }
            catch { }
        }
        finally
        {
            busy = false;
            play.Enabled = true;
            check.Enabled = true;
            UpdateActionState();
        }
    }

    private async Task RefreshOnlineAsync()
    {
        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 1200;
            client.Connect("185.207.214.14", 2909);
            var p = new List<byte> { 83, 65, 77, 80 };
            p.AddRange(new byte[] { 185, 207, 214, 14, (byte)(2909 & 255), (byte)(2909 >> 8), 105 });
            await client.SendAsync(p.ToArray(), p.Count);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            var rsp = await client.ReceiveAsync(cts.Token);
            byte[] dat = rsp.Buffer;
            if (dat.Length < 16 || !dat.Take(11).SequenceEqual(p)) throw new Exception();
            int players = BitConverter.ToUInt16(dat, 12);
            int max = BitConverter.ToUInt16(dat, 14);
            online.Text = $"{players} / {max}";
            online.ForeColor = Color.FromArgb(23, 142, 79);
        }
        catch { online.Text = "Нет ответа"; online.ForeColor = Muted; }
    }
}


internal class ModernCard : Panel
{
    internal Color Fill { get; set; } = Color.White;
    internal Color? Outline { get; set; }
    internal int Corner { get; set; } = 20;
    internal ModernCard() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor=Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var geometry=ShapePath(new Rectangle(0,0,Width-1,Height-1),Corner);
        using var brush=new SolidBrush(Fill);
        e.Graphics.FillPath(brush,geometry);
        if (Outline.HasValue) {
            using var pen = new Pen(Outline.Value,1);
            e.Graphics.DrawPath(pen,geometry);
        }
    }
    internal static GraphicsPath ShapePath(Rectangle r, int radius)
    {
        int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        var gp=new GraphicsPath();
        gp.AddArc(r.Left,r.Top,d,d,180,90);
        gp.AddArc(r.Right-d,r.Top,d,d,270,90);
        gp.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
        gp.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        gp.CloseFigure();
        return gp;
    }
}
internal sealed class ModernButton : Button
{
    internal Color Fill { get; set; } = Color.FromArgb(25,30,40);
    internal Color? Border { get; set; }
    internal int Radius { get; set; } = 14;
    private bool hover;
    internal ModernButton() {
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        TabStop=true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover=true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover=false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var rect=new Rectangle(1,1,Width-3,Height-3);
        using var path=ModernCard.ShapePath(rect,Radius);
        Color fill=hover ? ControlPaint.Light(Fill,0.08f) : Fill;
        if (!Enabled) fill=Color.FromArgb(225,226,229);
        using var brush=new SolidBrush(fill);
        e.Graphics.FillPath(brush,path);
        if (Border.HasValue) {
            using var pen=new Pen(Border.Value);
            e.Graphics.DrawPath(pen,path);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,rect,Enabled?ForeColor:Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
internal sealed class HeroCard : Panel
{
    internal HeroCard() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor=Color.FromArgb(236,241,247);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var rect=new Rectangle(0,0,Width-1,Height-1);
        using var outline=ModernCard.ShapePath(rect,25);
        using var bg=new LinearGradientBrush(rect,Color.FromArgb(250,251,253),
                                      Color.FromArgb(228,233,240),LinearGradientMode.Horizontal);
        g.FillPath(bg,outline);
        g.SetClip(outline);
        // Architectural grid and orbital arcs, rendered entirely in vector graphics.
        using var grid=new Pen(Color.FromArgb(35,154,168,189),1);
        for (int i=0;i<19;i++) {
            int x=690+i*24;
            g.DrawLine(grid,x,0,x,Height);
        }
        for (int i=0;i<13;i++) {
            int y=i*25;
            g.DrawLine(grid,700,y,Width,y);
        }
        using var halo=new SolidBrush(Color.FromArgb(110,255,255,255));
        g.FillEllipse(halo,710,-72,455,455);
        using var circlePen=new Pen(Color.FromArgb(150,191,202,218),2);
        g.DrawEllipse(circlePen,734,-64,418,418);
        g.DrawEllipse(circlePen,778,-22,335,335);
        using var inner=new LinearGradientBrush(new Point(785,5),new Point(1100,300),
                           Color.FromArgb(51,60,77),Color.FromArgb(18,23,34));
        g.FillEllipse(inner,829,30,249,249);
        using var reflection=new SolidBrush(Color.FromArgb(40,255,255,255));
        g.FillEllipse(reflection,850,45,203,100);
        using var glyph=new Font("Segoe UI",106,FontStyle.Bold);
        TextRenderer.DrawText(g,"Ш",glyph,new Rectangle(852,52,212,189),Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var smallPen=new Pen(Color.FromArgb(130,127,144,163),1);
        g.DrawArc(smallPen,681,37,487,487,210,95);
        g.ResetClip();
        using var border=new Pen(Color.FromArgb(223,229,236),1);
        g.DrawPath(border,outline);
    }
}
