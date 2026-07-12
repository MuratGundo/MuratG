using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Compatibility;
using UCrew.TTARCH2.Core.Localization;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow : Window
{
    private readonly TtarchextBackendService _backend = new();
    private readonly LandbCleanTextService _landbText = new();
    private readonly HashSet<string> _modifiedFiles = new(StringComparer.OrdinalIgnoreCase);

    private List<ArchiveFileRow> _allFiles = new();
    private string? _archivePath;
    private string? _extractionRoot;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        UpdateButtonState();
    }

    private async void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Guardians TTARCH2 arşivini seç",
            Filter = "TTARCH2 dosyası (*.ttarch2)|*.ttarch2|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "TTARCH2 açılıyor ve gerçek dosya listesi çıkarılıyor...");

            TtarchextBackendResult result = await _backend
                .ExtractGuardiansArchiveAsync(dialog.FileName)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
            }

            _archivePath = Path.GetFullPath(dialog.FileName);
            _extractionRoot = result.WorkingDirectory;
            _modifiedFiles.Clear();
            _allFiles = BuildIndexedRows(result.Resources, result.StandardOutput);

            ApplyFilter();
            ArchiveInfoText.Text =
                $"{Path.GetFileName(_archivePath)}\n" +
                $"{_allFiles.Count:N0} dosya • {_allFiles.Count(file => file.IsLandb):N0} LANDb";

            string warning = result.Warnings.Count > 0
                ? $" Uyarı: {result.Warnings[0]}"
                : string.Empty;

            StatusText.Text =
                $"Arşiv açıldı. Dosya indeksleri gerçek çıkarma sırasına göre 0–{Math.Max(0, _allFiles.Count - 1):N0} olarak düzeltildi.{warning}";
        }
        catch (Exception ex)
        {
            ResetArchiveState();
            MessageBox.Show(this, ex.Message, "TTARCH2 Açma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "TTARCH2 açılamadı.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void ExportTxt_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not ArchiveFileRow selected || !selected.IsLandb)
            return;

        SaveFileDialog dialog = new()
        {
            Title = "Temiz TXT dosyasını kaydet",
            Filter = "UTF-8 metin (*.txt)|*.txt",
            FileName = Path.GetFileNameWithoutExtension(selected.Name) + ".clean.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Seçili LANDb dosyasından temiz TXT çıkarılıyor...");
            int count = await _landbText.ExportAsync(selected.ExtractedPath, dialog.FileName).ConfigureAwait(true);
            selected.State = "TXT çıkarıldı";
            FilesGrid.Items.Refresh();
            StatusText.Text = $"{count:N0} temiz metin kaydı çıkarıldı: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TXT Çıkarma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Temiz TXT çıkarılamadı.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void ImportTxt_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not ArchiveFileRow selected || !selected.IsLandb || _extractionRoot is null)
            return;

        OpenFileDialog dialog = new()
        {
            Title = "Çevrilmiş temiz TXT dosyasını seç",
            Filter = "UTF-8 metin (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        string backupPath = GetBackupPath(selected);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

        if (!File.Exists(backupPath))
            File.Copy(selected.ExtractedPath, backupPath, overwrite: false);

        try
        {
            SetBusy(true, "Çevrilmiş TXT seçili LANDb dosyasına aktarılıyor...");
            int count = await _landbText
                .ImportAsync(backupPath, dialog.FileName, selected.ExtractedPath)
                .ConfigureAwait(true);

            selected.Size = new FileInfo(selected.ExtractedPath).Length;
            selected.State = "Değiştirildi";
            _modifiedFiles.Add(selected.RelativePath);
            FilesGrid.Items.Refresh();

            StatusText.Text =
                $"{count:N0} çeviri kaydı içe aktarıldı. Değiştirilen LANDb: {selected.Name}";
        }
        catch (Exception ex)
        {
            File.Copy(backupPath, selected.ExtractedPath, overwrite: true);
            selected.Size = new FileInfo(selected.ExtractedPath).Length;
            selected.State = "Orijinal";
            _modifiedFiles.Remove(selected.RelativePath);
            FilesGrid.Items.Refresh();

            MessageBox.Show(this, ex.Message, "TXT İçe Aktarma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "TXT içe aktarılamadı; çalışma dosyası orijinale döndürüldü.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void BuildArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_archivePath is null || _extractionRoot is null)
            return;

        if (_modifiedFiles.Count == 0)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                "Henüz değiştirilmiş LANDb yok. Yine de yeni TTARCH2 oluşturulsun mu?",
                "TTARCH2 Oluştur",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Yeni TTARCH2 dosyasını kaydet",
            Filter = "TTARCH2 dosyası (*.ttarch2)|*.ttarch2",
            FileName = Path.GetFileNameWithoutExtension(_archivePath) + ".TR.ttarch2",
            AddExtension = true,
            DefaultExt = ".ttarch2"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Oodle sıkıştırmalı yeni TTARCH2 oluşturuluyor...");

            TtarchextBackendResult result = await _backend
                .RebuildGuardiansArchiveAsync(_extractionRoot, dialog.FileName, _archivePath)
                .ConfigureAwait(true);

            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));

            long outputSize = new FileInfo(dialog.FileName).Length;
            StatusText.Text = $"Yeni TTARCH2 oluşturuldu: {dialog.FileName}";

            MessageBox.Show(
                this,
                $"Paketleme tamamlandı.\n\n" +
                $"Değiştirilen LANDb: {_modifiedFiles.Count:N0}\n" +
                $"Çıktı boyutu: {outputSize:N0} bayt\n\n" +
                $"{dialog.FileName}",
                "TTARCH2 Hazır",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TTARCH2 Paketleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Yeni TTARCH2 oluşturulamadı.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (FilesGrid is null)
            return;
        ApplyFilter();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (FilesGrid is null)
            return;
        ApplyFilter();
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonState();
    }

    private void ApplyFilter()
    {
        if (FilesGrid is null)
            return;

        string search = SearchTextBox?.Text?.Trim() ?? string.Empty;
        bool landbOnly = LandbOnlyCheckBox?.IsChecked == true;

        List<ArchiveFileRow> filtered = _allFiles
            .Where(file => !landbOnly || file.IsLandb)
            .Where(file => string.IsNullOrEmpty(search)
                || file.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || file.Index.ToString().Contains(search, StringComparison.Ordinal))
            .OrderBy(file => file.Index)
            .ToList();

        FilesGrid.ItemsSource = filtered;
        UpdateButtonState();
    }

    private static List<ArchiveFileRow> BuildIndexedRows(
        IReadOnlyCollection<ArchiveResourceEntry> resources,
        string standardOutput)
    {
        Dictionary<string, ArchiveResourceEntry> remaining = resources
            .GroupBy(resource => NormalizePath(resource.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<ArchiveResourceEntry> ordered = new(resources.Count);

        foreach (string rawLine in standardOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = NormalizePath(rawLine.Trim());
            if (line.Length == 0 || remaining.Count == 0)
                continue;

            string? matchedKey = remaining.Keys
                .Where(key => line.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(key => key.Length)
                .FirstOrDefault();

            if (matchedKey is null)
            {
                List<string> basenameMatches = remaining.Keys
                    .Where(key => line.EndsWith(Path.GetFileName(key), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (basenameMatches.Count == 1)
                    matchedKey = basenameMatches[0];
            }

            if (matchedKey is null)
                continue;

            ordered.Add(remaining[matchedKey]);
            remaining.Remove(matchedKey);
        }

        ordered.AddRange(remaining.Values.OrderBy(resource => resource.RelativePath, StringComparer.OrdinalIgnoreCase));

        return ordered.Select((resource, index) => new ArchiveFileRow
        {
            Index = index,
            Name = resource.RelativePath,
            RelativePath = resource.RelativePath,
            ExtractedPath = resource.ExtractedPath,
            Extension = resource.Extension,
            Size = resource.Size,
            State = "Orijinal"
        }).ToList();
    }

    private string GetBackupPath(ArchiveFileRow selected)
    {
        string workRoot = Path.GetDirectoryName(_extractionRoot!)!;
        return Path.Combine(workRoot, "backup", selected.RelativePath);
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/').TrimStart('.', '/');

    private void ResetArchiveState()
    {
        _archivePath = null;
        _extractionRoot = null;
        _allFiles.Clear();
        _modifiedFiles.Clear();
        FilesGrid.ItemsSource = null;
        ArchiveInfoText.Text = "Arşiv açılmadı";
        UpdateButtonState();
    }

    private void SetBusy(bool busy, string message)
    {
        _isBusy = busy;
        StatusText.Text = message;
        BusyProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        FilesGrid.IsEnabled = !busy;
        OpenArchiveButton.IsEnabled = !busy;
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        if (ExportTxtButton is null || ImportTxtButton is null || BuildArchiveButton is null)
            return;

        bool selectedLandb = FilesGrid?.SelectedItem is ArchiveFileRow row && row.IsLandb;
        ExportTxtButton.IsEnabled = !_isBusy && selectedLandb;
        ImportTxtButton.IsEnabled = !_isBusy && selectedLandb;
        BuildArchiveButton.IsEnabled = !_isBusy && _archivePath is not null && _extractionRoot is not null;
    }
}
