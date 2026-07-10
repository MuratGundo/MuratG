using System.Windows;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Extraction;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow
{
    private void RefreshResourceCatalog()
    {
        if (_currentArchive is null)
        {
            ResourcesGrid.ItemsSource = null;
            ResourceSummaryText.Text = "Kaynak kataloğu yok.";
            return;
        }

        ResourcesGrid.ItemsSource = _currentArchive.Resources;
        int landbCount = _currentArchive.Resources.Count(x => x.IsLandb);
        ResourceSummaryText.Text = $"Toplam {_currentArchive.Resources.Count:N0} kaynak | LANDb adayı {landbCount:N0}";
    }

    private async void ExtractSelectedResource_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null || ResourcesGrid.SelectedItem is not ArchiveResourceEntry resource)
        {
            MessageBox.Show(this, "Önce bir kaynak seçmelisin.", "Kaynak Çıkarma", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Seçili kaynağı dışarı çıkar",
            Filter = resource.IsLandb
                ? "LANDb dosyası (*.landb)|*.landb|Tüm dosyalar (*.*)|*.*"
                : "Tüm dosyalar (*.*)|*.*",
            FileName = resource.Name,
            AddExtension = true,
            DefaultExt = resource.Extension
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Seçili kaynak çıkarılıyor...");
            await new ArchiveResourceExtractService().ExtractAsync(_currentArchive, resource, dialog.FileName);
            StatusText.Text = $"Kaynak çıkarıldı: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kaynak çıkarılamadı.";
            MessageBox.Show(this, ex.Message, "Kaynak Çıkarma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void ReplaceSelectedResource_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null || ResourcesGrid.SelectedItem is not ArchiveResourceEntry resource)
        {
            MessageBox.Show(this, "Önce değiştirilecek LANDb kaynağını seçmelisin.", "LANDb Değiştir", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFileDialog replacementDialog = new()
        {
            Title = "Yeni LANDb dosyasını seç",
            Filter = "LANDb dosyası (*.landb)|*.landb|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (replacementDialog.ShowDialog(this) != true)
            return;

        SaveFileDialog outputDialog = new()
        {
            Title = "Yeni TTARCH2 kopyasını kaydet",
            Filter = "TTARCH2 dosyası (*.ttarch2)|*.ttarch2|Tüm dosyalar (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(_currentArchive.FileName) + ".TR.ttarch2",
            AddExtension = true,
            DefaultExt = ".ttarch2"
        };

        if (outputDialog.ShowDialog(this) != true)
            return;

        long replacementSize = new FileInfo(replacementDialog.FileName).Length;
        long delta = replacementSize - resource.Size;

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Seçili LANDb yeni TTARCH2 kopyasına aktarılacak.\n\n" +
            $"Eski boyut: {resource.Size:N0} bayt\n" +
            $"Yeni boyut: {replacementSize:N0} bayt\n" +
            $"Fark: {delta:+#,0;-#,0;0} bayt\n\n" +
            "Orijinal TTARCH2 değiştirilmeyecek. Devam edilsin mi?",
            "LANDb İçe Aktarma",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, "LANDb içe aktarılıyor ve TTARCH2 yeniden oluşturuluyor...");

            ArchiveVariableSizeReplacementResult result = await new ArchiveVariableSizeReplacementService()
                .ReplaceToCopyAsync(
                    _currentArchive,
                    resource,
                    replacementDialog.FileName,
                    outputDialog.FileName);

            if (!result.Success)
            {
                string errors = string.Join(Environment.NewLine, result.Errors.Take(20));
                MessageBox.Show(
                    this,
                    $"TTARCH2 oluşturulamadı.\n\n{errors}",
                    "TTARCH2 Rebuild Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusText.Text = "TTARCH2 rebuild başarısız.";
                return;
            }

            MessageBox.Show(
                this,
                $"Yeni TTARCH2 oluşturuldu.\n\n" +
                $"Çıktı: {result.OutputPath}\n" +
                $"Boyut: {result.OutputFileSize:N0} bayt\n" +
                $"Fark: {result.SizeDelta:+#,0;-#,0;0} bayt\n" +
                $"Güncellenen pointer: {result.UpdatedPointerCount:N0}\n\n" +
                "Orijinal TTARCH2 değiştirilmedi.",
                "TTARCH2 Rebuild Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text = $"Yeni TTARCH2 oluşturuldu: {result.OutputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "TTARCH2 rebuild sırasında hata oluştu.";
            MessageBox.Show(this, ex.Message, "TTARCH2 Rebuild Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }
}
