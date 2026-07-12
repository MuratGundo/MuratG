using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Compatibility;

public sealed class TtarchextBackendService
{
    // ttarchext 0.3.2 game table index for Marvel's Guardians of the Galaxy.
    private const string GuardiansGameNumber = "62";
    private const string GuardiansArchiveVersion = "7";

    public bool TryFindTool(out string toolPath)
    {
        List<string> candidates = new();

        string? configured = Environment.GetEnvironmentVariable("UCREW_TTARCHEXT");
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        string baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "ttarchext.exe"));
        candidates.Add(Path.Combine(baseDirectory, "Tools", "ttarchext.exe"));
        candidates.Add(Path.Combine(baseDirectory, "tools", "ttarchext.exe"));

        DirectoryInfo? current = new(baseDirectory);
        for (int i = 0; i < 6 && current is not null; i++, current = current.Parent)
        {
            candidates.Add(Path.Combine(current.FullName, "ttarchext.exe"));
            candidates.Add(Path.Combine(current.FullName, "Tools", "ttarchext.exe"));
            candidates.Add(Path.Combine(current.FullName, "tools", "ttarchext.exe"));
        }

        toolPath = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists) ?? string.Empty;

        return toolPath.Length > 0;
    }

    public async Task<TtarchextBackendResult> ExtractGuardiansArchiveAsync(
        string archivePath,
        CancellationToken token = default)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        string workingDirectory = GetWorkingDirectory(fullArchivePath);
        string extractionDirectory = Path.Combine(workingDirectory, "extracted");

        TtarchextBackendResult result = new()
        {
            WorkingDirectory = extractionDirectory
        };

        if (!TryFindTool(out string toolPath))
        {
            result.Errors.Add(
                "ttarchext.exe bulunamadı. Dosyayı uygulamanın yanına veya Tools klasörüne koy ya da UCREW_TTARCHEXT ortam değişkenini ayarla.");
            return result;
        }

        result = new TtarchextBackendResult
        {
            WorkingDirectory = extractionDirectory,
            ToolPath = toolPath
        };

        Directory.CreateDirectory(workingDirectory);

        if (Directory.Exists(extractionDirectory))
            Directory.Delete(extractionDirectory, recursive: true);
        Directory.CreateDirectory(extractionDirectory);

        string arguments = string.Join(' ',
            "-o",
            GuardiansGameNumber,
            Quote(fullArchivePath),
            Quote(extractionDirectory));

        ProcessRunResult process = await RunAsync(toolPath, arguments, Path.GetDirectoryName(toolPath)!, token)
            .ConfigureAwait(false);

        result.StandardOutput = process.StandardOutput;
        result.StandardError = process.StandardError;

        if (process.ExitCode != 0)
        {
            string combined = process.StandardError + Environment.NewLine + process.StandardOutput;
            string hint = combined.Contains("zlib/deflate", StringComparison.OrdinalIgnoreCase)
                ? Environment.NewLine + Environment.NewLine +
                  "Arşiv bu ttarchext sürümünün desteklemediği bir sıkıştırma kullanıyor olabilir veya dosya bozuk olabilir."
                : string.Empty;

            result.Errors.Add(
                $"ttarchext çıkarma işlemi başarısız oldu (çıkış kodu {process.ExitCode}).\n{combined}{hint}");
            return result;
        }

        string[] files = Directory.GetFiles(extractionDirectory, "*", SearchOption.AllDirectories);
        int index = 0;

        foreach (string file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            string relative = Path.GetRelativePath(extractionDirectory, file);

            result.Resources.Add(new ArchiveResourceEntry
            {
                Index = index++,
                Name = relative,
                RelativePath = relative,
                ExtractedPath = info.FullName,
                ExtractionRoot = extractionDirectory,
                Extension = info.Extension,
                Offset = -1,
                Size = info.Length,
                Confidence = 1.0,
                Source = "ttarchext 0.3.2 / Guardians game key 62"
            });
        }

        if (result.Resources.Count == 0)
            result.Errors.Add("ttarchext çalıştı ancak çıkarılmış kaynak bulunamadı.");

        return result;
    }

    public async Task<TtarchextBackendResult> BuildGuardiansPatchArchiveAsync(
        string patchDirectory,
        string outputArchivePath,
        CancellationToken token = default)
    {
        string fullPatchDirectory = Path.GetFullPath(patchDirectory);
        TtarchextBackendResult result = new()
        {
            WorkingDirectory = fullPatchDirectory
        };

        if (!Directory.Exists(fullPatchDirectory))
        {
            result.Errors.Add("Yama çalışma klasörü bulunamadı.");
            return result;
        }

        string[] patchFiles = Directory.GetFiles(fullPatchDirectory, "*", SearchOption.AllDirectories);
        if (patchFiles.Length == 0)
        {
            result.Errors.Add("Yamaya eklenmiş LANDb, font veya başka kaynak dosyası bulunamadı.");
            return result;
        }

        if (!TryFindTool(out string toolPath))
        {
            result.Errors.Add("ttarchext.exe bulunamadı.");
            return result;
        }

        result = new TtarchextBackendResult
        {
            WorkingDirectory = fullPatchDirectory,
            ToolPath = toolPath
        };

        string output = Path.GetFullPath(outputArchivePath);
        string outputDirectory = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("Çıktı klasörü belirlenemedi.");
        Directory.CreateDirectory(outputDirectory);

        if (File.Exists(output))
            File.Delete(output);

        // Some ttarchext 0.3.2 builds choose the archive family from the output
        // extension before processing -V. Building directly as 0.ttarch may therefore
        // leave an old-format/zero-filled placeholder. Always build to a temporary
        // .ttarch2 file, validate its magic, then rename it to the requested 0.ttarch.
        string temporaryOutput = Path.Combine(
            outputDirectory,
            $".ucrew_{Guid.NewGuid():N}.ttarch2");

        List<string> logs = new();
        bool built = false;
        string lastMagic = string.Empty;

        string[] argumentVariants =
        {
            string.Join(' ',
                "-b",
                "-V",
                GuardiansArchiveVersion,
                GuardiansGameNumber,
                Quote(temporaryOutput),
                Quote(fullPatchDirectory)),

            string.Join(' ',
                "-b",
                GuardiansGameNumber,
                Quote(temporaryOutput),
                Quote(fullPatchDirectory))
        };

        try
        {
            foreach (string arguments in argumentVariants)
            {
                token.ThrowIfCancellationRequested();

                if (File.Exists(temporaryOutput))
                    File.Delete(temporaryOutput);

                ProcessRunResult process = await RunAsync(
                        toolPath,
                        arguments,
                        Path.GetDirectoryName(toolPath)!,
                        token)
                    .ConfigureAwait(false);

                logs.Add(
                    $"> ttarchext.exe {arguments}\n" +
                    $"Çıkış kodu: {process.ExitCode}\n" +
                    process.StandardError + Environment.NewLine + process.StandardOutput);

                result.StandardOutput = process.StandardOutput;
                result.StandardError = process.StandardError;

                if (process.ExitCode != 0 ||
                    !File.Exists(temporaryOutput) ||
                    new FileInfo(temporaryOutput).Length < 4)
                {
                    continue;
                }

                lastMagic = await ReadMagicAsync(temporaryOutput, token).ConfigureAwait(false);
                if (!IsTtarch2Magic(lastMagic))
                    continue;

                built = true;
                break;
            }

            if (!built)
            {
                string headerText = string.IsNullOrEmpty(lastMagic)
                    ? "boş veya oluşturulamadı"
                    : FormatMagic(lastMagic);

                result.Errors.Add(
                    "ttarchext geçerli bir Guardians TTARCH2 yaması oluşturamadı. " +
                    $"Son başlık: {headerText}.\n\n" +
                    string.Join("\n\n", logs));
                return result;
            }

            File.Move(temporaryOutput, output, overwrite: true);

            string finalMagic = await ReadMagicAsync(output, token).ConfigureAwait(false);
            if (!IsTtarch2Magic(finalMagic))
            {
                File.Delete(output);
                result.Errors.Add(
                    $"Oluşturulan dosyanın son başlığı geçersiz: {FormatMagic(finalMagic)}. " +
                    "Beklenen başlık NCTT veya zCTT.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryOutput))
                    File.Delete(temporaryOutput);
            }
            catch
            {
                // Temporary cleanup failure must not hide the actual build result.
            }
        }

        return result;
    }

    private static bool IsTtarch2Magic(string magic) =>
        magic.Equals("NCTT", StringComparison.Ordinal) ||
        magic.Equals("zCTT", StringComparison.Ordinal);

    private static async Task<string> ReadMagicAsync(string path, CancellationToken token)
    {
        byte[] buffer = new byte[4];
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), token)
                .ConfigureAwait(false);

            if (read == 0)
                break;

            totalRead += read;
        }

        return Encoding.ASCII.GetString(buffer, 0, totalRead);
    }

    private static string FormatMagic(string magic)
    {
        if (string.IsNullOrEmpty(magic))
            return "boş";

        return string.Concat(magic.Select(character =>
            char.IsControl(character) ? $"\\x{(int)character:X2}" : character.ToString()));
    }

    private static string GetWorkingDirectory(string archivePath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(archivePath.ToLowerInvariant()));
        string id = Convert.ToHexString(hash.AsSpan(0, 8));
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UCrewTTARCH2",
            "Work");
        return Path.Combine(root, id);
    }

    private static async Task<ProcessRunResult> RunAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken token)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(token);

        await process.WaitForExitAsync(token).ConfigureAwait(false);

        return new ProcessRunResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
}
