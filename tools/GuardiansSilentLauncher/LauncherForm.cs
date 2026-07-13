using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace UCREW.GuardiansTR;

internal sealed class LauncherForm : Form
{
    private const string ParentPidPrefix = "--ucrew-parent-pid=";
    private const string ReadyEventPrefix = "--ucrew-ready-event=";

    private readonly LauncherEngine _engine;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly int? _parentProcessId;
    private readonly string? _readyEventName;
    private readonly bool _preloadMode;

    private readonly Label _statusLabel;
    private readonly Label _detailLabel;
    private readonly Panel _progressTrack;
    private readonly Panel _progressFill;
    private readonly Button _closeButton;

    private bool _allowClose;
    private bool _started;
    private bool _readySignaled;

    public LauncherForm(IReadOnlyList<string> arguments)
    {
        _parentProcessId = ParseParentProcessId(arguments);
        _readyEventName = ParseArgument(arguments, ReadyEventPrefix);
        _preloadMode = _parentProcessId.HasValue &&
            !string.IsNullOrWhiteSpace(_readyEventName);

        string[] gameArguments = arguments
            .Where(argument =>
                !argument.StartsWith(ParentPidPrefix, StringComparison.OrdinalIgnoreCase) &&
                !argument.StartsWith(ReadyEventPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _engine = new LauncherEngine(gameArguments);

        SuspendLayout();

        Text = "U-CREW Guardians Türkçe";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(960, 540);
        MinimumSize = new Size(960, 540);
        MaximumSize = new Size(960, 540);
        BackColor = Color.FromArgb(5, 7, 14);
        ShowInTaskbar = true;
        DoubleBuffered = true;
        KeyPreview = true;

        BackgroundImage = LoadImageUnlocked(_engine.BackgroundPath);
        BackgroundImageLayout = ImageLayout.Stretch;

        var shade = new GradientShadePanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        Controls.Add(shade);

        var logo = new PictureBox
        {
            Location = new Point(34, 26),
            Size = new Size(360, 74),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadImageUnlocked(_engine.LogoPath)
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
            Location = new Point(34, 354),
            Size = new Size(892, 150),
            BackColor = Color.FromArgb(205, 6, 9, 16)
        };

        shade.Controls.Add(contentPanel);

        var titleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 18),
            Text = "GALAKSİNİN KORUYUCULARI • TÜRKÇE YAMA",
            ForeColor = Color.FromArgb(169, 255, 45),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold)
        };

        contentPanel.Controls.Add(titleLabel);

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 43),
            Size = new Size(844, 36),
            Text = "Başlatıcı hazırlanıyor…",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 21f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        contentPanel.Controls.Add(_statusLabel);

        _detailLabel = new Label
        {
            AutoSize = false,
            Location = new Point(25, 80),
            Size = new Size(842, 24),
            Text = "U-CREW güvenli yama sistemi başlatılıyor.",
            ForeColor = Color.FromArgb(205, 211, 222),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };

        contentPanel.Controls.Add(_detailLabel);

        _progressTrack = new Panel
        {
            Location = new Point(25, 116),
            Size = new Size(842, 10),
            BackColor = Color.FromArgb(55, 65, 76)
        };

        _progressFill = new Panel
        {
            Location = Point.Empty,
            Size = new Size(0, 10),
            BackColor = Color.FromArgb(126, 255, 0)
        };

        _progressTrack.Controls.Add(_progressFill);
        contentPanel.Controls.Add(_progressTrack);

        var footerLabel = new Label
        {
            AutoSize = true,
            Location = new Point(34, 511),
            Text = "U-CREW • u-crew.net",
            ForeColor = Color.FromArgb(195, 205, 215),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
        };

        shade.Controls.Add(footerLabel);

        Shown += async (_, _) => await RunLauncherAsync();
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

    private async Task RunLauncherAsync()
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
            SetProgress(
                2,
                "Başlatıcı hazırlanıyor…",
                "U-CREW dosyaları gizli alanda hazırlanıyor.");

            await Task.Delay(250, _cancellation.Token);

            await Task.Run(
                () => _engine.PreparePatch(progress, _cancellation.Token),
                _cancellation.Token);

            await Task.Delay(650, _cancellation.Token);

            SetProgress(
                100,
                _preloadMode ? "Yama kuruldu." : "Oyun başlatılıyor…",
                _preloadMode
                    ? "Guardians.exe Türkçe olarak açılıyor."
                    : "Türkçe yama etkin. İyi oyunlar!");

            await Task.Delay(500, _cancellation.Token);

            if (_preloadMode)
            {
                SignalReadyEvent();
                Hide();

                await WaitForParentGameAsync();
                await Task.Run(_engine.Cleanup);
            }
            else
            {
                using Process process = _engine.StartGame();

                Hide();
                await process.WaitForExitAsync();

                await Task.Run(_engine.Cleanup);
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
            _engine.LogException(exception);
            await SafeCleanupAsync();

            _allowClose = true;

            MessageBox.Show(
                "Türkçe yama başlatılamadı.\n\n" +
                exception.Message +
                "\n\nAyrıntı: gizli .ucrew\\ucrew_launcher.log",
                "U-CREW Guardians Türkçe",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
        }
    }

    private async Task WaitForParentGameAsync()
    {
        if (!_parentProcessId.HasValue)
        {
            return;
        }

        try
        {
            using Process parent = Process.GetProcessById(_parentProcessId.Value);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // Oyun bu sırada zaten kapanmış olabilir.
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SignalReadyEvent()
    {
        if (_readySignaled || string.IsNullOrWhiteSpace(_readyEventName))
        {
            return;
        }

        try
        {
            using EventWaitHandle readyEvent =
                EventWaitHandle.OpenExisting(_readyEventName);

            readyEvent.Set();
            _readySignaled = true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task SafeCleanupAsync()
    {
        try
        {
            await Task.Run(_engine.Cleanup);
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
            "Geçici yama dosyaları temizleniyor.");
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

        _progressFill.Invalidate();
        _progressTrack.Invalidate();
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

    private static int? ParseParentProcessId(IReadOnlyList<string> arguments)
    {
        string? value = ParseArgument(arguments, ParentPidPrefix);
        return int.TryParse(value, out int processId) && processId > 0
            ? processId
            : null;
    }

    private static string? ParseArgument(
        IReadOnlyList<string> arguments,
        string prefix)
    {
        foreach (string argument in arguments)
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static Image? LoadImageUnlocked(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
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
            Color.FromArgb(150, 0, 0, 0),
            LinearGradientMode.Vertical);

        eventArgs.Graphics.FillRectangle(gradient, ClientRectangle);
    }
}
