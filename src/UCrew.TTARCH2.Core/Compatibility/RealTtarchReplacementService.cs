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
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resource);

        ArchiveVariableSizeReplacementResult result = new()
        {
            OutputPath = Path.GetFullPath(outputArchivePath),
            OriginalFileSize = archive.FileSize
        };

        if (string.IsNullOrWhiteSpace(resource.RelativePath))
        {
            result.Errors.Add("Kaynağın arşiv içi yolu bilinmiyor.");
            return result;
        }

        if (!File.Exists(replacementPath))
        {
            result.Errors.Add("Yeni kaynak dosyası bulunamadı.");
            return result;
        }

        string patchRoot = Path.Combine(
            Path.GetTempPath(),
            "ucrew-ttarch-patch",
            Guid.NewGuid().ToString("N"));

        try
        {
            string relativePath = NormalizeRelativePath(resource.RelativePath);
            if (relativePath.Length == 0 || relativePath.Split('/').Any(part => part == ".."))
            {
                result.Errors.Add("Kaynağın arşiv içi yolu geçersiz.");
                return result;
            }

            string destination = Path.Combine(
                patchRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(replacementPath, destination, overwrite: true);

            TtarchextBackendResult backend = await new TtarchextBackendService()
                .BuildGuardiansPatchArchiveAsync(
                    patchRoot,
                    outputArchivePath,
                    token)
                .ConfigureAwait(false);

            result.Errors.AddRange(backend.Errors);
            result.Warnings.AddRange(backend.Warnings);

            if (backend.Success && File.Exists(outputArchivePath))
            {
                result.OutputFileSize = new FileInfo(outputArchivePath).Length;
                result.Warnings.Add(
                    "Çıktı yalnızca değiştirilmiş kaynağı içeren küçük bir yama arşividir. " +
                    "Guardians için 0.ttarch adıyla kullanılmalıdır.");
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(patchRoot))
                    Directory.Delete(patchRoot, recursive: true);
            }
            catch (Exception cleanupError)
            {
                result.Warnings.Add($"Geçici yama klasörü silinemedi: {cleanupError.Message}");
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
                result.Warnings.Add("Başarısız yama çıktısı otomatik silinemedi.");
            }
        }

        return result;
    }

    private static string NormalizeRelativePath(string value) =>
        value.Replace('\\', '/').TrimStart('.', '/');
}
