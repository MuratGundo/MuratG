using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace UCREW.SecurePatch;

internal sealed class LauncherForm : Form
{
    private readonly ClientConfig _config;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _privateRoot;
    private readonly string _logPath;
    private readonly SecurePatchApiClient _apiClient;
    private readonly PatchRuntime _runtime;

    private readonly Label _gameLabel;
    private readonly Label _statusLabel;
    private readonly Label _detailLabel;
    private readonly Panel _progressTrack;
    private readonly Panel _progressFill;
    private readonly Button _closeButton;

    private bool _allowClose;
    private bool _started;

    public LauncherForm(ClientConfig config)
    {
        _config = config;
        _privateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "U-CREW",
            "SecurePatch",
            config.GameSlug);
        _logPath = Path.Combine(_privateRoot, "client.log");

        Directory.CreateDirectory(_privateRoot);
        FileSystemUtil.SetHiddenSystem(_privateRoot);

        _apiClient = new SecurePatchApiClient(config, _privateRoot, Log);
        _runtime = new PatchRuntime(config, _privateRoot, Log);

        SuspendLayout();

        Text = string.IsNullOrWhiteSpace(config.WindowTitle)
            ? "U-CREW Türkçe Yama"
            : config.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(960, 540);
        MinimumSize = ClientSize;
        MaximumSize = ClientSize;
        BackColor = Color.FromArgb(5, 7, 14);
        ShowInTaskbar = true;
        DoubleBuffered = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        string backgroundPath = ConfigLoader.ResolveAssetPath(config, config.BackgroundPath);
        BackgroundImage = LoadImageUnlocked(config, backgroundPath);
        BackgroundImageLayout = ImageLayout.Stretch;

        var shade = new GradientShadePanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        Controls.Add(shade);

        string logoPath = ConfigLoader.ResolveAssetPath(config, config.LogoPath);
        var logo = new PictureBox
        {
            Location = new Point(34, 26),
            Size = new Size(360, 74),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadImageUnlocked(config, logoPath)
        };
        shade.Controls.Add(logo);

        _closeButton = new Button
        {
            Text = "×",
            Location = new Point(905, 20),
            Size = new Size(36, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(120, 0, 0, 0),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18f, FontStyle.Regular),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 180, 30, 30);
        _closeButton.Click += (_, _) => RequestClose();
        shade.Controls.Add(_closeButton);

        var contentPanel = new Panel
        {
            Location = new Point(34, 346),
            Size = new Size(892, 164),
            BackColor = Color.FromArgb(210, 6, 9, 16)
        };
        shade.Controls.Add(contentPanel);

