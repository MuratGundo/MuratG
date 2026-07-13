using System.Text.Json;

namespace UCREW.SecurePatch.Manager;

internal sealed class MainForm : Form
{
    private readonly TextBox _host = new() { Text = "185.8.129.202" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 22 };
    private readonly TextBox _user = new() { Text = "root" };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _database = new() { Text = "ucrewnet_ucrew_patch_v3" };
    private readonly TextBox _appRoot = new() { Text = "/var/www/api.u-crew.net" };

    private readonly TextBox _slug = new() { Text = "guardians" };
    private readonly TextBox _gameName = new() { Text = "Marvel's Guardians of the Galaxy: The Telltale Series" };
    private readonly TextBox _gameExe = new() { Text = "Guardians.exe" };
    private readonly ComboBox _installMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _targetPath = new() { Text = "archives" };
    private readonly TextBox _extensions = new() { Text = ".landb;.font;.fnt;.dds;.d3dtx" };
    private readonly CheckBox _cleanup = new() { Text = "Oyun kapanınca temizle", Checked = true, AutoSize = true };
    private readonly CheckBox _preserveTree = new() { Text = "Klasör yapısını koru", Checked = false, AutoSize = true };
    private readonly CheckBox _bootstrap = new() { Text = "Bootstrap gerekli", Checked = true, AutoSize = true };
    private readonly ComboBox _bootstrapType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _waitForExit = new() { Text = "Oyunun kapanmasını bekle", Checked = true, AutoSize = true };
    private readonly CheckBox _backupExisting = new() { Text = "Mevcut dosyaları yedekle", Checked = true, AutoSize = true };

    private readonly TextBox _sourcePath = new();
    private readonly TextBox _outputPath = new();
    private readonly TextBox _version = new() { Text = "1.0.0" };
    private readonly TextBox _channel = new() { Text = "stable" };
    private readonly TextBox _metadataPath = new();

