using System.Text;
using UCrew.TTARCH2.Core.Import;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbFixedSizeImportService
{
    private const int BufferSize = 128 * 1024;

    public async Task<LandbTextImportResult> ImportToCopyAsync(
        ArchiveModel archive,
        string translatedTextPath,
        string outputLandbPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        LandbTextImportResult validation = await new LandbTextImportService()
            .ValidateAsync(archive, translatedTextPath, token)
            .ConfigureAwait(false);

        if (!validation.Success)
            return validation;

        for (int i = 0; i < validation.Lines.Count; i++)
        {
            int byteCount = Encoding.UTF8.GetByteCount(validation.Lines[i]);
            int expected = archive.Landb.TextCandidates[i].ByteLength;

            if (byteCount != expected)
            {
                validation.Errors.Add(
                    $"Line {i + 1:N0} byte size mismatch. Expected {expected:N0}, got {byteCount:N0}.");
            }
        }

        if (!validation.Success)
            return validation;

        string sourcePath = Path.GetFullPath(archive.FullPath);
        string destinationPath = Path.GetFullPath(outputLandbPath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            validation.Errors.Add("The original LANDb file cannot be overwritten.");
            return validation;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await CopyFileAsync(sourcePath, destinationPath, token).ConfigureAwait(false);

        await using FileStream output = new(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (output.Length != archive.FileSize)
            throw new InvalidDataException("Copied LANDb size does not match the source file size.");

        for (int i = 0; i < validation.Lines.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            LandbTextCandidate candidate = archive.Landb.TextCandidates[i];
            byte[] replacement = new UTF8Encoding(false).GetBytes(validation.Lines[i]);

            output.Position = candidate.Offset;
            await output.WriteAsync(replacement, token).ConfigureAwait(false);
        }

        await output.FlushAsync(token).ConfigureAwait(false);

        if (output.Length != archive.FileSize)
            throw new InvalidDataException("LANDb import changed the total file size unexpectedly.");

        return validation;
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken token)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, BufferSize, token).ConfigureAwait(false);
        await destination.FlushAsync(token).ConfigureAwait(false);
    }
}
