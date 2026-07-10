using System.Text;
using System.Windows;
using Microsoft.Win32;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow
{
    private async void RebuildLandbVariable_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is null || _currentArchive.Landb.TextCandidates.Count == 0)
        {
            MessageBox.Show(
                this,
                "Önce metin adayları bulunan bir LANDb dosyası açmalısın.",
                "LANDb Rebuild",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenFileDialog textDialog = new()
        {
            Title = "Çevrilmiş temiz TXT dosyasını seç",
            Filter = "UTF-8 metin (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (textDialog.ShowDialog(this) != true)
            return;

        LandbRebuildPlan plan;

        try
        {
            SetBusy(true, "LANDb rebuild planı hazırlanıyor...");
            plan = await new LandbVariableLengthPlanner()
                .CreatePlanAsync(_currentArchive, textDialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText.Text = "LANDb rebuild planı oluşturulamadı.";
            MessageBox.Show(this, ex.Message, "LANDb Plan Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            SetBusy(false, StatusText.Text);
            return;
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }

        if (!plan.CanRebuild)
        {
            string errors = string.Join(Environment.NewLine, plan.Errors.Take(20));
            if (plan.Errors.Count > 20)
                errors += $"{Environment.NewLine}... ve {plan.Errors.Count - 20:N0} ek hata";

            MessageBox.Show(
                this,
                $"Rebuild güvenlik kontrolünden geçmedi. Dosya oluşturulmayacak.\n\n{errors}",
                "LANDb Rebuild Engellendi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = $"LANDb rebuild engellendi: {plan.Errors.Count:N0} hata.";
            return;
        }

        int changedCount = plan.Entries.Count(x => x.Delta != 0);
        string warningText = plan.Warnings.Count > 0
            ? Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, plan.Warnings.Take(5))
            : string.Empty;

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Dry-run başarılı.\n\n" +
            $"Metin sayısı: {plan.Entries.Count:N0}\n" +
            $"Boyutu değişen satır: {changedCount:N0}\n" +
            $"Eski dosya boyutu: {plan.OriginalFileSize:N0} bayt\n" +
            $"Yeni dosya boyutu: {plan.PlannedFileSize:N0} bayt\n" +
            $"Toplam fark: {plan.TotalDelta:+#,0;-#,0;0} bayt" + warningText +
            "\n\nYeni LANDb dosyası oluşturulsun mu?",
            "LANDb Sınırsız Rebuild",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        SaveFileDialog outputDialog = new()
        {
            Title = "Yeni LANDb dosyasını kaydet",
            Filter = "LANDb dosyası (*.landb)|*.landb|Tüm dosyalar (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(_currentArchive.FileName) + ".TR.landb",
            AddExtension = true,
            DefaultExt = ".landb"
        };

        if (outputDialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "LANDb dosyası sınırsız uzunlukla yeniden oluşturuluyor...");

            LandbVariableLengthRebuildResult result = await new LandbVariableLengthRebuildService()
                .RebuildAsync(_currentArchive, textDialog.FileName, outputDialog.FileName);

            if (!result.Success)
            {
                string errors = string.Join(Environment.NewLine, result.Errors.Take(20));
                MessageBox.Show(
                    this,
                    $"Rebuild başarısız oldu. Geçersiz çıktı korunmadı.\n\n{errors}",
                    "LANDb Rebuild Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusText.Text = "LANDb rebuild başarısız.";
                return;
            }

            StringBuilder summary = new();
            summary.AppendLine("LANDb rebuild tamamlandı.");
            summary.AppendLine();
            summary.AppendLine($"Çıktı: {result.OutputPath}");
            summary.AppendLine($"Dosya boyutu: {result.OutputFileSize:N0} bayt");
            summary.AppendLine($"Güncellenen pointer: {result.UpdatedPointerCount:N0}");
            summary.AppendLine($"Güncellenen uzunluk alanı: {result.UpdatedLengthFieldCount:N0}");
            summary.AppendLine();
            summary.Append("Orijinal LANDb değiştirilmedi.");

            MessageBox.Show(this, summary.ToString(), "LANDb Rebuild Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"LANDb rebuild tamamlandı: {result.OutputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "LANDb rebuild sırasında hata oluştu.";
            MessageBox.Show(this, ex.Message, "LANDb Rebuild Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }
}
