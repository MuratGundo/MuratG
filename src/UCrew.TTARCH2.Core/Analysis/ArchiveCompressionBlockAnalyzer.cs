using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class ArchiveCompressionBlockAnalyzer : IAnalyzer
{
    public string Name => "TTARCH2 Compression Block Analyzer";

    public int Priority => 330;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ArchiveModel model = context.Model;
        var reader = context.Archive.Reader;
        model.CompressionBlocks.Clear();

        int index = 0;
        const int scanChunkSize = 1024 * 1024;
        long position = 0;

        while (position < model.FileSize)
        {
            token.ThrowIfCancellationRequested();

            int length = (int)Math.Min(scanChunkSize, model.FileSize - position);
            using var _ = reader.Bookmark();
            reader.Seek(position);
            byte[] data = reader.ReadBytes(length);

            for (int i = 0; i + 2 < data.Length; i++)
            {
                if (!LooksLikeZlibHeader(data[i], data[i + 1]))
                    continue;

                long absoluteOffset = position + i;
                model.CompressionBlocks.Add(new ArchiveCompressionBlock
                {
                    Index = index++,
                    HeaderOffset = absoluteOffset,
                    DataOffset = absoluteOffset,
                    CompressedSize = 0,
                    UncompressedSize = 0,
                    Codec = "Zlib candidate",
                    Confidence = 0.55
                });
            }

            position += length;
        }

        return Task.CompletedTask;
    }

    private static bool LooksLikeZlibHeader(byte cmf, byte flg)
    {
        if ((cmf & 0x0F) != 8)
            return false;

        int combined = (cmf << 8) | flg;
        return combined % 31 == 0;
    }
}
