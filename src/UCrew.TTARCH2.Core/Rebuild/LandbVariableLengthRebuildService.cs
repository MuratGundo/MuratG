using System.Buffers.Binary;
using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbVariableLengthRebuildService
{
    private const int BufferSize = 128 * 1024;

    public async Task<LandbVariableLengthRebuildResult> RebuildAsync(
        ArchiveModel archive,
        string translatedTextPath,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        LandbRebuildPlan plan = await new LandbVariableLengthPlanner()
            .CreatePlanAsync(archive, translatedTextPath, token)
            .ConfigureAwait(false);

        LandbVariableLengthRebuildResult result = new()
        {
            Plan = plan,
            OutputPath = Path.GetFullPath(outputPath)
        };

        result.Warnings.AddRange(plan.Warnings);

        if (!plan.CanRebuild)
        {
            result.Errors.AddRange(plan.Errors);
            return result;
        }

        string sourcePath = Path.GetFullPath(archive.FullPath);
        string destinationPath = Path.GetFullPath(outputPath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The original LANDb file cannot be overwritten.");
            return result;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        List<LandbRebuildEntry> entries = plan.Entries
            .OrderBy(x => x.OriginalOffset)
            .ToList();

        await BuildFileAsync(sourcePath, destinationPath, entries, archive.FileSize, token)
            .ConfigureAwait(false);

        await PatchMetadataAsync(destinationPath, archive, plan, result, token)
            .ConfigureAwait(false);

        FileInfo outputInfo = new(destinationPath);
        result.OutputFileSize = outputInfo.Length;

        if (result.OutputFileSize != plan.PlannedFileSize)
        {
            result.Errors.Add(
                $"Rebuilt file size mismatch. Planned {plan.PlannedFileSize:N0}, produced {result.OutputFileSize:N0}.");
        }

        await VerifyOutputAsync(destinationPath, plan, result, token).ConfigureAwait(false);

        if (!result.Success && File.Exists(destinationPath))
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch
            {
                result.Warnings.Add("The invalid output file could not be deleted automatically.");
            }
        }

        return result;
    }

    private static async Task BuildFileAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<LandbRebuildEntry> entries,
        long sourceLength,
        CancellationToken token)
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

        long sourceCursor = 0;

        foreach (LandbRebuildEntry entry in entries)
        {
            token.ThrowIfCancellationRequested();

            if (entry.OriginalOffset < sourceCursor)
                throw new InvalidDataException($"Overlapping LANDb text entry at 0x{entry.OriginalOffset:X}.");

            await CopyRangeAsync(source, destination, sourceCursor, entry.OriginalOffset - sourceCursor, token)
                .ConfigureAwait(false);

            byte[] replacement = new UTF8Encoding(false).GetBytes(entry.Text);
            await destination.WriteAsync(replacement, token).ConfigureAwait(false);

            sourceCursor = entry.OriginalOffset + entry.OriginalByteLength;
        }

        await CopyRangeAsync(source, destination, sourceCursor, sourceLength - sourceCursor, token)
            .ConfigureAwait(false);

        await destination.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task CopyRangeAsync(
        FileStream source,
        FileStream destination,
        long sourceOffset,
        long length,
        CancellationToken token)
    {
        if (length < 0)
            throw new InvalidDataException("Negative copy range detected during LANDb rebuild.");

        source.Position = sourceOffset;
        byte[] buffer = new byte[BufferSize];
        long remaining = length;

        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);

            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF during LANDb rebuild.");

            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static async Task PatchMetadataAsync(
        string destinationPath,
        ArchiveModel archive,
        LandbRebuildPlan plan,
        LandbVariableLengthRebuildResult result,
        CancellationToken token)
    {
        await using FileStream output = new(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        foreach (LandbRebuildEntry entry in plan.Entries)
        {
            foreach (long oldPointerSource in entry.PointerSourceOffsets.Distinct())
            {
                PointerHit? pointer = archive.Pointers.FirstOrDefault(x => x.SourceOffset == oldPointerSource);
                if (pointer is null)
                {
                    result.Errors.Add($"Pointer metadata missing at 0x{oldPointerSource:X}.");
                    continue;
                }

                long newPointerSource = MapOffset(oldPointerSource, plan.Entries);
                await WriteIntegerAsync(output, newPointerSource, pointer.Size, entry.NewOffset, token)
                    .ConfigureAwait(false);
                result.UpdatedPointerCount++;
            }

            foreach (LandbRebuildLengthField field in entry.LengthFields)
            {
                long newFieldOffset = MapOffset(field.FieldOffset, plan.Entries);
                await WriteIntegerAsync(output, newFieldOffset, field.FieldSize, field.NewValue, token)
                    .ConfigureAwait(false);
                result.UpdatedLengthFieldCount++;
            }
        }

        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task WriteIntegerAsync(
        FileStream stream,
        long offset,
        int size,
        long value,
        CancellationToken token)
    {
        if (offset < 0 || offset + size > stream.Length)
            throw new InvalidDataException($"Metadata write is outside rebuilt file bounds at 0x{offset:X}.");

        byte[] buffer = new byte[size];

        switch (size)
        {
            case 2 when value <= ushort.MaxValue:
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)value);
                break;
            case 4 when value <= uint.MaxValue:
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value);
                break;
            case 8:
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value);
                break;
            default:
                throw new InvalidDataException($"Unsupported metadata field size/value: {size} bytes, {value}.");
        }

        stream.Position = offset;
        await stream.WriteAsync(buffer, token).ConfigureAwait(false);
    }

    private static long MapOffset(long originalOffset, IReadOnlyList<LandbRebuildEntry> entries)
    {
        long delta = 0;

        foreach (LandbRebuildEntry entry in entries)
        {
            if (entry.OriginalOffset >= originalOffset)
                break;

            delta += entry.Delta;
        }

        return originalOffset + delta;
    }

    private static async Task VerifyOutputAsync(
        string outputPath,
        LandbRebuildPlan plan,
        LandbVariableLengthRebuildResult result,
        CancellationToken token)
    {
        await using FileStream stream = new(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        foreach (LandbRebuildEntry entry in plan.Entries)
        {
            token.ThrowIfCancellationRequested();

            byte[] expected = new UTF8Encoding(false).GetBytes(entry.Text);
            byte[] actual = new byte[expected.Length];
            stream.Position = entry.NewOffset;

            int total = 0;
            while (total < actual.Length)
            {
                int read = await stream.ReadAsync(actual.AsMemory(total), token).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }

            if (total != expected.Length || !actual.AsSpan(0, total).SequenceEqual(expected))
            {
                result.Errors.Add($"Text verification failed for line {entry.Index + 1:N0} at 0x{entry.NewOffset:X}.");
            }
        }
    }
}
