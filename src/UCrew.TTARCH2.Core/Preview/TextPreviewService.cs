using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Preview;

public sealed class TextPreviewService
{
    public const int DefaultPreviewLength = 256 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<string> ReadChunkTextAsync(
        ArchiveModel archive,
        ChunkModel chunk,
        int maxBytes = DefaultPreviewLength,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(chunk);

        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        if (chunk.Offset < 0 || chunk.EndOffset <= chunk.Offset || chunk.EndOffset > archive.FileSize)
            throw new InvalidDataException("Chunk boundaries are invalid.");

        int length = (int)Math.Min(chunk.Length, maxBytes);
        byte[] buffer = new byte[length];

        await using FileStream stream = new(
            archive.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        stream.Position = chunk.Offset;
        int total = 0;

        while (total < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, length - total), token).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }

        ReadOnlySpan<byte> data = buffer.AsSpan(0, total);
        string text;

        try
        {
            text = StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.UTF8.GetString(data);
        }

        if (chunk.Length > maxBytes)
            text += "\r\n\r\n[Metin önizlemesi ilk 256 KB ile sınırlandırıldı.]";

        return text;
    }

    public async Task SaveEditedTextAsync(
        string text,
        string outputPath,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        await File.WriteAllTextAsync(outputPath, text ?? string.Empty, new UTF8Encoding(false), token)
            .ConfigureAwait(false);
    }
}
