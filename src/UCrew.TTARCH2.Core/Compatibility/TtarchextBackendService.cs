using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Compatibility;

public sealed class TtarchextBackendService
{
    // ttarchext 0.3.2 game table index for Marvel's Guardians of the Galaxy.
    // Using -k 476f7447 ("GotG") is not equivalent to the built-in 55-byte key
    // used by ttarchext and causes encrypted chunks to fail during deflate decode.
    private const string GuardiansGameNumber = "62";

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
                  "Arşiv, bu ttarchext sürümünün desteklemediği Oodle sıkıştırması kullanıyor olabilir veya dosya bozuk olabilir."
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
            string extension = info.Extension;

            result.Resources.Add(new ArchiveResourceEntry
            {
                Index = index++,
                Name = relative,
                RelativePath = relative,
                ExtractedPath = info.FullName,
                ExtractionRoot = extractionDirectory,
                Extension = extension,
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

    public async Task<TtarchextBackendResult> RebuildGuardiansArchiveAsync(
        string extractedDirectory,
        string outputArchivePath,
        string sourceArchivePath,
        CancellationToken token = default)
    {
        TtarchextBackendResult result = new()
        {
            WorkingDirectory = Path.GetFullPath(extractedDirectory)
        };

        if (!TryFindTool(out string toolPath))
        {
            result.Errors.Add("ttarchext.exe bulunamadı.");
            return result;
        }

        result = new TtarchextBackendResult
        {
            WorkingDirectory = Path.GetFullPath(extractedDirectory),
            ToolPath = toolPath
        };

        string output = Path.GetFullPath(outputArchivePath);
        string? outputDirectory = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        if (File.Exists(output))
            File.Delete(output);

        // Stock ttarchext 0.3.2 does not support the custom -z Oodle rebuild switch.
        // Build a normal/uncompressed TTARCH2 using the built-in Guardians profile.
        string arguments = string.Join(' ',
            "-b",
            GuardiansGameNumber,
            Quote(output),
            Quote(Path.GetFullPath(extractedDirectory)));

        ProcessRunResult process = await RunAsync(toolPath, arguments, Path.GetDirectoryName(toolPath)!, token)
            .ConfigureAwait(false);

        result.StandardOutput = process.StandardOutput;
        result.StandardError = process.StandardError;

        if (process.ExitCode != 0 || !File.Exists(output))
        {
            result.Errors.Add(
                $"ttarchext rebuild işlemi başarısız oldu (çıkış kodu {process.ExitCode}).\n{process.StandardError}\n{process.StandardOutput}");
        }

        return result;
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
