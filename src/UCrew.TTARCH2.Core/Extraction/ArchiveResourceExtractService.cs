using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Extraction;

public sealed class ArchiveResourceExtractService
{
    private const int BufferSize = 128 * 1024;

    public async Task ExtractAsync(
        ArchiveModel archive,
        ArchiveResourceEntry resource,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resource);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (resource.IsExtracted)
        {
            await using FileStream extracted = new(
                resource.ExtractedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using FileStream extractedOutput = new(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await extracted.CopyToAsync(extractedOutput, BufferSize, token).ConfigureAwait(false);
            return;
        }

        if (resource.Offset < 0 || resource.Size <= 0 || resource.Offset + resource.Size > archive.FileSize)
            throw new InvalidDataException("Resource boundaries are invalid.");

        await using FileStream input = new(
            archive.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        await using FileStream output = new(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        input.Position = resource.Offset;
        long remaining = resource.Size;
        byte[] buffer = new byte[BufferSize];

        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = await input.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while extracting archive resource.");

            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
