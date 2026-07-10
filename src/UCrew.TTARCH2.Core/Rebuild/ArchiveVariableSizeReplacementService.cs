using System.Buffers.Binary;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveVariableSizeReplacementService
{
    private const int BufferSize = 128 * 1024;

    public async Task<ArchiveVariableSizeReplacementResult> ReplaceToCopyAsync(
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

        if (!File.Exists(replacementPath))
        {
            result.Errors.Add("Replacement LANDb file does not exist.");
            return result;
        }

        if (resource.Offset < 0 || resource.Size <= 0 || resource.Offset + resource.Size > archive.FileSize)
        {
            result.Errors.Add("Selected archive resource boundaries are invalid.");
            return result;
        }

        string sourcePath = Path.GetFullPath(archive.FullPath);
        string destinationPath = Path.GetFullPath(outputArchivePath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The original TTARCH2 archive cannot be overwritten.");
            return result;
        }

        long replacementSize = new FileInfo(replacementPath).Length;
        long delta = replacementSize - resource.Size;

        List<PointerHit> affectedPointers = archive.Pointers
            .Where(x => x.TargetOffset > resource.Offset)
            .OrderBy(x => x.SourceOffset)
            .ToList();

        if (delta != 0 && affectedPointers.Count == 0)
        {
            result.Errors.Add(
                "Archive size changes, but no pointer fields after the replaced resource were identified.");
            return result;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await RebuildFileAsync(sourcePath, replacementPath, destinationPath, resource, token)
            .ConfigureAwait(false);

        await PatchPointersAsync(destinationPath, resource, delta, affectedPointers, result, token)
            .ConfigureAwait(false);

        result.OutputFileSize = new FileInfo(destinationPath).Length;
        long expectedSize = archive.FileSize + delta;

        if (result.OutputFileSize != expectedSize)
        {
            result.Errors.Add(
                $"Rebuilt TTARCH2 size mismatch. Expected {expectedSize:N0}, got {result.OutputFileSize:N0}.");
        }

        await VerifyReplacementAsync(destinationPath, replacementPath, resource.Offset, replacementSize, result, token)
            .ConfigureAwait(false);

        if (!result.Success && File.Exists(destinationPath))
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch
            {
                result.Warnings.Add("Invalid rebuilt archive could not be deleted automatically.");
            }
        }

        if (delta != 0)
        {
            result.Warnings.Add(
                "Variable-size TTARCH2 replacement is experimental until the exact file table and compressed block metadata are confirmed for this game build.");
        }

        return result;
    }

    private static async Task RebuildFileAsync(
        string sourcePath,
        string replacementPath,
        string destinationPath,
        ArchiveResourceEntry resource,
        CancellationToken token)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream replacement = new(
            replacementPath,
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

        await CopyRangeAsync(source, destination, 0, resource.Offset, token).ConfigureAwait(false);
        await replacement.CopyToAsync(destination, BufferSize, token).ConfigureAwait(false);

        long tailOffset = resource.Offset + resource.Size;
        await CopyRangeAsync(source, destination, tailOffset, source.Length - tailOffset, token)
            .ConfigureAwait(false);

        await destination.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task PatchPointersAsync(
        string destinationPath,
        ArchiveResourceEntry resource,
        long delta,
        IReadOnlyList<PointerHit> pointers,
        ArchiveVariableSizeReplacementResult result,
        CancellationToken token)
    {
        await using FileStream stream = new(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        foreach (PointerHit pointer in pointers)
        {
            token.ThrowIfCancellationRequested();

            long newSourceOffset = pointer.SourceOffset > resource.Offset
                ? pointer.SourceOffset + delta
                : pointer.SourceOffset;
            long newTargetOffset = pointer.TargetOffset + delta;

            if (newSourceOffset < 0 || newSourceOffset + pointer.Size > stream.Length)
            {
                result.Errors.Add($"Pointer field moved outside archive bounds at 0x{newSourceOffset:X}.");
                continue;
            }

            byte[] buffer = new byte[pointer.Size];
            switch (pointer.Size)
            {
                case 4 when newTargetOffset <= uint.MaxValue:
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)newTargetOffset);
                    break;
                case 8:
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)newTargetOffset);
                    break;
                default:
                    result.Errors.Add($"Unsupported pointer field size {pointer.Size} at 0x{pointer.SourceOffset:X}.");
                    continue;
            }

            stream.Position = newSourceOffset;
            await stream.WriteAsync(buffer, token).ConfigureAwait(false);
            result.UpdatedPointerCount++;
        }

        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task VerifyReplacementAsync(
        string outputPath,
        string replacementPath,
        long replacementOffset,
        long replacementSize,
        ArchiveVariableSizeReplacementResult result,
        CancellationToken token)
    {
        await using FileStream output = new(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        await using FileStream replacement = new(
            replacementPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        output.Position = replacementOffset;
        byte[] outputBuffer = new byte[BufferSize];
        byte[] replacementBuffer = new byte[BufferSize];
        long remaining = replacementSize;

        while (remaining > 0)
        {
            int request = (int)Math.Min(BufferSize, remaining);
            int replacementRead = await replacement.ReadAsync(replacementBuffer.AsMemory(0, request), token)
                .ConfigureAwait(false);
            int outputRead = await output.ReadAsync(outputBuffer.AsMemory(0, request), token)
                .ConfigureAwait(false);

            if (replacementRead != outputRead
                || replacementRead == 0
                || !replacementBuffer.AsSpan(0, replacementRead).SequenceEqual(outputBuffer.AsSpan(0, outputRead)))
            {
                result.Errors.Add("Replacement LANDb verification failed inside rebuilt TTARCH2 archive.");
                return;
            }

            remaining -= replacementRead;
        }
    }

    private static async Task CopyRangeAsync(
        FileStream source,
        FileStream destination,
        long sourceOffset,
        long length,
        CancellationToken token)
    {
        source.Position = sourceOffset;
        byte[] buffer = new byte[BufferSize];
        long remaining = length;

        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while rebuilding TTARCH2 archive.");

            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
