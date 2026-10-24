using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ShishaniRP;

internal sealed class LauncherView : Form
{
    private static readonly Color Base = Color.FromArgb(247, 248, 251);
    private static readonly Color White = Color.White;
    private static readonly Color Text = Color.FromArgb(28, 34, 45);
    private static readonly Color Soft = Color.FromArgb(116, 126, 143);
    private static readonly Color Hairline = Color.FromArgb(229, 233, 240);
    private static readonly Color Green = Color.FromArgb(33, 174, 102);

    private readonly PlayerState state;
    private ReleaseInfo? latest;
    private bool installing;
    private string currentGamePath;
    private readonly Label onlineValue;
    private readonly Label versionText;
    private readonly Label statusText;
    private readonly TextBox nickname;
    private readonly FlatButton primary;
    private readonly FlatButton refresh;
    private readonly ProgressBar bar;
    private readonly System.Windows.Forms.Timer poll;
    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SHISHANI_RP", "launcher-state.json");

    internal LauncherView()
    {
        state = ReadState();
        currentGamePath = state.GamePath;
        Text = "ШиШаНи";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 648);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Base;
        DoubleBuffered = true;
        MaximizeBox = false;
        MinimumSize = Size;
        MaximumSize = Size;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

        var top = new Panel { Location = new Point(0, 0), Size = new Size(1100, 78), BackColor = White };
        Controls.Add(top);
        top.MouseDown += BeginMove;
        top.Paint += (_, e) =>
        {
            using var pen = new Pen(Hairline);
            e.Graphics.DrawLine(pen, 0, 77, 1100, 77);
        };

        var mark = new RoundedPanel { Location = new Point(31, 18), Size = new Size(44, 44),
            Fill = Text, Curve = 14 };
        top.Controls.Add(mark);
        AddLabel(mark, "Ш", 1, 0, 42, 43, 24, true, White, ContentAlignment.MiddleCenter);
        var name = AddLabel(top, "ШиШаНи", 87, 19, 208, 37, 20, true);
        name.MouseDown += BeginMove;
        var tag = AddLabel(top, "ROLEPLAY", 275, 26, 130, 28, 9, true, Soft);
        tag.MouseDown += BeginMove;
        var gear = new FlatButton
        {
            Location = new Point(922, 17), Size = new Size(50, 46),
            Fill = White, Line = Hairline, Curve = 14,
            Text = "\uE713", Font = new Font("Segoe MDL2 Assets", 16),
            ForeColor = Text
        };
        gear.Click += (_, _) => OpenSettings();
        gear.AccessibleName = "Настройки пути игры";
        top.Controls.Add(gear);
        var minimize = new FlatButton
        {
            Location = new Point(980, 17), Size = new Size(45, 46),
            Fill = White, Curve = 14, Text = "−",
            Font = new Font("Segoe UI", 17), ForeColor = Soft
        };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        top.Controls.Add(minimize);
        var close = new FlatButton
        {
            Location = new Point(1031, 17), Size = new Size(45, 46),
            Fill = White, Curve = 14, Text = "×",
            Font = new Font("Segoe UI", 22), ForeColor = Text
        };
        close.Click += (_, _) => Close();
        top.Controls.Add(close);

        var hero = new CityHero { Location = new Point(27, 98), Size = new Size(1046, 290) };
        Controls.Add(hero);
        AddLabel(hero, "SHISHANI ROLEPLAY", 38, 32, 426, 27, 10, true, Soft);
        AddLabel(hero, "Твой город.", 35, 73, 610, 71, 42, true, Text);
        AddLabel(hero, "Твоя история.", 35, 132, 630, 72, 42, true, Text);
        AddLabel(hero, "Играй. Общайся. Создавай свою историю.", 39, 221, 570, 30, 12, false, Soft);

