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
    private readonly Label online = new();
    private readonly Label status = new();
    private readonly Label version = new();
    private readonly ProgressBar progress = new();
    private readonly TextBox nick = new();
    private readonly TextBox gamePath = new();
    private readonly Button play = new();
    private readonly Button check = new();
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

    private void BuildInterface()
    {
        var header = WhitePanel(this, 0, 0, 1100, 76);
        var logo = WhitePanel(header, 25, 20, 39, 38);
        logo.BackColor = Ink;
        TextLabel(logo, "Ш", 7, 0, 28, 36, 19, true, Color.White);
        TextLabel(header, "ШиШаНи", 77, 16, 260, 32, 19, true);
        TextLabel(header, "SA-MP ROLEPLAY PROJECT", 79, 46, 275, 19, 8, false, Muted);
        TextLabel(header, "ГЛАВНАЯ", 816, 22, 130, 32, 10, true);
        TextLabel(header, "SA-MP 0.3.7", 951, 22, 125, 32, 10, false, Muted);
        var separator = new Panel { Location = new Point(0, 75), Size = new Size(1100, 1), BackColor = Border };
        header.Controls.Add(separator);

        var hero = new Panel { Location = new Point(25, 94), Size = new Size(1050, 288) };
        hero.Paint += (_, e) =>
        {
            using var brush = new LinearGradientBrush(hero.ClientRectangle,
                Color.FromArgb(28, 34, 44), Color.FromArgb(70, 76, 91), LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, hero.ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var linePen = new Pen(Color.FromArgb(60, 210, 219, 230), 2);
            for (int i = 0; i < 12; i++)
            {
                int xx = 590 + i * 60;
                e.Graphics.DrawLine(linePen, xx, 288, xx + 150, 20);
            }
            using var halo = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
            e.Graphics.FillEllipse(halo, 740, -80, 390, 390);
        };
        Controls.Add(hero);
        TextLabel(hero, "SHISHANI  /  ROLEPLAY", 37, 29, 455, 28, 11, true, Color.FromArgb(220, 229, 238));
        TextLabel(hero, "ШиШаНи", 34, 74, 585, 83, 43, true, Color.White);
        TextLabel(hero, "Твоя история начинается здесь.", 39, 157, 490, 35, 14, false, Color.FromArgb(232, 238, 244));
        TextLabel(hero, "Сервер 1", 39, 228, 267, 35, 16, true, Color.White);
        TextLabel(hero, "ROLEPLAY", 163, 236, 200, 21, 9, false, Color.FromArgb(188, 200, 213));

        var server = WhitePanel(this, 25, 401, 287, 136);
        TextLabel(server, "СЕРВЕР", 17, 10, 230, 28, 9, true, Muted);
        TextLabel(server, "Сервер 1", 17, 42, 151, 32, 17, true);
        online = TextLabel(server, "Проверяем...", 158, 43, 115, 28, 10, true, Muted);
        online.TextAlign = ContentAlignment.MiddleRight;
        TextLabel(server, "Количество игроков онлайн", 17, 89, 255, 25, 9, false, Muted);

        var player = WhitePanel(this, 324, 401, 286, 136);
        TextLabel(player, "ТВОЙ ПЕРСОНАЖ", 17, 10, 250, 28, 9, true, Muted);
        nick.Location = new Point(18, 55);
        nick.Size = new Size(251, 31);
        nick.Font = new Font("Segoe UI", 12);
        nick.Text = state.Nick;
        nick.BorderStyle = BorderStyle.FixedSingle;
        player.Controls.Add(nick);
        TextLabel(player, "Например: Khavaji_Tsuev", 18, 91, 258, 28, 9, false, Muted);

        var folder = WhitePanel(this, 622, 401, 453, 136);
        TextLabel(folder, "ПАПКА УСТАНОВКИ ИГРЫ", 18, 10, 350, 29, 9, true, Muted);
        gamePath.Location = new Point(17, 54);
        gamePath.Size = new Size(282, 31);
        gamePath.Font = new Font("Segoe UI", 9.5f);
        gamePath.BorderStyle = BorderStyle.FixedSingle;
        gamePath.Text = state.GamePath;
        folder.Controls.Add(gamePath);
        var browse = MakeButton(folder, "ОБЗОР", 310, 50, 124, 40,
            Color.FromArgb(232, 235, 240), Ink);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Выбери папку для установки ШиШаНи" };
            if (Directory.Exists(gamePath.Text)) dlg.SelectedPath = gamePath.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) { gamePath.Text = dlg.SelectedPath; SaveState(); ShowVersion(); }
        };
        TextLabel(folder, "Можно выбрать пустую папку", 18, 91, 385, 28, 9, false, Muted);

        var bottom = WhitePanel(this, 25, 551, 1050, 115);
        TextLabel(bottom, "ИГРОВЫЕ ФАЙЛЫ", 18, 10, 290, 26, 10, true);
        version = TextLabel(bottom, "Проверяем наличие обновлений...", 18, 34, 549, 26, 9, false, Muted);
        progress.Location = new Point(18, 71);
        progress.Size = new Size(510, 10);
        progress.Minimum = 0;
        progress.Maximum = 100;
        bottom.Controls.Add(progress);
        status = TextLabel(bottom, "Подключаемся к GitHub...", 18, 85, 520, 26, 9, false, Muted);
        check = MakeButton(bottom, "ПРОВЕРИТЬ / ОБНОВИТЬ", 558, 28, 207, 60,
            Color.FromArgb(232, 235, 240), Ink);
        play = MakeButton(bottom, "ИГРАТЬ  →", 777, 28, 255, 60, Ink, Color.White);
        play.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        check.Click += async (_, _) => await InstallAsync(false, true);
        play.Click += async (_, _) => await InstallAsync(true, false);
        nick.TextChanged += (_, _) => SaveState();
        gamePath.Leave += (_, _) => { SaveState(); ShowVersion(); };
        ShowVersion();
    }

    private bool Current(ReleaseInfo rel) =>
        rel.Version == state.InstalledVersion
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
            play.Text = Current(latest) ? "ИГРАТЬ  →" : "УСТАНОВИТЬ / ИГРАТЬ";
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
            play.Text = "ИГРАТЬ  →";
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
        }
    }

    private async Task RefreshOnlineAsync()
    {
        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 1200;
            await client.ConnectAsync("185.207.214.14", 2909);
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
