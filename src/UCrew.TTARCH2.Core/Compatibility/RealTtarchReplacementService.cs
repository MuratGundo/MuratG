using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Compatibility;

public sealed class RealTtarchReplacementService
{
    public async Task<ArchiveVariableSizeReplacementResult> ReplaceAndRebuildAsync(
        ArchiveModel archive,
        ArchiveResourceEntry resource,
        string replacementPath,
        string outputArchivePath,
        CancellationToken token = default)
    {
        ArchiveVariableSizeReplacementResult result = new()
        {
            OutputPath = Path.GetFullPath(outputArchivePath),
            OriginalFileSize = archive.FileSize
        };

        if (!resource.IsExtracted || string.IsNullOrWhiteSpace(resource.ExtractionRoot))
        {
            result.Errors.Add("Kaynak gerçek TTARCH2 çalışma klasöründen gelmiyor.");
            return result;
        }

        if (!File.Exists(replacementPath))
        {
            result.Errors.Add("Yeni LANDb dosyası bulunamadı.");
            return result;
        }

        string targetPath = resource.ExtractedPath;
        string backupPath = targetPath + ".ucrew_backup";

        try
        {
            File.Copy(targetPath, backupPath, overwrite: true);
            File.Copy(replacementPath, targetPath, overwrite: true);

            TtarchextBackendResult backend = await new TtarchextBackendService()
                .RebuildGuardiansArchiveAsync(
                    resource.ExtractionRoot,
                    outputArchivePath,
                    archive.FullPath,
                    token)
                .ConfigureAwait(false);

            result.Errors.AddRange(backend.Errors);
            result.Warnings.AddRange(backend.Warnings);

            if (backend.Success && File.Exists(outputArchivePath))
                result.OutputFileSize = new FileInfo(outputArchivePath).Length;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, targetPath, overwrite: true);
                    File.Delete(backupPath);
                }
            }
            catch (Exception restoreError)
            {
                result.Warnings.Add($"Çalışma dosyası geri yüklenemedi: {restoreError.Message}");
            }
        }

        if (!result.Success && File.Exists(outputArchivePath))
        {
            try
            {
                File.Delete(outputArchivePath);
            }
            catch
            {
                result.Warnings.Add("Başarısız TTARCH2 çıktısı otomatik silinemedi.");
            }
        }

        return result;
    }
}
