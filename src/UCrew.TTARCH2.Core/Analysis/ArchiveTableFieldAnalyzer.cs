using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class ArchiveTableFieldAnalyzer : IAnalyzer
{
    public string Name => "TTARCH2 Table Field Analyzer";

    public int Priority => 320;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ArchiveModel model = context.Model;
        var reader = context.Archive.Reader;
        model.TableFields.Clear();

        foreach (ArchiveResourceEntry resource in model.Resources)
        {
            token.ThrowIfCancellationRequested();

            long searchEnd = Math.Min(model.FileSize, Math.Max(0, model.Header.HeaderLength));
            if (searchEnd <= 0)
                searchEnd = Math.Min(model.FileSize, 1024 * 1024);

            ScanForValue(reader, model, resource, resource.Offset, "Offset", searchEnd, token);
            ScanForValue(reader, model, resource, resource.Size, "Size", searchEnd, token);
        }

        return Task.CompletedTask;
    }

    private static void ScanForValue(
        UCrew.TTARCH2.Core.Binary.BinaryReaderX reader,
        ArchiveModel model,
        ArchiveResourceEntry resource,
        long expectedValue,
        string kind,
        long searchEnd,
        CancellationToken token)
    {
        if (expectedValue < 0 || expectedValue > uint.MaxValue)
            return;

        for (long offset = 0; offset + 4 <= searchEnd; offset += 4)
        {
            token.ThrowIfCancellationRequested();
            using var _ = reader.Bookmark();
            reader.Seek(offset);
            uint value = reader.ReadUInt32();

            if (value != expectedValue)
                continue;

            double confidence = 0.45;
            if (kind == "Offset" && model.Pointers.Any(x => x.SourceOffset == offset && x.TargetOffset == expectedValue))
                confidence = 0.90;
            else if (offset < 0x10000)
                confidence = 0.60;

            model.TableFields.Add(new ArchiveTableField
            {
                ResourceIndex = resource.Index,
                FieldOffset = offset,
                FieldSize = 4,
                StoredValue = value,
                Kind = kind,
                Confidence = confidence
            });
        }
    }
}