        var nickCard = new RoundedPanel
        {
            Location = new Point(27, 407), Size = new Size(655, 118),
            Fill = White, Line = Hairline, Curve = 21
        };
        Controls.Add(nickCard);
        AddLabel(nickCard, "ИГРОВОЙ НИК", 23, 12, 265, 29, 9, true, Soft);
        var inputPanel = new RoundedPanel
        {
            Location = new Point(23, 47), Size = new Size(608, 48),
            Fill = Color.FromArgb(249, 250, 252), Line = Hairline, Curve = 13
        };
        nickCard.Controls.Add(inputPanel);
        nickname = new TextBox
        {
            Text = state.Nick, BorderStyle = BorderStyle.None,
            Location = new Point(17, 14), Size = new Size(575, 27),
            Font = new Font("Segoe UI", 12), ForeColor = Text,
            BackColor = Color.FromArgb(249, 250, 252), MaxLength = 24
        };
        inputPanel.Controls.Add(nickname);
        nickname.PlaceholderText = "Введите ник персонажа";
        nickname.TextChanged += (_, _) => SaveState();
        nickname.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = PrimaryAsync(); }
        };

        var serverCard = new RoundedPanel
        {
            Location = new Point(698, 407), Size = new Size(375, 118),
            Fill = White, Line = Hairline, Curve = 21
        };
        Controls.Add(serverCard);
        AddLabel(serverCard, "●", 22, 14, 19, 24, 11, true, Green);
        AddLabel(serverCard, "СЕРВЕР 1", 43, 14, 284, 24, 9, true, Soft);
        AddLabel(serverCard, "Игроков онлайн", 22, 62, 210, 34, 13, true, Text);
        onlineValue = AddLabel(serverCard, "—", 239, 56, 114, 44, 19, true, Text,
            ContentAlignment.MiddleRight);

        var footer = new RoundedPanel
        {
            Location = new Point(27, 543), Size = new Size(1046, 84),
            Fill = White, Line = Hairline, Curve = 20
        };
        Controls.Add(footer);
        statusText = AddLabel(footer, "Проверяем сборку...", 24, 18, 597, 30, 12, true);
        versionText = AddLabel(footer, "Подключаемся к серверу обновлений", 24, 47, 587, 22, 9, false, Soft);
        bar = new ProgressBar
        {
            Visible = false, Location = new Point(25, 71),
            Size = new Size(581, 5), Minimum = 0, Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        footer.Controls.Add(bar);

        refresh = new FlatButton
        {
            Location = new Point(633, 15), Size = new Size(118, 54),
            Text = "↻", Font = new Font("Segoe UI", 20),
            Fill = Color.FromArgb(242, 244, 247), ForeColor = Text,
            Curve = 15
        };
        refresh.AccessibleName = "Проверить обновления";
        refresh.Click += async (_, _) => await RefreshReleaseAsync();
        footer.Controls.Add(refresh);

        primary = new FlatButton
        {
            Location = new Point(764, 15), Size = new Size(256, 54),
            Text = "УСТАНОВИТЬ", Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold),
            Fill = Text, ForeColor = White, Curve = 15
        };
        primary.Click += async (_, _) => await PrimaryAsync();
        footer.Controls.Add(primary);

        UpdateView();
        Shown += async (_, _) =>
        {
            await Task.WhenAll(RefreshOnlineAsync(), RefreshReleaseAsync());
            poll.Start();
        };
        poll = new System.Windows.Forms.Timer { Interval = 90000 };
        poll.Tick += async (_, _) =>
        {
            await RefreshOnlineAsync();
            if (!installing) await RefreshReleaseAsync();
        };
        FormClosing += (_, e) =>
        {
            if (installing)
            {
                e.Cancel = true;
                statusText.Text = "Установка продолжается — не закрывай лаунчер";
            }
            else SaveState();
        };
    }

    private static Label AddLabel(Control parent, string title, int x, int y, int w, int h,
        float fontSize, bool bold = false, Color? fore = null,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var l = new Label
        {
            Text = title, BackColor = Color.Transparent,
            Location = new Point(x, y), Size = new Size(w, h),
            Font = new Font("Segoe UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = fore ?? Text, TextAlign = align, AutoEllipsis = true
        };
        parent.Controls.Add(l);
        return l;
    }

    private static PlayerState ReadState()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(StateFile)) ?? new();
        }
        catch { }
        return new();
    }

    private void SaveState()
    {
        state.Nick = nickname?.Text.Trim() ?? state.Nick;
        state.GamePath = currentGamePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
        }
        catch { }
    }

    private bool HasGame()
    {
        return !string.IsNullOrWhiteSpace(currentGamePath)
            && File.Exists(Path.Combine(currentGamePath, "gta_sa.exe"))
            && File.Exists(Path.Combine(currentGamePath, "samp.exe"));
    }

    private bool InstalledLatest()
    {
        return latest != null && HasGame() &&
            state.InstalledVersion == latest.Version &&
            !string.IsNullOrWhiteSpace(state.InstalledPath) &&
            Path.GetFullPath(state.InstalledPath)
                .Equals(Path.GetFullPath(currentGamePath), StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateView()
    {
        if (installing) return;
        bool installed = HasGame();
        bool updateAvailable = installed && latest != null && !InstalledLatest();
        primary.Text = !installed ? "УСТАНОВИТЬ  →"
                     : updateAvailable ? "ОБНОВИТЬ  →" : "ИГРАТЬ  →";
        statusText.Text = !installed ? "Игра не установлена"
                        : updateAvailable ? "Доступно обновление"
                        : "Готово к игре";
        versionText.Text = !installed ? "Нажми «Установить», чтобы начать"
                         : updateAvailable ? "Новая версия: " + latest!.Version
                         : "Сервер 1 · ШиШаНи";
    }

    private async Task RefreshReleaseAsync()
    {
        if (installing) return;
        try
        {
            refresh.Enabled = false;
            latest = await ReleaseService.FindAsync();
            UpdateView();
        }
        catch (Exception ex)
        {
            if (!HasGame())
            {
                statusText.Text = "Не удалось получить обновления";
                versionText.Text = "Проверь подключение к интернету";
                versionText.Tag = ex.Message;
            }
            else
            {
                latest = null;
                UpdateView();
            }
        }
        finally { refresh.Enabled = true; }
    }

    private async Task RefreshOnlineAsync()
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect("185.207.214.14", 2909);
            byte[] p = { 83, 65, 77, 80, 185, 207, 214, 14, (byte)(2909 & 0xff), (byte)(2909 >> 8), (byte)'i' };
            await udp.SendAsync(p, p.Length);
            using var timeout = new CancellationTokenSource(1500);
            var reply = await udp.ReceiveAsync(timeout.Token);
            if (reply.Buffer.Length < 16 || !reply.Buffer.Take(11).SequenceEqual(p)) throw new Exception("Invalid SA-MP response");
            int count = BitConverter.ToUInt16(reply.Buffer, 12);
            int cap = BitConverter.ToUInt16(reply.Buffer, 14);
            onlineValue.Text = $"{count} / {cap}";
        }
        catch { onlineValue.Text = "—"; }
    }

    private async Task PrimaryAsync()
    {
        if (installing) return;
        bool installed = HasGame();
        if (installed)
        {
            // Check once more before Play, so players receive new releases.
            try { latest = await ReleaseService.FindAsync(); } catch { /* offline: allow local launch */ }
        }
        if (!installed || (latest != null && !InstalledLatest()))
        {
            await InstallGameAsync();
        }
        else LaunchGame();
    }

    private async Task InstallGameAsync()
    {
        if (installing) return;
        installing = true;
        primary.Enabled = false;
        refresh.Enabled = false;
        bar.Visible = true;
        bar.Value = 0;
        primary.Text = "УСТАНОВКА...";
        statusText.Text = "Подготовка загрузки...";
        versionText.Text = "Проверяем файлы";
        try
        {
            latest ??= await ReleaseService.FindAsync();
            string path = Path.GetFullPath(currentGamePath);
            if (path == Path.GetPathRoot(path))
                throw new Exception("Нельзя устанавливать игру в корень диска.");
            if (Process.GetProcessesByName("gta_sa").Length > 0)
                throw new Exception("Сначала закрой GTA San Andreas.");
            Directory.CreateDirectory(path);
            var update = new Progress<InstallStatus>(info =>
            {
                bar.Value = Math.Clamp(info.Percent, 0, 100);
                statusText.Text = info.Message;
                versionText.Text = "ШиШаНи · Установка клиента";
            });
            await ReleaseService.InstallAsync(latest, path, update);
            state.InstalledVersion = latest.Version;
            state.InstalledPath = path;
            SaveState();
            statusText.Text = "Установка завершена";
        }
        catch (Exception ex)
        {
            statusText.Text = "Не удалось установить игру";
            versionText.Text = ex.Message.Length > 90 ? ex.Message[..87] + "..." : ex.Message;
            versionText.Tag = ex.Message;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(StateFile)!, "launcher.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n");
            }
            catch { }
            return;
        }
        finally
        {
            installing = false;
            primary.Enabled = true;
            refresh.Enabled = true;
            bar.Visible = false;
            UpdateView();
        }
    }

    private void LaunchGame()
    {
        if (!Regex.IsMatch(nickname.Text.Trim(), @"^[a-zA-Z0-9_]{3,24}$"))
        {
            nickname.Focus();
            statusText.Text = "Укажи игровой ник";
            versionText.Text = "От 3 до 24 символов: латиница, цифры или _";
            return;
        }
        string target = Path.Combine(currentGamePath, "samp.exe");
        try
        {
            Process.Start(new ProcessStartInfo(target, "-h 185.207.214.14 -p 2909 -n " + nickname.Text.Trim())
            {
                WorkingDirectory = currentGamePath, UseShellExecute = true
            });
            SaveState();
            statusText.Text = "Запускаем игру...";
            versionText.Text = "Сервер 1";
        }
        catch (Exception e)
        {
            statusText.Text = "Не удалось запустить игру";
            versionText.Text = e.Message;
        }
    }

    private void OpenSettings()
    {
        if (installing) return;
        using var dialog = new Form
        {
            Text = "Путь к игре", ClientSize = new Size(590, 223),
            BackColor = White, Font = new Font("Segoe UI", 10),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false
        };
        AddLabel(dialog, "Папка игры", 24, 18, 530, 43, 19, true);
        AddLabel(dialog, "Куда устанавливать файлы ШиШаНи", 25, 58, 535, 26, 10, false, Soft);
        var field = new TextBox
        {
            Location = new Point(25, 104), Size = new Size(399, 31),
            Font = new Font("Segoe UI", 10), Text = currentGamePath
        };
        dialog.Controls.Add(field);
        var browse = new Button
        {
            Text = "Обзор", Location = new Point(440, 100), Size = new Size(126, 39),
            FlatStyle = FlatStyle.System
        };
        dialog.Controls.Add(browse);
        browse.Click += (_, _) =>
        {
            using var folder = new FolderBrowserDialog { Description = "Папка установки ШиШаНи" };
            if (Directory.Exists(field.Text)) folder.SelectedPath = field.Text;
            if (folder.ShowDialog(dialog) == DialogResult.OK) field.Text = folder.SelectedPath;
        };
        var apply = new FlatButton
        {
            Text = "СОХРАНИТЬ", Location = new Point(410, 162),
            Size = new Size(156, 43), Fill = Text, ForeColor = White, Curve = 12,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        dialog.Controls.Add(apply);
        apply.Click += (_, _) =>
        {
            try
            {
                var value = Path.GetFullPath(field.Text.Trim());
                if (value == Path.GetPathRoot(value)) throw new Exception();
                currentGamePath = value;
                SaveState();
                latest = null;
                UpdateView();
                dialog.DialogResult = DialogResult.OK;
            }
            catch { MessageBox.Show(dialog, "Выбери правильную папку, не корень диска.", "ШиШаНи"); }
        };
        dialog.AcceptButton = apply;
        dialog.ShowDialog(this);
        _ = RefreshReleaseAsync();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wp, int lp);
    private void BeginMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, 0);
    }
}

