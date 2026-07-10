using System.Windows;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Analysis;
using UCrew.TTARCH2.Core.Extraction;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Preview;
using UCrew.TTARCH2.Core.Reporting;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow : Window
{
    private readonly ArchiveAnalysisService _analysisService = new();
    private readonly HexPreviewService _hexPreviewService = new();
    private ArchiveModel? _currentArchive;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "TTARCH2 arşivi aç",
            Filter = "TTARCH2 dosyaları (*.ttarch2)|*.ttarch2|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await AnalyzeFileAsync(dialog.FileName);
    }

    private async Task AnalyzeFileAsync(string filePath)
    {
        try
        {
            SetBusy(true, "Dosya analiz ediliyor...");

            ArchiveModel archive = await _analysisService.AnalyzeAsync(filePath);
            _currentArchive = archive;
            DisplayArchive(archive);

            StatusText.Text = $"Analiz tamamlandı: {archive.Chunks.Count:N0} chunk bulundu.";
        }
        catch (Exception ex)
        {
            _currentArchive = null;
            StatusText.Text = "Analiz başarısız.";
            MessageBox.Show(this, ex.Message, "Analiz Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null)
        {
            MessageBox.Show(this, "Önce bir arşiv açmalısın.", "Rapor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "JSON analiz raporunu kaydet",
            Filter = "JSON dosyası (*.json)|*.json",
            FileName = Path.GetFileNameWithoutExtension(_currentArchive.FileName) + ".analysis.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "JSON raporu kaydediliyor...");
            await new JsonReportWriter().WriteAsync(_currentArchive, dialog.FileName);
            StatusText.Text = $"Rapor kaydedildi: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Rapor kaydedilemedi.";
            MessageBox.Show(this, ex.Message, "Rapor Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void DumpChunks_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null)
        {
            MessageBox.Show(this, "Önce bir arşiv açmalısın.", "Chunk Çıkarma", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_currentArchive.Chunks.Count == 0)
        {
            MessageBox.Show(this, "Çıkarılabilecek chunk bulunamadı.", "Chunk Çıkarma", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "Chunkların çıkarılacağı klasörü seç",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Chunklar dışarı çıkarılıyor...");
            int count = await new ChunkDumpService().DumpAsync(_currentArchive, dialog.FolderName);
            StatusText.Text = $"{count:N0} chunk dışarı çıkarıldı.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Chunk çıkarma başarısız.";
            MessageBox.Show(this, ex.Message, "Chunk Çıkarma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void ChunksGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_currentArchive is null || ChunksGrid.SelectedItem is not ChunkModel chunk)
        {
            HexPreviewText.Clear();
            return;
        }

        try
        {
            StatusText.Text = $"Chunk {chunk.Index} önizleniyor...";
            HexPreviewText.Text = await _hexPreviewService.CreateChunkPreviewAsync(_currentArchive, chunk);
            StatusText.Text = $"Chunk {chunk.Index} önizlemesi hazır.";
        }
        catch (Exception ex)
        {
            HexPreviewText.Text = $"Önizleme hatası: {ex.Message}";
            StatusText.Text = "Chunk önizleme başarısız.";
        }
    }

    private void DisplayArchive(ArchiveModel archive)
    {
        FileNameText.Text = archive.FileName;
        FileSizeText.Text = $"{archive.FileSize:N0} bayt";
        MagicText.Text = archive.Header.Magic;
        EcttText.Text = archive.Ectt.LooksLikeEctt
            ? $"Evet (Güven: {archive.Ectt.Confidence:0.00})"
            : "Hayır";
        ChunkCountText.Text = archive.Chunks.Count.ToString("N0");
        SignatureCountText.Text = archive.Signatures.Count.ToString("N0");

        Field0004Text.Text = $"0x{archive.Header.Field0004:X8}";
        Field0008Text.Text = $"0x{archive.Header.Field0008:X8}";
        Field000CText.Text = $"0x{archive.Header.Field000C:X8}";
        OffsetTableText.Text = archive.Ectt.OffsetEntryCount > 0
            ? $"0x{archive.Ectt.OffsetTableStart:X} - 0x{archive.Ectt.OffsetTableEnd:X} ({archive.Ectt.OffsetEntryCount:N0} giriş)"
            : "Bulunamadı";

        ChunksGrid.ItemsSource = archive.Chunks;
        SignaturesGrid.ItemsSource = archive.Signatures;
        HexPreviewText.Clear();
    }

    private void SetBusy(bool isBusy, string message)
    {
        IsEnabled = !isBusy;
        StatusText.Text = message;
        System.Windows.Input.Mouse.OverrideCursor = isBusy
            ? System.Windows.Input.Cursors.Wait
            : null;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
