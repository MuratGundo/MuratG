using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Preview;

public sealed class ChunkTypeDetector
{
    private const int ProbeLength = 512;

    public async Task<ChunkTypeInfo> DetectAsync(
        ArchiveModel archive,
        ChunkModel chunk,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(chunk);

        int length = (int)Math.Min(chunk.Length, ProbeLength);
        byte[] data = new byte[length];

        await using FileStream stream = new(
            archive.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        stream.Position = chunk.Offset;
        int total = 0;

        while (total < data.Length)
        {
            int read = await stream.ReadAsync(data.AsMemory(total), token).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }

        ReadOnlySpan<byte> span = data.AsSpan(0, total);

        if (StartsWith(span, "DDS "))
            return New("DDS Texture", ".dds", "Texture", 1.0);
        if (span.StartsWith([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return New("PNG Image", ".png", "Image", 1.0);
        if (StartsWith(span, "OggS"))
            return New("Ogg Audio", ".ogg", "Audio", 1.0);
        if (StartsWith(span, "RIFF"))
            return New("RIFF Container", ".riff", "Audio/Container", 0.95);
        if (span.StartsWith([0x78, 0x9C]) || span.StartsWith([0x78, 0xDA]))
            return New("Zlib Stream", ".zlib", "Compression", 0.90);
        if (span.StartsWith([0x28, 0xB5, 0x2F, 0xFD]))
            return New("Zstandard Stream", ".zst", "Compression", 1.0);
        if (span.StartsWith([0x04, 0x22, 0x4D, 0x18]))
            return New("LZ4 Frame", ".lz4", "Compression", 1.0);

        double printableRatio = GetPrintableRatio(span);
        if (printableRatio >= 0.85)
        {
            string text = Encoding.UTF8.GetString(span);
            if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("["))
                return New("JSON Text", ".json", "Text", 0.85);
            if (text.Contains("<", StringComparison.Ordinal) && text.Contains(">", StringComparison.Ordinal))
                return New("XML-like Text", ".xml", "Text", 0.70);
            return New("Plain Text", ".txt", "Text", Math.Min(0.90, printableRatio));
        }

        return New("Unknown Binary", ".bin", "Binary", 0.25);
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, string value)
    {
        return data.StartsWith(Encoding.ASCII.GetBytes(value));
    }

    private static double GetPrintableRatio(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return 0;

        int printable = 0;
        foreach (byte value in data)
        {
            if (value is 9 or 10 or 13 || value is >= 32 and <= 126)
                printable++;
        }

        return (double)printable / data.Length;
    }

    private static ChunkTypeInfo New(string name, string extension, string category, double confidence)
    {
        return new ChunkTypeInfo
        {
            Name = name,
            Extension = extension,
            Category = category,
            Confidence = confidence
        };
    }
}
