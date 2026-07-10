using System.Windows;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Analysis;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow : Window
{
    private readonly ArchiveAnalysisService _analysisService = new();

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
            DisplayArchive(archive);

            StatusText.Text = $"Analiz tamamlandı: {archive.Chunks.Count:N0} chunk bulundu.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analiz başarısız.";
            MessageBox.Show(this, ex.Message, "Analiz Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
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
