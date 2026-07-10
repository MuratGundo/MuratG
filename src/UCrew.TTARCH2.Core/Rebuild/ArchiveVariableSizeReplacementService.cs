using System.Buffers.Binary;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveVariableSizeReplacementService
{
    private const int BufferSize = 128 * 1024;
    private const double MinimumTableFieldConfidence = 0.60;

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

        List<ArchiveTableField> selectedSizeFields = archive.TableFields
            .Where(x => x.ResourceIndex == resource.Index)
            .Where(x => x.Kind == "Size")
            .Where(x => x.Confidence >= MinimumTableFieldConfidence)
            .ToList();

        HashSet<int> followingResourceIndexes = archive.Resources
            .Where(x => x.Offset > resource.Offset)
            .Select(x => x.Index)
            .ToHashSet();

        List<ArchiveTableField> followingOffsetFields = archive.TableFields
            .Where(x => followingResourceIndexes.Contains(x.ResourceIndex))
            .Where(x => x.Kind == "Offset")
            .Where(x => x.Confidence >= MinimumTableFieldConfidence)
            .ToList();

        if (delta != 0 && selectedSizeFields.Count == 0)
        {
            result.Errors.Add("No high-confidence TTARCH2 size field was found for the selected LANDb resource.");
            return result;
        }

        if (delta != 0 && affectedPointers.Count == 0 && followingOffsetFields.Count == 0)
        {
            result.Errors.Add(
                "Archive size changes, but no following pointer or TTARCH2 offset fields were identified.");
            return result;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await RebuildFileAsync(sourcePath, replacementPath, destinationPath, resource, token)
            .ConfigureAwait(false);

        await PatchMetadataAsync(
                destinationPath,
                resource,
                replacementSize,
                delta,
                affectedPointers,
                followingOffsetFields,
                selectedSizeFields,
                result,
                token)
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
                "Variable-size TTARCH2 replacement remains experimental until compressed block metadata is confirmed for this game build.");
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

    private static async Task PatchMetadataAsync(
        string destinationPath,
        ArchiveResourceEntry resource,
        long replacementSize,
        long delta,
        IReadOnlyList<PointerHit> pointers,
        IReadOnlyList<ArchiveTableField> followingOffsetFields,
        IReadOnlyList<ArchiveTableField> selectedSizeFields,
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

        HashSet<long> writtenFieldOffsets = new();

        foreach (ArchiveTableField field in selectedSizeFields)
        {
            long newFieldOffset = MapMetadataOffset(field.FieldOffset, resource, delta);
            await WriteIntegerAsync(stream, newFieldOffset, field.FieldSize, replacementSize, token)
                .ConfigureAwait(false);
            writtenFieldOffsets.Add(newFieldOffset);
            result.UpdatedSizeFieldCount++;
        }

        foreach (ArchiveTableField field in followingOffsetFields)
        {
            long newFieldOffset = MapMetadataOffset(field.FieldOffset, resource, delta);
            long newValue = field.StoredValue + delta;
            await WriteIntegerAsync(stream, newFieldOffset, field.FieldSize, newValue, token)
                .ConfigureAwait(false);
            writtenFieldOffsets.Add(newFieldOffset);
            result.UpdatedOffsetFieldCount++;
        }

        foreach (PointerHit pointer in pointers)
        {
            token.ThrowIfCancellationRequested();

            long newSourceOffset = MapMetadataOffset(pointer.SourceOffset, resource, delta);
            long newTargetOffset = pointer.TargetOffset + delta;

            if (writtenFieldOffsets.Contains(newSourceOffset))
                continue;

            await WriteIntegerAsync(stream, newSourceOffset, pointer.Size, newTargetOffset, token)
                .ConfigureAwait(false);
            writtenFieldOffsets.Add(newSourceOffset);
            result.UpdatedPointerCount++;
        }

        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static long MapMetadataOffset(long originalFieldOffset, ArchiveResourceEntry resource, long delta)
    {
        return originalFieldOffset >= resource.Offset + resource.Size
            ? originalFieldOffset + delta
            : originalFieldOffset;
    }

    private static async Task WriteIntegerAsync(
        FileStream stream,
        long offset,
        int size,
        long value,
        CancellationToken token)
    {
        if (offset < 0 || offset + size > stream.Length)
            throw new InvalidDataException($"TTARCH2 metadata field is outside rebuilt archive bounds at 0x{offset:X}.");

        byte[] buffer = new byte[size];

        switch (size)
        {
            case 2 when value >= 0 && value <= ushort.MaxValue:
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)value);
                break;
            case 4 when value >= 0 && value <= uint.MaxValue:
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value);
                break;
            case 8 when value >= 0:
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value);
                break;
            default:
                throw new InvalidDataException($"Unsupported TTARCH2 metadata field size/value: {size} bytes, {value}.");
        }

        stream.Position = offset;
        await stream.WriteAsync(buffer, token).ConfigureAwait(false);
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
