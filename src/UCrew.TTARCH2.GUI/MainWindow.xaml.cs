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
    private readonly string _patchRoot;

    private List<ArchiveFileRow> _allFiles = new();
    private string? _archivePath;
    private string? _extractionRoot;
    private bool _isBusy;

    public MainWindow()
    {
        _patchRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UCrewTTARCH2",
            "PatchProjects",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_patchRoot);

        InitializeComponent();
        Closed += (_, _) => TryDeleteDirectory(_patchRoot);
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
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));

            _archivePath = Path.GetFullPath(dialog.FileName);
            _extractionRoot = result.WorkingDirectory;
            _allFiles = BuildIndexedRows(result.Resources, result.StandardOutput);

            foreach (ArchiveFileRow row in _allFiles)
            {
                if (_modifiedFiles.Contains(NormalizePath(row.RelativePath)))
                    row.State = "Yamada";
            }

            ApplyFilter();
            UpdateArchiveInfo();

            string warning = result.Warnings.Count > 0
                ? $" Uyarı: {result.Warnings[0]}"
                : string.Empty;

            StatusText.Text =
                $"Arşiv açıldı. Index: 0–{Math.Max(0, _allFiles.Count - 1):N0}. " +
                $"Tek yama projesindeki dosya: {_modifiedFiles.Count:N0}.{warning}";
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
            selected.State = _modifiedFiles.Contains(NormalizePath(selected.RelativePath))
                ? "Yamada"
                : "TXT çıkarıldı";
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
            SetBusy(true, "Çevrilmiş TXT LANDb dosyasına aktarılıyor ve tek yama projesine ekleniyor...");
            int count = await _landbText
                .ImportAsync(backupPath, dialog.FileName, selected.ExtractedPath)
                .ConfigureAwait(true);

            StageFile(selected.ExtractedPath, selected.RelativePath);

            selected.Size = new FileInfo(selected.ExtractedPath).Length;
            selected.State = "Yamada";
            FilesGrid.Items.Refresh();
            UpdateArchiveInfo();

            StatusText.Text =
                $"{count:N0} çeviri kaydı içe aktarıldı. Tek yamadaki dosya: {_modifiedFiles.Count:N0}.";
        }
        catch (Exception ex)
        {
            File.Copy(backupPath, selected.ExtractedPath, overwrite: true);
            RemoveStagedFile(selected.RelativePath);
            selected.Size = new FileInfo(selected.ExtractedPath).Length;
            selected.State = "Orijinal";
            FilesGrid.Items.Refresh();
            UpdateArchiveInfo();

            MessageBox.Show(this, ex.Message, "TXT İçe Aktarma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "TXT içe aktarılamadı; çalışma dosyası orijinale döndürüldü.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void AddResource_Click(object sender, RoutedEventArgs e)
    {
        if (_archivePath is null || _allFiles.Count == 0)
            return;

        ArchiveFileRow? selectedRow = FilesGrid.SelectedItem as ArchiveFileRow;

        OpenFileDialog dialog = new()
        {
            Title = "Düzenlenmiş font veya kaynak dosyalarını seç",
            Filter =
                "Font ve oyun kaynakları (*.font;*.fnt;*.dds;*.d3dtx;*.landb)|*.font;*.fnt;*.dds;*.d3dtx;*.landb|" +
                "Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Font ve değiştirilmiş kaynak dosyaları tek yama projesine ekleniyor...");

            int added = 0;
            List<string> skipped = new();

            foreach (string sourcePath in dialog.FileNames)
            {
                ArchiveFileRow? target = FindTargetRow(sourcePath, selectedRow, dialog.FileNames.Length == 1);
                if (target is null)
                {
                    skipped.Add(Path.GetFileName(sourcePath));
                    continue;
                }

                StageFile(sourcePath, target.RelativePath);
                target.Size = new FileInfo(sourcePath).Length;
                target.State = "Yamada";
                added++;
            }

            FilesGrid.Items.Refresh();
            UpdateArchiveInfo();

            string skippedText = skipped.Count > 0
                ? Environment.NewLine + Environment.NewLine +
                  "Arşivde aynı adı bulunamadığı için eklenmeyenler:" + Environment.NewLine +
                  string.Join(Environment.NewLine, skipped.Take(20))
                : string.Empty;

            MessageBox.Show(
                this,
                $"Yamaya eklenen font/kaynak: {added:N0}\n" +
                $"Tek yamadaki toplam dosya: {_modifiedFiles.Count:N0}" + skippedText,
                "Font/Dosya Ekleme",
                MessageBoxButton.OK,
                skipped.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

            StatusText.Text =
                $"{added:N0} font/kaynak yamaya eklendi. Toplam: {_modifiedFiles.Count:N0}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Font/Dosya Ekleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Font veya kaynak dosyası yamaya eklenemedi.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void BuildPatch_Click(object sender, RoutedEventArgs e)
    {
        if (_modifiedFiles.Count == 0)
        {
            MessageBox.Show(
                this,
                "Önce en az bir LANDb, font veya değiştirilmiş kaynak dosyasını yamaya eklemelisin.",
                "Yama Boş",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Tek Guardians yama dosyasını kaydet",
            Filter = "TTARCH yaması (*.ttarch)|*.ttarch",
            FileName = "0.ttarch",
            AddExtension = true,
            DefaultExt = ".ttarch"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Yalnızca değiştirilmiş LANDb ve fontları içeren tek 0.ttarch oluşturuluyor...");

            TtarchextBackendResult result = await _backend
                .BuildGuardiansPatchArchiveAsync(_patchRoot, dialog.FileName)
                .ConfigureAwait(true);

            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));

            FileInfo output = new(dialog.FileName);
            string[] stagedFiles = Directory.GetFiles(_patchRoot, "*", SearchOption.AllDirectories);
            int landbCount = stagedFiles.Count(path => Path.GetExtension(path).Equals(".landb", StringComparison.OrdinalIgnoreCase));
            int fontCount = stagedFiles.Count(IsFontResource);

            StatusText.Text = $"Tek yama oluşturuldu: {dialog.FileName}";

            MessageBox.Show(
                this,
                "Tek yama dosyası hazır.\n\n" +
                $"Toplam kaynak: {stagedFiles.Length:N0}\n" +
                $"LANDb: {landbCount:N0}\n" +
                $"Font kaynağı: {fontCount:N0}\n" +
                $"Boyut: {output.Length:N0} bayt\n\n" +
                "0.ttarch dosyasını oyunun archives klasörüne koy. " +
                "Eski 0.ttarch varsa önce yedekle.",
                "0.ttarch Hazır",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "0.ttarch Oluşturma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Tek 0.ttarch yaması oluşturulamadı.";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private ArchiveFileRow? FindTargetRow(string sourcePath, ArchiveFileRow? selectedRow, bool singleFile)
    {
        string sourceName = Path.GetFileName(sourcePath);

        if (singleFile && selectedRow is not null)
            return selectedRow;

        List<ArchiveFileRow> exactMatches = _allFiles
            .Where(row => Path.GetFileName(row.RelativePath)
                .Equals(sourceName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exactMatches.Count == 1 ? exactMatches[0] : null;
    }

    private void StageFile(string sourcePath, string relativePath)
    {
        string normalized = NormalizePath(relativePath);
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part == ".."))
            throw new InvalidDataException($"Geçersiz arşiv yolu: {relativePath}");

        string localRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
        string destination = Path.GetFullPath(Path.Combine(_patchRoot, localRelative));
        string rootPrefix = Path.GetFullPath(_patchRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Yama klasörü dışına çıkan geçersiz yol: {relativePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourcePath, destination, overwrite: true);
        _modifiedFiles.Add(normalized);
    }

    private void RemoveStagedFile(string relativePath)
    {
        string normalized = NormalizePath(relativePath);
        string destination = Path.Combine(_patchRoot, normalized.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(destination))
            File.Delete(destination);

        _modifiedFiles.Remove(normalized);
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

    private static bool IsFontResource(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".font", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fnt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".d3dtx", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateArchiveInfo()
    {
        if (_archivePath is null)
        {
            ArchiveInfoText.Text = $"Arşiv açılmadı\nYamadaki dosya: {_modifiedFiles.Count:N0}";
            return;
        }

        ArchiveInfoText.Text =
            $"{Path.GetFileName(_archivePath)}\n" +
            $"{_allFiles.Count:N0} dosya • {_allFiles.Count(file => file.IsLandb):N0} LANDb • Yamadaki: {_modifiedFiles.Count:N0}";
    }

    private void ResetArchiveState()
    {
        _archivePath = null;
        _extractionRoot = null;
        _allFiles.Clear();
        FilesGrid.ItemsSource = null;
        UpdateArchiveInfo();
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
        if (ExportTxtButton is null || ImportTxtButton is null || AddResourceButton is null || BuildPatchButton is null)
            return;

        bool selectedLandb = FilesGrid?.SelectedItem is ArchiveFileRow row && row.IsLandb;
        ExportTxtButton.IsEnabled = !_isBusy && selectedLandb;
        ImportTxtButton.IsEnabled = !_isBusy && selectedLandb;
        AddResourceButton.IsEnabled = !_isBusy && _archivePath is not null && _allFiles.Count > 0;
        BuildPatchButton.IsEnabled = !_isBusy && _modifiedFiles.Count > 0;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary project cleanup failure must not block application shutdown.
        }
    }
}
