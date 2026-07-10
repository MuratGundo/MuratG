using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Extraction;

public sealed class SingleChunkExportService
{
    private const int BufferSize = 128 * 1024;

    public async Task ExportAsync(
        ArchiveModel archive,
        ChunkModel chunk,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(chunk);

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        if (chunk.Offset < 0 || chunk.EndOffset <= chunk.Offset || chunk.EndOffset > archive.FileSize)
            throw new InvalidDataException("Chunk boundaries are invalid.");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using FileStream input = new(
            archive.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream output = new(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        input.Position = chunk.Offset;
        long remaining = chunk.Length;
        byte[] buffer = new byte[BufferSize];

        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = await input.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException($"Unexpected EOF while exporting chunk {chunk.Index}.");

            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
