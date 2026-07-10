using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class FixedSizeChunkPatchService
{
    private const int BufferSize = 128 * 1024;

    public async Task PatchTextToCopyAsync(
        ArchiveModel archive,
        ChunkModel chunk,
        string editedText,
        string outputArchivePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(chunk);

        byte[] replacement = new UTF8Encoding(false).GetBytes(editedText ?? string.Empty);
        await PatchBytesToCopyAsync(archive, chunk, replacement, outputArchivePath, token).ConfigureAwait(false);
    }

    public async Task PatchBytesToCopyAsync(
        ArchiveModel archive,
        ChunkModel chunk,
        ReadOnlyMemory<byte> replacement,
        string outputArchivePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(chunk);

        if (string.IsNullOrWhiteSpace(outputArchivePath))
            throw new ArgumentException("Output archive path is empty.", nameof(outputArchivePath));

        ValidateChunk(chunk, archive.FileSize);

        if (replacement.Length != chunk.Length)
        {
            throw new InvalidDataException(
                $"Replacement size must exactly match the chunk size. Expected {chunk.Length:N0} bytes, got {replacement.Length:N0} bytes.");
        }

        string sourcePath = Path.GetFullPath(archive.FullPath);
        string destinationPath = Path.GetFullPath(outputArchivePath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The original archive cannot be overwritten. Choose a different output path.");

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await CopyArchiveAsync(sourcePath, destinationPath, token).ConfigureAwait(false);

        await using FileStream output = new(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (output.Length != archive.FileSize)
            throw new InvalidDataException("The copied archive size does not match the original archive size.");

        output.Position = chunk.Offset;
        await output.WriteAsync(replacement, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);

        if (output.Length != archive.FileSize)
            throw new InvalidDataException("Patching changed the archive size unexpectedly.");
    }

    private static async Task CopyArchiveAsync(string sourcePath, string destinationPath, CancellationToken token)
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

    private static void ValidateChunk(ChunkModel chunk, long fileSize)
    {
        if (chunk.Offset < 0 || chunk.EndOffset <= chunk.Offset || chunk.EndOffset > fileSize)
        {
            throw new InvalidDataException(
                $"Chunk {chunk.Index} has invalid bounds: 0x{chunk.Offset:X}-0x{chunk.EndOffset:X}.");
        }
    }
}
