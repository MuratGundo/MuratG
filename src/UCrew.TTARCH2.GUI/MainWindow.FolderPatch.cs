using System.Windows;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Compatibility;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow
{
    private static readonly HashSet<string> FolderPatchExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".landb",
        ".font",
        ".fnt",
        ".dds",
        ".d3dtx"
    };

    private async void BuildFromFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog folderDialog = new()
        {
            Title = "LANDb ve font dosyalarının bulunduğu klasörü seç",
            Multiselect = false
        };

        if (folderDialog.ShowDialog(this) != true ||
            string.IsNullOrWhiteSpace(folderDialog.FolderName))
        {
            return;
        }

        string sourceFolder = Path.GetFullPath(folderDialog.FolderName);
        string[] sourceFiles;

        try
        {
            sourceFiles = Directory
                .GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
                .Where(IsSupportedFolderPatchFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Klasör Okuma Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (sourceFiles.Length == 0)
        {
            MessageBox.Show(
                this,
                "Seçilen klasörde desteklenen dosya bulunamadı.\n\n" +
                "Desteklenenler: .landb, .font, .fnt, .dds, .d3dtx",
                "Klasör Boş",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SaveFileDialog saveDialog = new()
        {
            Title = "Tek Guardians yama dosyasını kaydet",
            Filter = "TTARCH yaması (*.ttarch)|*.ttarch",
            FileName = "0.ttarch",
            AddExtension = true,
            DefaultExt = ".ttarch"
        };

        if (saveDialog.ShowDialog(this) != true)
            return;

        string temporaryPatchRoot = Path.Combine(
            Path.GetTempPath(),
            "ucrew-guardians-folder-patch",
            Guid.NewGuid().ToString("N"));

        try
        {
            BuildFolderPatchButton.IsEnabled = false;
            SetBusy(true, $"Klasördeki {sourceFiles.Length:N0} LANDb/font dosyasından tek 0.ttarch oluşturuluyor...");

            Directory.CreateDirectory(temporaryPatchRoot);

            foreach (string sourceFile in sourceFiles)
            {
                string relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
                string destination = Path.Combine(temporaryPatchRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourceFile, destination, overwrite: true);
            }

            TtarchextBackendResult result = await _backend
                .BuildGuardiansPatchArchiveAsync(temporaryPatchRoot, saveDialog.FileName)
                .ConfigureAwait(true);

            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));

            int landbCount = sourceFiles.Count(path =>
                Path.GetExtension(path).Equals(".landb", StringComparison.OrdinalIgnoreCase));
            int fontCount = sourceFiles.Length - landbCount;
            long outputSize = new FileInfo(saveDialog.FileName).Length;

            StatusText.Text = $"Klasörden tek yama oluşturuldu: {saveDialog.FileName}";

            MessageBox.Show(
                this,
                "Tek 0.ttarch hazır.\n\n" +
                $"Kaynak klasör: {sourceFolder}\n" +
                $"Toplam dosya: {sourceFiles.Length:N0}\n" +
                $"LANDb: {landbCount:N0}\n" +
                $"Font/kaynak: {fontCount:N0}\n" +
                $"Çıktı boyutu: {outputSize:N0} bayt\n\n" +
                "TXT, BAK ve desteklenmeyen dosyalar pakete alınmadı.",
                "0.ttarch Hazır",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Klasörden 0.ttarch Oluşturma Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Klasörden tek 0.ttarch oluşturulamadı.";
        }
        finally
        {
            TryDeleteDirectory(temporaryPatchRoot);
            SetBusy(false, StatusText.Text);
            BuildFolderPatchButton.IsEnabled = true;
        }
    }

    private static bool IsSupportedFolderPatchFile(string path)
    {
        string fileName = Path.GetFileName(path);
        if (fileName.StartsWith(".", StringComparison.Ordinal))
            return false;

        return FolderPatchExtensions.Contains(Path.GetExtension(path));
    }
}