        _gameLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 13),
            Size = new Size(842, 23),
            Text = config.GameSlug.ToUpperInvariant() + " • TÜRKÇE YAMA",
            ForeColor = Color.FromArgb(169, 255, 45),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            UseCompatibleTextRendering = true
        };
        contentPanel.Controls.Add(_gameLabel);

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 36),
            Size = new Size(844, 50),
            Text = "Başlatıcı hazırlanıyor…",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 19.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 0, 2),
            UseCompatibleTextRendering = true
        };
        contentPanel.Controls.Add(_statusLabel);

        _detailLabel = new Label
        {
            AutoSize = false,
            Location = new Point(25, 86),
            Size = new Size(842, 27),
            Text = "U-CREW güvenli yama sistemi başlatılıyor.",
            ForeColor = Color.FromArgb(205, 211, 222),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 0, 1),
            UseCompatibleTextRendering = true
        };
        contentPanel.Controls.Add(_detailLabel);

        _progressTrack = new Panel
        {
            Location = new Point(25, 126),
            Size = new Size(842, 10),
            BackColor = Color.FromArgb(55, 65, 76)
        };

        _progressFill = new Panel
        {
            Location = Point.Empty,
            Size = new Size(0, 10),
            BackColor = ParseColor("#55ff00", Color.FromArgb(85, 255, 0))
        };
        _progressTrack.Controls.Add(_progressFill);
        contentPanel.Controls.Add(_progressTrack);

        var footerLabel = new Label
        {
            AutoSize = true,
            Location = new Point(34, 516),
            Text = "U-CREW • u-crew.net",
            ForeColor = Color.FromArgb(195, 205, 215),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            UseCompatibleTextRendering = true
        };
        shade.Controls.Add(footerLabel);

        Shown += async (_, _) => await RunAsync();
        FormClosing += OnFormClosing;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape)
            {
                RequestClose();
            }
        };

        ResumeLayout(false);
    }

    private async Task RunAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var progress = new Progress<LauncherProgress>(item =>
            SetProgress(item.Percent, item.Status, item.Detail));

        try
        {
            SetProgress(2, "Başlatıcı hazırlanıyor…", "Önceki yama oturumları kontrol ediliyor.");
            await Task.Run(_runtime.RecoverStaleSession, _cancellation.Token);

            using PreparedPatch preparedPatch = await _apiClient.AcquireAsync(
                progress,
                _cancellation.Token);

            _gameLabel.Text = string.IsNullOrWhiteSpace(preparedPatch.Ticket.GameTitle)
                ? _config.GameSlug.ToUpperInvariant() + " • TÜRKÇE YAMA"
                : preparedPatch.Ticket.GameTitle.ToUpperInvariant() + " • TÜRKÇE YAMA";

            RuntimeProfile profile = preparedPatch.Ticket.RuntimeProfile;
            if (!string.IsNullOrWhiteSpace(profile.Theme?.Accent))
            {
                _progressFill.BackColor = ParseColor(profile.Theme.Accent, _progressFill.BackColor);
            }

            InstallSession session = await Task.Run(
                () => _runtime.Install(preparedPatch, progress, _cancellation.Token),
                _cancellation.Token);

            await Task.Delay(500, _cancellation.Token);
            SetProgress(100, "Oyun başlatılıyor…", "Türkçe yama etkin. İyi oyunlar!");
            await Task.Delay(500, _cancellation.Token);

            using Process process = _runtime.StartGame(profile);

            if (_config.CloseWindowWhenGameStarts)
            {
                Hide();
            }
            else
            {
                SetProgress(100, "Oyun çalışıyor.", "Oyun kapanınca geçici yama dosyaları temizlenecek.");
            }

            if (profile.WaitForGameExit)
            {
                await _runtime.WaitForGameExitAsync(
                    process,
                    profile,
                    _cancellation.Token);
            }

            if (session.CleanupOnExit)
            {
                await Task.Run(_runtime.CleanupActiveSession);
            }
            else
            {
                await Task.Run(_runtime.CompletePersistentSession);
            }

            _allowClose = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            await SafeCleanupAsync();
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            Log(exception.ToString());
            await SafeCleanupAsync();
            _allowClose = true;

            MessageBox.Show(
                "Türkçe yama başlatılamadı.\n\n" + exception.Message +
                "\n\nAyrıntı: %LOCALAPPDATA%\\U-CREW\\SecurePatch\\" +
                _config.GameSlug + "\\client.log",
                "U-CREW Güvenli Yama",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
        }
    }

    private async Task SafeCleanupAsync()
    {
        try
        {
            await Task.Run(_runtime.CleanupActiveSession);
        }
        catch
        {
        }
    }

    private void RequestClose()
    {
        if (_allowClose)
        {
            Close();
            return;
        }

        _closeButton.Enabled = false;
        SetProgress(
            Math.Max(1, GetCurrentProgress()),
            "İşlem iptal ediliyor…",
            "Geçici yama dosyaları güvenli biçimde temizleniyor.");
        _cancellation.Cancel();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        RequestClose();
    }

    private void SetProgress(int percent, string status, string detail)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetProgress(percent, status, detail));
            return;
        }

        int safePercent = Math.Clamp(percent, 0, 100);
        _statusLabel.Text = status;
        _detailLabel.Text = detail;
        _progressFill.Width = (int)Math.Round(
            _progressTrack.ClientSize.Width * (safePercent / 100d));
    }

    private int GetCurrentProgress()
    {
        if (_progressTrack.ClientSize.Width <= 0)
        {
            return 0;
        }

        return (int)Math.Round(
            (_progressFill.Width / (double)_progressTrack.ClientSize.Width) * 100d);
    }

    private void Log(string message)
    {
        FileSystemUtil.AppendLog(_logPath, message);
        FileSystemUtil.SetHiddenSystem(_privateRoot);
    }

    private static Image? LoadImageUnlocked(ClientConfig config, string path)
    {
        try
        {
            if (ConfigLoader.TryReadEmbeddedFile(path, out byte[] embeddedBytes))
            {
                using var memory = new MemoryStream(embeddedBytes, writable: false);
                using Image embeddedImage = Image.FromStream(memory);
                return new Bitmap(embeddedImage);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using Image image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed class GradientShadePanel : Panel
{
    public GradientShadePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        base.OnPaintBackground(eventArgs);

        using var gradient = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(25, 0, 0, 0),
            Color.FromArgb(175, 0, 0, 0),
            LinearGradientMode.Vertical);
        eventArgs.Graphics.FillRectangle(gradient, ClientRectangle);
    }
}
