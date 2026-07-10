using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Preview;

public sealed class HexPreviewService
{
    public const int DefaultPreviewLength = 4096;

    public async Task<string> CreateChunkPreviewAsync(
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

        return Format(buffer.AsSpan(0, total), chunk.Offset, chunk.Length > maxBytes);
    }

    private static string Format(ReadOnlySpan<byte> data, long baseOffset, bool truncated)
    {
        StringBuilder builder = new(data.Length * 4);

        for (int lineStart = 0; lineStart < data.Length; lineStart += 16)
        {
            int count = Math.Min(16, data.Length - lineStart);
            builder.Append((baseOffset + lineStart).ToString("X8"));
            builder.Append("  ");

            for (int i = 0; i < 16; i++)
            {
                if (i < count)
                    builder.Append(data[lineStart + i].ToString("X2"));
                else
                    builder.Append("  ");

                builder.Append(i == 7 ? "  " : " ");
            }

            builder.Append(" |");

            for (int i = 0; i < count; i++)
            {
                byte value = data[lineStart + i];
                builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }

            builder.AppendLine("|");
        }

        if (truncated)
            builder.AppendLine("\n[Önizleme ilk 4096 bayt ile sınırlandırıldı]");

        return builder.ToString();
    }
}
