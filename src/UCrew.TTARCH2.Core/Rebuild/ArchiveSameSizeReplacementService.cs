using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveSameSizeReplacementService
{
    private const int BufferSize = 128 * 1024;

    public async Task ApplyToCopyAsync(
        ArchiveModel archive,
        ArchiveResourceEntry resource,
        string replacementPath,
        string outputArchivePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resource);

        ArchiveResourceReplacementPlan plan = new ArchiveResourceReplacementPlanner()
            .Create(archive, resource, replacementPath);

        if (!plan.CanApply)
            throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Errors));

        string sourcePath = Path.GetFullPath(archive.FullPath);
        string destinationPath = Path.GetFullPath(outputArchivePath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The original archive cannot be overwritten.");

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using (FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(destination, BufferSize, token).ConfigureAwait(false);
            await destination.FlushAsync(token).ConfigureAwait(false);
        }

        await using FileStream replacement = new(
            replacementPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream output = new(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        output.Position = resource.Offset;
        byte[] buffer = new byte[BufferSize];
        long remaining = resource.Size;

        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = await replacement.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while reading replacement resource.");

            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }

        await output.FlushAsync(token).ConfigureAwait(false);

        if (output.Length != archive.FileSize)
            throw new InvalidDataException("Archive size changed unexpectedly during same-size replacement.");
    }
}
