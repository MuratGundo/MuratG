using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class ECTTAnalyzer : IAnalyzer
{
    public string Name => "ECTT Analyzer";

    public int Priority => 260;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        if (!string.Equals(context.Model.Header.Magic, "ECTT", StringComparison.Ordinal))
            return Task.CompletedTask;

        var reader = context.Archive.Reader;
        long fileSize = reader.Length;

        List<long> offsets = ReadOffsetDirectory(reader, fileSize, token);

        if (offsets.Count < 2)
        {
            context.Model.Validation.Warnings.Add("ECTT offset directory candidate was not found.");
            return Task.CompletedTask;
        }

        for (int i = 0; i < offsets.Count - 1; i++)
        {
            long start = offsets[i];
            long end = offsets[i + 1];

            if (end <= start)
                continue;

            context.Model.ECTTChunks.Add(new ECTTChunkInfo
            {
                Index = i,
                Offset = start,
                EndOffset = end,
                Confidence = 0.90
            });
        }

        context.Model.Regions.Add(new RegionModel
        {
            StartOffset = 0x10,
            EndOffset = 0x10 + offsets.Count * 8L,
            Kind = RegionKind.PossiblePointerTable,
            Confidence = 0.95,
            Description = $"ECTT offset directory candidate, entries: {offsets.Count}"
        });

        return Task.CompletedTask;
    }

    private static List<long> ReadOffsetDirectory(
        Binary.BinaryReaderX reader,
        long fileSize,
        CancellationToken token)
    {
        List<long> offsets = new();

        reader.Seek(0x10);

        while (reader.Remaining >= 8)
        {
            token.ThrowIfCancellationRequested();

            long source = reader.Position;
            uint zero = reader.ReadUInt32();
            uint value = reader.ReadUInt32();

            if (zero != 0)
                break;

            if (value == 0 || value > fileSize)
                break;

            if (offsets.Count > 0 && value <= offsets[^1])
                break;

            offsets.Add(value);

            if (value == fileSize)
                break;

            long next = source + 8;
            if (next <= source)
                break;
        }

        if (offsets.Count > 0 && offsets[^1] != fileSize)
            offsets.Clear();

        return offsets;
    }
}