internal class RoundedPanel : Panel
{
    internal Color Fill { get; set; } = Color.White;
    internal Color? Line { get; set; }
    internal int Curve { get; set; } = 18;

    internal RoundedPanel()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
    }

    internal static GraphicsPath Geometry(Rectangle rect, int curve)
    {
        int d = Math.Max(1, Math.Min(Math.Min(rect.Width, rect.Height), curve * 2));
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Geometry(new Rectangle(1, 1, Width - 3, Height - 3), Curve);
        using var brush = new SolidBrush(Fill);
        e.Graphics.FillPath(brush, path);
        if (Line.HasValue)
        {
            using var pen = new Pen(Line.Value, 1);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class FlatButton : Button
{
    internal Color Fill { get; set; } = Color.FromArgb(28, 34, 45);
    internal Color? Line { get; set; }
    internal int Curve { get; set; } = 14;
    private bool hovered;
    internal FlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e); hovered = true; Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); hovered = false; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPanel.Geometry(new Rectangle(1, 1, Width - 3, Height - 3), Curve);
        var fill = hovered && Enabled ? ControlPaint.Light(Fill, 0.09f) : Fill;
        if (!Enabled) fill = Color.FromArgb(220, 224, 229);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (Line.HasValue)
        {
            using var line = new Pen(Line.Value);
            e.Graphics.DrawPath(line, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(3, 2, Width - 6, Height - 4),
            Enabled ? ForeColor : Color.Gray, TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class CityHero : Panel
{
    internal CityHero()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(238, 242, 248);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(1, 1, Width - 3, Height - 3);
        using var edge = RoundedPanel.Geometry(rect, 23);
        using var grad = new LinearGradientBrush(rect,
            Color.FromArgb(249, 250, 253), Color.FromArgb(230, 237, 246),
            LinearGradientMode.Horizontal);
        g.FillPath(grad, edge);
        g.SetClip(edge);
        using var grid = new Pen(Color.FromArgb(26, 82, 107, 147), 1);
        for (int x = 675; x < Width; x += 28)
            g.DrawLine(grid, x, 0, x, Height);
        for (int y = 0; y < Height; y += 28)
            g.DrawLine(grid, 660, y, Width, y);

        using var glow = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
        g.FillEllipse(glow, 682, -93, 406, 406);
        using var outline = new Pen(Color.FromArgb(103, 178, 192, 210), 1.5f);
        g.DrawEllipse(outline, 688, -86, 420, 420);
        g.DrawEllipse(outline, 744, -32, 311, 311);
        using var shadow = new LinearGradientBrush(new Point(791, 22), new Point(1024, 278),
            Color.FromArgb(66, 76, 97), Color.FromArgb(20, 27, 42));
        g.FillEllipse(shadow, 809, 18, 259, 259);
        using var sparkle = new SolidBrush(Color.FromArgb(39, 255, 255, 255));
        g.FillEllipse(sparkle, 831, 37, 207, 104);
        using var font = new Font("Segoe UI", 115, FontStyle.Bold);
        TextRenderer.DrawText(g, "Ш", font, new Rectangle(838, 43, 212, 190),
            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        g.ResetClip();
        using var border = new Pen(Color.FromArgb(224, 232, 241), 1);
        g.DrawPath(border, edge);
    }
}
