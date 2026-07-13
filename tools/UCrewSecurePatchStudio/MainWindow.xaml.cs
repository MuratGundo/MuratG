using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace UCREW.SecurePatchStudio;

public partial class MainWindow : Window
{
    private readonly StudioLogger _logger = new();
    private readonly SettingsService _settingsService = new();
    private readonly ProfileService _profileService = new();
    private readonly PackageBuilderService _packageBuilder;
    private readonly ServerDeploymentService _deploymentService;
    private readonly PatchPublisherService _publisherService;

    private StudioSettings _settings;
    private string _currentProfilePath = string.Empty;
    private CancellationTokenSource? _operationCancellation;

    public MainWindow()
    {
        InitializeComponent();

        _packageBuilder = new PackageBuilderService(_logger);
        _deploymentService = new ServerDeploymentService(_logger);
        _publisherService = new PatchPublisherService(_logger);
        _settings = _settingsService.Load();

        _logger.MessageWritten += Logger_MessageWritten;

        LoadSettingsToUi();
        LoadBundledClient();
        LoadDefaultProfile();
        RefreshLogs();
        UpdatePublishSummary();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _operationCancellation?.Cancel();
        Close();
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index))
        {
            MainTabs.SelectedIndex = index;

            if (index == 4)
            {
                UpdatePublishSummary();
            }
            else if (index == 5)
            {
                RefreshLogs();
            }
        }
    }

    private async void DeployServerButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(
            DeployServerButton,
            "Sunucu kuruluyor...",
            async (progress, token) =>
            {
                ReadSettingsFromUi();
                _settingsService.Save(_settings);

                OperationResult result = await _deploymentService.DeployAsync(
                    _settings,
                    SshPasswordBox.Password,
                    DatabasePasswordBox.Password,
                    progress,
                    token);

                DashboardStatusText.Text = "Sunucu ve MySQL şeması hazır.";
                MessageBox.Show(
                    result.Message + Environment.NewLine + Environment.NewLine + result.Details,
                    "U-CREW Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadSettingsFromUi();
            _settingsService.Save(_settings);
            UpdatePublishSummary();
            SetStatus("Ayarlar kaydedildi.", 0);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _currentProfilePath = string.Empty;
        ApplyProfileToUi(new GameProfile
        {
            GameSlug = "oyun-slug",
            GameName = "Oyun Adı",
            GameExe = "Game.exe",
            InstallMode = "overlay_tree",
            TargetPath = string.Empty,
            AllowedExtensions = new[] { ".txt" },
            CleanupOnExit = true,
            PreserveDirectoryTree = true,
            BackupExistingFiles = true,
            WaitForGameExit = true
        });
        CurrentProfilePathText.Text = "Yeni profil";
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "U-CREW oyun profilini seç",
            Filter = "U-CREW oyun profili (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            GameProfile profile = _profileService.Load(dialog.FileName);
            _currentProfilePath = dialog.FileName;
            ApplyProfileToUi(profile);
            CurrentProfilePathText.Text = dialog.FileName;
            BuildProfilePathBox.Text = dialog.FileName;
            _settings.LastProfilePath = dialog.FileName;
            _settingsService.Save(_settings);
            SetStatus("Profil açıldı: " + profile.GameName, 0);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GameProfile profile = ReadProfileFromUi();
            string path = _currentProfilePath;

            if (string.IsNullOrWhiteSpace(path))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Oyun profilini kaydet",
                    Filter = "U-CREW oyun profili (*.json)|*.json",
                    FileName = profile.GameSlug + ".json"
                };

                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                path = dialog.FileName;
            }

            _profileService.Save(path, profile);
            _currentProfilePath = path;
            CurrentProfilePathText.Text = path;
            BuildProfilePathBox.Text = path;
            _settings.LastProfilePath = path;
            _settingsService.Save(_settings);
            SetStatus("Profil kaydedildi: " + profile.GameName, 0);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void SelectProfileLogoButton_Click(object sender, RoutedEventArgs e)
    {
        SelectImageInto(ProfileLogoPathBox, "U-CREW logosunu seç");
    }

    private void SelectProfileBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        SelectImageInto(ProfileBackgroundPathBox, "Uygulama arka planını seç");
    }

    private void SelectImageInto(TextBox target, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Resim dosyaları (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private void SelectBuildProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Paket için oyun profilini seç",
            Filter = "U-CREW oyun profili (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            BuildProfilePathBox.Text = dialog.FileName;
            _settings.LastProfilePath = dialog.FileName;
            _settingsService.Save(_settings);
        }
    }

    private void SelectSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Yama dosyalarının bulunduğu klasörü seç"
        };

        if (dialog.ShowDialog(this) == true)
        {
            BuildSourcePathBox.Text = dialog.FolderName;
            _settings.LastSourcePath = dialog.FolderName;
            _settingsService.Save(_settings);
        }
    }

    private void SelectSourceZipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Yama ZIP paketini seç",
            Filter = "ZIP dosyası (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) == true)
        {
            BuildSourcePathBox.Text = dialog.FileName;
            _settings.LastSourcePath = dialog.FileName;
            _settingsService.Save(_settings);
        }
    }

    private void SelectOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Şifreli yama çıktılarının kaydedileceği klasörü seç"
        };

        if (dialog.ShowDialog(this) == true)
        {
            BuildOutputPathBox.Text = dialog.FolderName;
            _settings.LastOutputPath = dialog.FolderName;
            _settingsService.Save(_settings);
        }
    }

    private async void BuildPackageButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(
            BuildPackageButton,
            "Şifreli yama paketi hazırlanıyor...",
            async (progress, token) =>
            {
                string profilePath = Path.GetFullPath(BuildProfilePathBox.Text.Trim());
                GameProfile profile = _profileService.Load(profilePath);

                PackageBuildResult result = await _packageBuilder.BuildAsync(
                    profile,
                    BuildSourcePathBox.Text.Trim(),
                    BuildOutputPathBox.Text.Trim(),
                    BuildVersionBox.Text.Trim(),
                    BuildChannelBox.Text.Trim(),
                    progress,
                    token);

                BuildResultText.Text =
                    $"Paket: {result.EncryptedPackagePath}\n" +
                    $"Metadata: {result.MetadataPath}\n" +
                    $"SQL: {result.SqlPath}\n" +
                    $"SHA-256: {result.Metadata.Sha256}";
                PublishMetadataPathBox.Text = result.MetadataPath;
                _settings.LastMetadataPath = result.MetadataPath;
                _settings.LastSourcePath = BuildSourcePathBox.Text.Trim();
                _settings.LastOutputPath = BuildOutputPathBox.Text.Trim();
                _settingsService.Save(_settings);

                DashboardStatusText.Text =
                    $"{result.Metadata.GameName} {result.Metadata.Version} paketi hazır.";

                MessageBox.Show(
                    "Şifreli .ucp paketi başarıyla oluşturuldu.",
                    "U-CREW Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private void SelectClientExeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Genel U-CREW güvenli yama istemcisini seç",
            Filter = "U-CREW güvenli istemci (UCREW_SecurePatch.exe)|UCREW_SecurePatch.exe|EXE (*.exe)|*.exe"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ClientExePathBox.Text = dialog.FileName;
        }
    }

    private async void BuildClientPackageButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(
            BuildClientPackageButton,
            "Oyuna özel yama uygulaması hazırlanıyor...",
            async (progress, token) =>
            {
                string profilePath = Path.GetFullPath(BuildProfilePathBox.Text.Trim());
                string clientExePath = ResolveClientExePath();
                string outputRoot = Path.GetFullPath(BuildOutputPathBox.Text.Trim());

                if (!File.Exists(profilePath))
                    throw new FileNotFoundException("Önce kaydedilmiş oyun profilini seçin.", profilePath);
                if (!File.Exists(clientExePath))
                    throw new FileNotFoundException(
                        "Studio paketindeki güncel UCREW_SecurePatch.exe bulunamadı. Gerekirse dosyayı elle seçin.",
                        clientExePath);

                GameProfile profile = _profileService.Load(profilePath);
                string logoPath = profile.Theme?.Logo ?? string.Empty;
                string backgroundPath = profile.Theme?.Background ?? string.Empty;

                if (!File.Exists(logoPath))
                    throw new FileNotFoundException("Profilde seçilen logo bulunamadı.", logoPath);
                if (!File.Exists(backgroundPath))
                    throw new FileNotFoundException("Profilde seçilen arka plan bulunamadı.", backgroundPath);

                Directory.CreateDirectory(outputRoot);
                string staging = Path.Combine(
                    Path.GetTempPath(),
                    "UCREW_CLIENT_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                string payloadPath = staging + ".zip";

                try
                {
                    progress.Report(20);
                    string logoName = "ucrew-logo" + Path.GetExtension(logoPath).ToLowerInvariant();
                    string backgroundName = "game-background" + Path.GetExtension(backgroundPath).ToLowerInvariant();

                    File.Copy(logoPath, Path.Combine(staging, logoName), true);
                    File.Copy(backgroundPath, Path.Combine(staging, backgroundName), true);

                    var gameConfig = new
                    {
                        Enabled = true,
                        ApiBase = "https://api.u-crew.net/api/",
                        FallbackApiBase = "",
                        GameSlug = profile.GameSlug,
                        Channel = string.IsNullOrWhiteSpace(BuildChannelBox.Text)
                            ? "stable"
                            : BuildChannelBox.Text.Trim(),
                        GameRoot = ".",
                        GameExe = profile.GameExe,
                        GameArguments = Array.Empty<string>(),
                        LogoPath = logoName,
                        BackgroundPath = backgroundName,
                        WindowTitle = "U-CREW " + profile.GameName + " Türkçe Yama",
                        HideRuntimeFiles = true,
                        CloseWindowWhenGameStarts = true
                    };

                    File.WriteAllText(
                        Path.Combine(staging, "ucrew_game.json"),
                        JsonSerializer.Serialize(gameConfig, new JsonSerializerOptions { WriteIndented = true }),
                        new UTF8Encoding(false));
                    progress.Report(55);
                    string exePath = Path.Combine(
                        outputRoot,
                        "UCREW_" + profile.GameSlug + "_Turkce_Yama.exe");
                    if (File.Exists(exePath))
                        File.Delete(exePath);
                    if (File.Exists(payloadPath))
                        File.Delete(payloadPath);

                    await Task.Run(() =>
                    {
                        ZipFile.CreateFromDirectory(
                            staging,
                            payloadPath,
                            CompressionLevel.Optimal,
                            includeBaseDirectory: false);

                        using var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        using (FileStream client = File.OpenRead(clientExePath))
                            client.CopyTo(output);
                        using (FileStream payload = File.OpenRead(payloadPath))
                            payload.CopyTo(output);

                        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
                        writer.Write(new FileInfo(payloadPath).Length);
                        writer.Write(Encoding.ASCII.GetBytes("UCREW_PAYLOAD_V1"));
                        output.Flush(flushToDisk: true);
                    }, token);

                    progress.Report(100);
                    ClientPackageResultText.Text = "Tek EXE hazır: " + exePath;
                    MessageBox.Show(
                        "Logo, arka plan ve oyun ayarları içine gömülmüş tek EXE başarıyla oluşturuldu.",
                        "U-CREW Studio",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                finally
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                    if (File.Exists(payloadPath))
                        File.Delete(payloadPath);
                }
            });
    }

    private void SelectMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Yayınlanacak metadata dosyasını seç",
            Filter = "U-CREW metadata (*_metadata.json)|*_metadata.json|JSON (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            PublishMetadataPathBox.Text = dialog.FileName;
            _settings.LastMetadataPath = dialog.FileName;
            _settingsService.Save(_settings);
        }
    }

    private async void PublishButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(
            PublishButton,
            "Yama sunucuya yayınlanıyor...",
            async (progress, token) =>
            {
                ReadSettingsFromUi();
                _settingsService.Save(_settings);

                OperationResult result = await _publisherService.PublishAsync(
                    _settings,
                    SshPasswordBox.Password,
                    DatabasePasswordBox.Password,
                    Path.GetFullPath(PublishMetadataPathBox.Text.Trim()),
                    progress,
                    token);

                PublishResultText.Text = result.Message + Environment.NewLine + result.Details;
                DashboardStatusText.Text = result.Message;

                MessageBox.Show(
                    result.Message,
                    "U-CREW Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private void RefreshLogsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogs();
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.Clear();
        RefreshLogs();
    }

    private async Task RunOperationAsync(
        Button actionButton,
        string initialStatus,
        Func<IProgress<int>, CancellationToken, Task> operation)
    {
        if (_operationCancellation is not null)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        actionButton.IsEnabled = false;
        var progress = new Progress<int>(value => SetStatus(initialStatus, value));

        try
        {
            SetStatus(initialStatus, 1);
            await operation(progress, _operationCancellation.Token);
            SetStatus("İşlem başarıyla tamamlandı.", 100);
        }
        catch (OperationCanceledException)
        {
            SetStatus("İşlem iptal edildi.", 0);
        }
        catch (Exception exception)
        {
            _logger.Write(exception.ToString());
            SetStatus("İşlem başarısız.", 0);
            ShowError(exception);
        }
        finally
        {
            actionButton.IsEnabled = true;
            _operationCancellation.Dispose();
            _operationCancellation = null;
            RefreshLogs();
        }
    }

    private void LoadBundledClient()
    {
        string bundledClient = Path.Combine(AppContext.BaseDirectory, "UCREW_SecurePatch.exe");
        if (File.Exists(bundledClient))
        {
            ClientExePathBox.Text = bundledClient;
            _logger.Write("Güncel güvenli yama istemcisi Studio paketinden otomatik seçildi.");
        }
    }

    private string ResolveClientExePath()
    {
        string selected = ClientExePathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return Path.GetFullPath(selected);
        }

        return Path.Combine(AppContext.BaseDirectory, "UCREW_SecurePatch.exe");
    }

    private void LoadSettingsToUi()
    {
        HostBox.Text = _settings.Host;
        PortBox.Text = _settings.Port.ToString();
        SshUserBox.Text = _settings.SshUser;
        DatabaseBox.Text = _settings.DatabaseName;
        DatabaseUserBox.Text = _settings.DatabaseUser;
        SshPasswordBox.Password = _settingsService.UnprotectSecret(_settings.EncryptedSshPassword);
        DatabasePasswordBox.Password = _settingsService.UnprotectSecret(_settings.EncryptedDatabasePassword);
        BuildProfilePathBox.Text = _settings.LastProfilePath;
        BuildSourcePathBox.Text = _settings.LastSourcePath;
        BuildOutputPathBox.Text = _settings.LastOutputPath;
        PublishMetadataPathBox.Text = _settings.LastMetadataPath;
    }

    private void ReadSettingsFromUi()
    {
        _settings.Host = HostBox.Text.Trim();
        _settings.Port = int.TryParse(PortBox.Text.Trim(), out int port) ? port : 22;
        _settings.SshUser = SshUserBox.Text.Trim();
        _settings.DatabaseName = DatabaseBox.Text.Trim();
        _settings.DatabaseUser = DatabaseUserBox.Text.Trim();

        if (!string.IsNullOrEmpty(SshPasswordBox.Password))
            _settings.EncryptedSshPassword = _settingsService.ProtectSecret(SshPasswordBox.Password);
        if (!string.IsNullOrEmpty(DatabasePasswordBox.Password))
            _settings.EncryptedDatabasePassword = _settingsService.ProtectSecret(DatabasePasswordBox.Password);

        StudioValidation.ValidateServerSettings(_settings);
        UpdatePublishSummary();
    }

    private void LoadDefaultProfile()
    {
        ApplyProfileToUi(new GameProfile
        {
            GameSlug = "guardians",
            GameName = "Marvel's Guardians of the Galaxy: The Telltale Series",
            GameExe = "Guardians.exe",
            InstallMode = "overlay_flat",
            TargetPath = "archives",
            AllowedExtensions = new[] { ".landb", ".font", ".fnt", ".dds", ".d3dtx" },
            CleanupOnExit = true,
            PreserveDirectoryTree = false,
            RequiresBootstrap = true,
            BootstrapType = "version_proxy",
            WaitForGameExit = true,
            BackupExistingFiles = true,
            Theme = new ProfileTheme
            {
                Logo = "ucrew-logo.png",
                Background = "guardians-background.jpg",
                Accent = "#59FF35"
            }
        });

        CurrentProfilePathText.Text = "Guardians örnek profili";
    }

    private GameProfile ReadProfileFromUi()
    {
        var profile = new GameProfile
        {
            GameSlug = ProfileSlugBox.Text.Trim(),
            GameName = ProfileNameBox.Text.Trim(),
            GameExe = ProfileExeBox.Text.Trim(),
            TargetPath = ProfileTargetBox.Text.Trim(),
            InstallMode = GetComboValue(ProfileInstallModeBox),
            AllowedExtensions = ProfileExtensionsBox.Text
                .Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
            CleanupOnExit = CleanupOnExitCheck.IsChecked == true,
            PreserveDirectoryTree = PreserveTreeCheck.IsChecked == true,
            BackupExistingFiles = BackupFilesCheck.IsChecked == true,
            RequiresBootstrap = RequiresBootstrapCheck.IsChecked == true,
            BootstrapType = GetComboValue(BootstrapTypeBox),
            WaitForGameExit = true,
            Theme = new ProfileTheme
            {
                Logo = ProfileLogoPathBox.Text.Trim(),
                Background = ProfileBackgroundPathBox.Text.Trim(),
                Accent = "#55ff00"
            }
        };

        StudioValidation.ValidateProfile(profile);
        return profile;
    }

    private void ApplyProfileToUi(GameProfile profile)
    {
        ProfileSlugBox.Text = profile.GameSlug;
        ProfileNameBox.Text = profile.GameName;
        ProfileExeBox.Text = profile.GameExe;
        ProfileTargetBox.Text = profile.TargetPath;
        ProfileExtensionsBox.Text = string.Join(Environment.NewLine, profile.AllowedExtensions);
        CleanupOnExitCheck.IsChecked = profile.CleanupOnExit;
        PreserveTreeCheck.IsChecked = profile.PreserveDirectoryTree;
        BackupFilesCheck.IsChecked = profile.BackupExistingFiles;
        RequiresBootstrapCheck.IsChecked = profile.RequiresBootstrap;
        ProfileLogoPathBox.Text = profile.Theme?.Logo ?? string.Empty;
        ProfileBackgroundPathBox.Text = profile.Theme?.Background ?? string.Empty;
        SetComboValue(ProfileInstallModeBox, profile.InstallMode);
        SetComboValue(BootstrapTypeBox, profile.BootstrapType);
    }

    private static string GetComboValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? string.Empty
            : comboBox.Text.Trim();
    }

    private static void SetComboValue(ComboBox comboBox, string value)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(
                    comboItem.Content?.ToString(),
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    private void UpdatePublishSummary()
    {
        PublishServerSummaryText.Text =
            $"{HostBox.Text.Trim()}:{PortBox.Text.Trim()} • " +
            $"SSH: {SshUserBox.Text.Trim()} • " +
            $"MySQL: {DatabaseUserBox.Text.Trim()}@{DatabaseBox.Text.Trim()}";
    }

    private void Logger_MessageWritten(string line)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainTabs.SelectedIndex == 5)
            {
                RefreshLogs();
            }
        });
    }

    private void RefreshLogs()
    {
        LogTextBox.Text = _logger.ReadAll();
        LogTextBox.ScrollToEnd();
    }

    private void SetStatus(string message, int progress)
    {
        GlobalStatusText.Text = message;
        GlobalProgressBar.Value = Math.Clamp(progress, 0, 100);
    }

    private void ShowError(Exception exception)
    {
        MessageBox.Show(
            exception.Message,
            "U-CREW Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
