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
    private readonly ChunkTypeDetector _chunkTypeDetector = new();
    private readonly TextPreviewService _textPreviewService = new();
    private ArchiveModel? _currentArchive;
    private ChunkTypeInfo? _selectedChunkType;

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

        if (dialog.ShowDialog(this) == true)
            await AnalyzeFileAsync(dialog.FileName);
    }

    private async Task AnalyzeFileAsync(string filePath)
    {
        try
        {
            SetBusy(true, "Dosya analiz ediliyor...");
            ArchiveModel archive = await _analysisService.AnalyzeAsync(filePath);
            _currentArchive = archive;
            _selectedChunkType = null;
            DisplayArchive(archive);
            StatusText.Text = $"Analiz tamamlandı: {archive.Chunks.Count:N0} chunk bulundu.";
        }
        catch (Exception ex)
        {
            _currentArchive = null;
            _selectedChunkType = null;
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
        if (_currentArchive is null || _currentArchive.Chunks.Count == 0)
        {
            MessageBox.Show(this, "Çıkarılabilecek chunk bulunamadı.", "Chunk Çıkarma", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFolderDialog dialog = new() { Title = "Chunkların çıkarılacağı klasörü seç", Multiselect = false };
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

    private async void ExportSelectedChunk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null || ChunksGrid.SelectedItem is not ChunkModel chunk)
        {
            MessageBox.Show(this, "Önce bir chunk seçmelisin.", "Chunk Kaydet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedChunkType ??= await _chunkTypeDetector.DetectAsync(_currentArchive, chunk);
        SaveFileDialog dialog = new()
        {
            Title = "Seçili chunkı kaydet",
            Filter = $"{_selectedChunkType.Name} (*{_selectedChunkType.Extension})|*{_selectedChunkType.Extension}|Tüm dosyalar (*.*)|*.*",
            FileName = $"chunk_{chunk.Index:D4}_0x{chunk.Offset:X}{_selectedChunkType.Extension}",
            AddExtension = true,
            DefaultExt = _selectedChunkType.Extension
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Seçili chunk kaydediliyor...");
            await new SingleChunkExportService().ExportAsync(_currentArchive, chunk, dialog.FileName);
            StatusText.Text = $"Chunk kaydedildi: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Seçili chunk kaydedilemedi.";
            MessageBox.Show(this, ex.Message, "Chunk Kaydetme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void SaveEditedText_Click(object sender, RoutedEventArgs e)
    {
        if (!TextEditor.IsEnabled || ChunksGrid.SelectedItem is not ChunkModel chunk)
        {
            MessageBox.Show(this, "Önce metin olarak algılanan bir chunk seçmelisin.", "Metin Kaydet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Düzenlenmiş metni ayrı dosya olarak kaydet",
            Filter = "UTF-8 metin (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*",
            FileName = $"chunk_{chunk.Index:D4}_edited.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Düzenlenmiş metin kaydediliyor...");
            await _textPreviewService.SaveEditedTextAsync(TextEditor.Text, dialog.FileName);
            StatusText.Text = $"Metin kaydedildi: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Metin kaydedilemedi.";
            MessageBox.Show(this, ex.Message, "Metin Kaydetme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ClearChunkPreview();
            return;
        }

        try
        {
            StatusText.Text = $"Chunk {chunk.Index} analiz ediliyor...";
            Task<string> previewTask = _hexPreviewService.CreateChunkPreviewAsync(_currentArchive, chunk);
            Task<ChunkTypeInfo> typeTask = _chunkTypeDetector.DetectAsync(_currentArchive, chunk);
            await Task.WhenAll(previewTask, typeTask);

            _selectedChunkType = typeTask.Result;
            HexPreviewText.Text = previewTask.Result;
            SelectedChunkTypeText.Text = $"{_selectedChunkType.Name} / {_selectedChunkType.Category} / güven {_selectedChunkType.Confidence:0.00}";

            bool isText = string.Equals(_selectedChunkType.Category, "Text", StringComparison.OrdinalIgnoreCase);
            TextEditor.IsEnabled = isText;
            TextEditor.Text = isText
                ? await _textPreviewService.ReadChunkTextAsync(_currentArchive, chunk)
                : string.Empty;
            TextPreviewInfo.Text = isText
                ? "Metin UTF-8 olarak açıldı. Kaydetme işlemi arşivi değiştirmez; ayrı dosya oluşturur."
                : "Seçili chunk metin olarak algılanmadı.";

            StatusText.Text = $"Chunk {chunk.Index} önizlemesi hazır.";
        }
        catch (Exception ex)
        {
            ClearChunkPreview();
            HexPreviewText.Text = $"Önizleme hatası: {ex.Message}";
            StatusText.Text = "Chunk önizleme başarısız.";
        }
    }

    private void DisplayArchive(ArchiveModel archive)
    {
        FileNameText.Text = archive.FileName;
        FileSizeText.Text = $"{archive.FileSize:N0} bayt";
        MagicText.Text = archive.Header.Magic;
        EcttText.Text = archive.Ectt.LooksLikeEctt ? $"Evet (Güven: {archive.Ectt.Confidence:0.00})" : "Hayır";
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
        ClearChunkPreview();
    }

    private void ClearChunkPreview()
    {
        _selectedChunkType = null;
        SelectedChunkTypeText.Text = "-";
        HexPreviewText.Clear();
        TextEditor.Clear();
        TextEditor.IsEnabled = false;
        TextPreviewInfo.Text = "Metin türünde bir chunk seçilmedi.";
    }

    private void SetBusy(bool isBusy, string message)
    {
        IsEnabled = !isBusy;
        StatusText.Text = message;
        System.Windows.Input.Mouse.OverrideCursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
