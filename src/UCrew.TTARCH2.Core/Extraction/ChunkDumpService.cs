using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Extraction;

public sealed class ChunkDumpService
{
    private const int BufferSize = 128 * 1024;

    public async Task<int> DumpAsync(
        ArchiveModel archive,
        string outputDirectory,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is empty.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        await using FileStream input = new(
            archive.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[BufferSize];
        int dumped = 0;

        foreach (ChunkModel chunk in archive.Chunks.OrderBy(x => x.Index))
        {
            token.ThrowIfCancellationRequested();
            ValidateChunk(chunk, archive.FileSize);

            string fileName = $"chunk_{chunk.Index:D4}_0x{chunk.Offset:X}_0x{chunk.Length:X}.bin";
            string outputPath = Path.Combine(outputDirectory, fileName);

            input.Position = chunk.Offset;

            await using FileStream output = new(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long remaining = chunk.Length;

            while (remaining > 0)
            {
                int request = (int)Math.Min(buffer.Length, remaining);
                int read = await input.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);

                if (read == 0)
                    throw new EndOfStreamException($"Unexpected EOF while dumping chunk {chunk.Index}.");

                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                remaining -= read;
            }

            dumped++;
        }

        return dumped;
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