    private readonly RichTextBox _log = new()
    {
        ReadOnly = true,
        BackColor = Color.FromArgb(12, 15, 22),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9.5f)
    };

    private readonly ToolStripStatusLabel _status = new("Hazır");
    private readonly ToolStripProgressBar _progress = new() { Minimum = 0, Maximum = 100, Width = 220 };
    private PackageResult? _lastPackage;
    private bool _busy;

    public MainForm()
    {
        Text = "U-CREW Güvenli Yama Yöneticisi";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 760);
        Size = new Size(1220, 820);
        BackColor = Color.FromArgb(20, 23, 31);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        _installMode.Items.AddRange(new object[]
        {
            "overlay_flat",
            "overlay_tree",
            "replace_files",
            "archive_replace",
            "custom"
        });
        _installMode.SelectedItem = "overlay_flat";

        _bootstrapType.Items.AddRange(new object[]
        {
            "none",
            "version_proxy",
            "winmm_proxy",
            "launcher",
            "custom"
        });
        _bootstrapType.SelectedItem = "version_proxy";

        var header = BuildHeader();
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 6)
        };
        tabs.TabPages.Add(BuildServerTab());
        tabs.TabPages.Add(BuildProfileTab());
        tabs.TabPages.Add(BuildPackageTab());
        tabs.TabPages.Add(BuildPublishTab());
        tabs.TabPages.Add(BuildLogTab());

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(_progress);

        Controls.Add(tabs);
        Controls.Add(header);
        Controls.Add(statusStrip);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 86,
            BackColor = Color.FromArgb(10, 12, 18),
            Padding = new Padding(24, 14, 24, 10)
        };

        var title = new Label
        {
            Text = "U-CREW GÜVENLİ YAMA YÖNETİCİSİ",
            AutoSize = true,
            ForeColor = Color.FromArgb(92, 255, 48),
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            Location = new Point(24, 12)
        };
        var subtitle = new Label
        {
            Text = "Sunucu kurulumu • Oyun profili • Şifreleme • Yükleme • MySQL kaydı",
            AutoSize = true,
            ForeColor = Color.Silver,
            Location = new Point(27, 52)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private TabPage BuildServerTab()
    {
        var page = CreatePage("1. Sunucu Kurulumu");
        var layout = CreateFormLayout();
        AddRow(layout, 0, "VPS adresi", _host);
        AddRow(layout, 1, "SSH portu", _port);
        AddRow(layout, 2, "SSH kullanıcı", _user);
        AddRow(layout, 3, "SSH şifresi", _password);
        AddRow(layout, 4, "MySQL veritabanı", _database);
        AddRow(layout, 5, "API uygulama kökü", _appRoot);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(CreateButton("Bağlantıyı Test Et", async (_, _) => await TestConnectionAsync()));
        buttons.Controls.Add(CreatePrimaryButton("Genel Sistemi VPS'e Kur", async (_, _) => await InstallServerAsync()));
        layout.Controls.Add(buttons, 1, 6);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildProfileTab()
    {
        var page = CreatePage("2. Oyun Profili");
        var layout = CreateFormLayout();
        AddRow(layout, 0, "Oyun slug", _slug);
        AddRow(layout, 1, "Oyun adı", _gameName);
        AddRow(layout, 2, "Oyun EXE", _gameExe);
        AddRow(layout, 3, "Kurulum modu", _installMode);
        AddRow(layout, 4, "Hedef klasör", _targetPath);
        AddRow(layout, 5, "Dosya uzantıları", _extensions);

        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        checks.Controls.AddRange(new Control[]
        {
            _cleanup, _preserveTree, _bootstrap, _waitForExit, _backupExisting
        });
        layout.Controls.Add(new Label { Text = "Davranış", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
        layout.Controls.Add(checks, 1, 6);
        AddRow(layout, 7, "Bootstrap tipi", _bootstrapType);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(CreateButton("Profil Aç", (_, _) => LoadProfile()));
        buttons.Controls.Add(CreatePrimaryButton("Profili Kaydet", (_, _) => SaveProfile()));
        layout.Controls.Add(buttons, 1, 8);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPackageTab()
    {
        var page = CreatePage("3. Şifreli Paket Oluştur");
        var layout = CreateFormLayout();

        AddBrowseRow(layout, 0, "Yama ZIP / klasör", _sourcePath, BrowseSource);
        AddBrowseRow(layout, 1, "Çıktı klasörü", _outputPath, BrowseOutputFolder);
        AddRow(layout, 2, "Sürüm", _version);
        AddRow(layout, 3, "Kanal", _channel);

        var info = new Label
        {
            Text = "AES-256-CBC şifreli .ucp, metadata, profil ve MySQL kayıt dosyası birlikte oluşturulur.",
            AutoSize = true,
            ForeColor = Color.Silver,
            Padding = new Padding(0, 8, 0, 8)
        };
        layout.Controls.Add(info, 1, 4);
        layout.Controls.Add(CreatePrimaryButton("Paketi Hazırla", async (_, _) => await BuildPackageAsync()), 1, 5);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPublishTab()
    {
        var page = CreatePage("4. Sunucuya Yayınla");
        var layout = CreateFormLayout();
        AddBrowseRow(layout, 0, "Metadata dosyası", _metadataPath, BrowseMetadata);

        var warning = new Label
        {
            Text = "Yayınlama işlemi .ucp dosyasını VPS'e yükler, sunucuda SHA-256 doğrular ve MySQL kaydını oluşturur.",
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 200, 70),
            Padding = new Padding(0, 10, 0, 10)
        };
        layout.Controls.Add(warning, 1, 1);
        layout.Controls.Add(CreatePrimaryButton("Paketi VPS'e Yükle ve Yayınla", async (_, _) => await PublishAsync()), 1, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = CreatePage("5. İşlem Günlüğü");
        _log.Dock = DockStyle.Fill;
        page.Controls.Add(_log);
        return page;
    }

    private async Task TestConnectionAsync()
    {
        await RunBusyAsync("SSH bağlantısı test ediliyor...", () =>
            SshService.TestConnection(ReadServerSettings(), AppendLog));
    }

    private async Task InstallServerAsync()
    {
        await RunBusyAsync("Genel sistem VPS'e kuruluyor...", () =>
            SshService.InstallServer(ReadServerSettings(), AppendLog));
    }

    private async Task BuildPackageAsync()
    {
        await RunBusyAsync("Şifreli paket hazırlanıyor...", () =>
        {
            GameProfile profile = ReadProfile();
            _lastPackage = PackageBuilder.Build(
                profile,
                _sourcePath.Text.Trim(),
                _outputPath.Text.Trim(),
                _version.Text.Trim(),
                _channel.Text.Trim(),
                AppendLog);
            BeginInvoke(() => _metadataPath.Text = _lastPackage.MetadataPath);
        });
    }

    private async Task PublishAsync()
    {
        await RunBusyAsync("Paket VPS'e yayınlanıyor...", () =>
        {
            PackageResult package = ResolvePackageForPublish();
            SshService.Publish(ReadServerSettings(), package, AppendLog);
        });
    }

    private PackageResult ResolvePackageForPublish()
    {
        string metadataPath = _metadataPath.Text.Trim();
        if (_lastPackage is not null &&
            string.Equals(_lastPackage.MetadataPath, metadataPath, StringComparison.OrdinalIgnoreCase))
        {
            return _lastPackage;
        }

        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("Metadata dosyası bulunamadı.", metadataPath);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        JsonElement root = document.RootElement;
        string directory = Path.GetDirectoryName(metadataPath)!;
        string slug = root.GetProperty("game_slug").GetString() ?? "";
        string channel = root.GetProperty("channel").GetString() ?? "stable";
        string fileName = root.GetProperty("encrypted_file").GetString() ?? "";

        return new PackageResult
        {
            MetadataPath = metadataPath,
            EncryptedPackagePath = Path.Combine(directory, fileName),
            SqlPath = Path.Combine(directory, $"{slug}_{channel}_REGISTER.sql"),
            ProfilePath = Path.Combine(directory, $"{slug}_profile.json"),
            Sha256 = root.GetProperty("sha256").GetString() ?? "",
            GameSlug = slug,
            Version = root.GetProperty("version").GetString() ?? "",
            Channel = channel,
            FileName = fileName
        };
    }

    private ServerSettings ReadServerSettings() => new()
    {
        Host = _host.Text.Trim(),
        Port = (int)_port.Value,
        User = _user.Text.Trim(),
        Password = _password.Text,
        DatabaseName = _database.Text.Trim(),
        AppRoot = _appRoot.Text.Trim().TrimEnd('/')
    };

    private GameProfile ReadProfile() => new()
    {
        GameSlug = _slug.Text.Trim().ToLowerInvariant(),
        GameName = _gameName.Text.Trim(),
        GameExe = _gameExe.Text.Trim(),
        InstallMode = _installMode.SelectedItem?.ToString() ?? "overlay_tree",
        TargetPath = _targetPath.Text.Trim(),
        AllowedExtensions = _extensions.Text
            .Split(new[] { ';', ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.StartsWith('.') ? value.ToLowerInvariant() : "." + value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        CleanupOnExit = _cleanup.Checked,
        PreserveDirectoryTree = _preserveTree.Checked,
        RequiresBootstrap = _bootstrap.Checked,
        BootstrapType = _bootstrapType.SelectedItem?.ToString() ?? "launcher",
        WaitForGameExit = _waitForExit.Checked,
        BackupExistingFiles = _backupExisting.Checked
    };

    private void ApplyProfile(GameProfile profile)
    {
        _slug.Text = profile.GameSlug;
        _gameName.Text = profile.GameName;
        _gameExe.Text = profile.GameExe;
        _installMode.SelectedItem = profile.InstallMode;
        _targetPath.Text = profile.TargetPath;
        _extensions.Text = string.Join(';', profile.AllowedExtensions);
        _cleanup.Checked = profile.CleanupOnExit;
        _preserveTree.Checked = profile.PreserveDirectoryTree;
        _bootstrap.Checked = profile.RequiresBootstrap;
        _bootstrapType.SelectedItem = profile.BootstrapType;
        _waitForExit.Checked = profile.WaitForGameExit;
        _backupExisting.Checked = profile.BackupExistingFiles;
    }

    private void SaveProfile()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "U-CREW oyun profili (*.json)|*.json",
            FileName = _slug.Text.Trim() + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(ReadProfile(), new JsonSerializerOptions { WriteIndented = true }));
        AppendLog("Profil kaydedildi: " + dialog.FileName);
    }

    private void LoadProfile()
    {
        using var dialog = new OpenFileDialog { Filter = "U-CREW oyun profili (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        GameProfile profile = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(dialog.FileName),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Profil okunamadı.");
        ApplyProfile(profile);
        AppendLog("Profil açıldı: " + dialog.FileName);
    }

    private void BrowseSource()
    {
        using var choice = new OpenFileDialog { Filter = "ZIP paketi (*.zip)|*.zip|Tüm dosyalar (*.*)|*.*" };
        if (choice.ShowDialog(this) == DialogResult.OK)
        {
            _sourcePath.Text = choice.FileName;
            return;
        }

        using var folder = new FolderBrowserDialog { Description = "Yama klasörünü seç" };
        if (folder.ShowDialog(this) == DialogResult.OK) _sourcePath.Text = folder.SelectedPath;
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Çıktı klasörünü seç" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.SelectedPath;
    }

    private void BrowseMetadata()
    {
        using var dialog = new OpenFileDialog { Filter = "U-CREW metadata (*_metadata.json)|*_metadata.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _metadataPath.Text = dialog.FileName;
    }

    private async Task RunBusyAsync(string status, Action action)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(status, 15);

        try
        {
            await Task.Run(action);
            SetStatus("İşlem başarıyla tamamlandı.", 100);
            MessageBox.Show(this, "İşlem başarıyla tamamlandı.", "U-CREW", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            AppendLog("HATA: " + exception);
            SetStatus("İşlem başarısız.", 0);
            MessageBox.Show(this, exception.Message, "U-CREW Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void SetStatus(string text, int progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text, progress));
            return;
        }

        _status.Text = text;
        _progress.Value = Math.Clamp(progress, 0, 100);
    }

    private static TabPage CreatePage(string title) => new(title)
    {
        BackColor = Color.FromArgb(24, 27, 36),
        ForeColor = Color.White,
        Padding = new Padding(22)
    };

    private static TableLayoutPanel CreateFormLayout() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        RowCount = 12,
        Padding = new Padding(10),
        BackColor = Color.FromArgb(24, 27, 36)
    };

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var text = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 10, 16, 10),
            ForeColor = Color.Gainsboro
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 6, 3, 6);
        control.MinimumSize = new Size(420, 30);
        layout.Controls.Add(text, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static void AddBrowseRow(TableLayoutPanel layout, int row, string label, TextBox textBox, Action browse)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        textBox.Dock = DockStyle.Fill;
        var button = CreateButton("Gözat", (_, _) => browse());
        panel.Controls.Add(textBox, 0, 0);
        panel.Controls.Add(button, 1, 0);
        AddRow(layout, row, label, panel);
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(14, 5, 14, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 54, 68),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 110);
        button.Click += handler;
        return button;
    }

    private static Button CreatePrimaryButton(string text, EventHandler handler)
    {
        Button button = CreateButton(text, handler);
        button.BackColor = Color.FromArgb(52, 150, 35);
        button.FlatAppearance.BorderColor = Color.FromArgb(95, 255, 55);
        button.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        return button;
    }
}
