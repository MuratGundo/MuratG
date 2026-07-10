using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class EcttAnalyzer : IAnalyzer
{
    public string Name => "ECTT Analyzer";

    public int Priority => 275;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ArchiveModel model = context.Model;

        if (!string.Equals(model.Header.Magic, "ECTT", StringComparison.Ordinal))
        {
            model.Ectt.LooksLikeEctt = false;
            model.Ectt.Notes = "Magic is not ECTT.";
            return Task.CompletedTask;
        }

        var reader = context.Archive.Reader;
        List<long> offsets = ReadOffsetCandidates(reader, token);

        if (offsets.Count < 2)
        {
            model.Ectt.LooksLikeEctt = true;
            model.Ectt.Confidence = 0.25;
            model.Ectt.Notes = "ECTT magic found, but no usable chunk offset table was detected.";
            return Task.CompletedTask;
        }

        model.Ectt.LooksLikeEctt = true;
        model.Ectt.OffsetTableStart = 0x10;
        model.Ectt.OffsetTableEnd = 0x10 + offsets.Count * 8;
        model.Ectt.OffsetEntryCount = offsets.Count;
        model.Ectt.ChunkCount = offsets.Count - 1;
        model.Ectt.Confidence = offsets[^1] == model.FileSize ? 0.95 : 0.70;
        model.Ectt.Notes = offsets[^1] == model.FileSize
            ? "Detected monotonic ECTT-style offset table ending at EOF."
            : "Detected monotonic ECTT-style offset table, but last entry is not EOF.";

        for (int i = 0; i < offsets.Count - 1; i++)
        {
            long start = offsets[i];
            long end = offsets[i + 1];

            if (end <= start)
                continue;

            model.Chunks.Add(new ChunkModel
            {
                Index = i,
                Offset = start,
                EndOffset = end,
                Entropy = FindEntropyNear(model, start),
                Source = "ECTT offset table",
                Confidence = model.Ectt.Confidence
            });
        }

        return Task.CompletedTask;
    }

    private static List<long> ReadOffsetCandidates(UCrew.TTARCH2.Core.Binary.BinaryReaderX reader, CancellationToken token)
    {
        List<long> offsets = new();

        reader.Seek(0x10);

        while (reader.Remaining >= 8)
        {
            token.ThrowIfCancellationRequested();

            long entryOffset = reader.Position;
            uint reserved = reader.ReadUInt32();
            uint value = reader.ReadUInt32();

            if (reserved != 0)
                break;

            if (value == 0)
                break;

            if (offsets.Count > 0 && value <= offsets[^1])
                break;

            offsets.Add(value);

            if (offsets.Count > 4096)
                break;

            if (entryOffset > 0x20000 && offsets.Count < 4)
                break;
        }

        return offsets;
    }

    private static double FindEntropyNear(ArchiveModel model, long offset)
    {
        EntropyPoint? point = model.EntropyMap
            .OrderBy(x => Math.Abs(x.Offset - offset))
            .FirstOrDefault();

        return point?.Value ?? 0;
    }
}
