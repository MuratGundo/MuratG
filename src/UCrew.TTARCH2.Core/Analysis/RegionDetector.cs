using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class RegionDetector : IAnalyzer
{
    public string Name => "Region Detector";

    public int Priority => 300;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        DetectHeader(context.Model);
        DetectPointerRegions(context.Model);
        DetectEntropyRegions(context.Model);
        return Task.CompletedTask;
    }

    private static void DetectHeader(ArchiveModel model)
    {
        model.Regions.Add(new RegionModel
        {
            StartOffset = 0,
            EndOffset = Math.Min(0x100, model.FileSize),
            Kind = RegionKind.Header,
            Confidence = 0.75,
            Description = "Initial header candidate"
        });
    }

    private static void DetectPointerRegions(ArchiveModel model)
    {
        var ordered = model.Pointers
            .Where(x => x.IsValid)
            .OrderBy(x => x.SourceOffset)
            .ToList();

        if (ordered.Count == 0)
            return;

        long start = ordered[0].SourceOffset;
        long previous = start;
        int count = 1;

        foreach (PointerHit pointer in ordered.Skip(1))
        {
            long gap = pointer.SourceOffset - previous;

            if (gap is 4 or 8)
            {
                previous = pointer.SourceOffset;
                count++;
                continue;
            }

            AddPointerRegion(model, start, previous, count);
            start = pointer.SourceOffset;
            previous = pointer.SourceOffset;
            count = 1;
        }

        AddPointerRegion(model, start, previous, count);
    }

    private static void AddPointerRegion(ArchiveModel model, long start, long previous, int count)
    {
        if (count < 8)
            return;

        model.Regions.Add(new RegionModel
        {
            StartOffset = start,
            EndOffset = Math.Min(previous + 8, model.FileSize),
            Kind = RegionKind.PossiblePointerTable,
            Confidence = Math.Min(0.95, 0.50 + count / 100.0),
            Description = $"Possible pointer table, entries: {count}"
        });
    }

    private static void DetectEntropyRegions(ArchiveModel model)
    {
        foreach (EntropyPoint point in model.EntropyMap)
        {
            RegionKind kind = point.Value switch
            {
                >= 7.75 => RegionKind.HighEntropyData,
                <= 2.00 => RegionKind.Padding,
                <= 5.00 => RegionKind.LowEntropyData,
                _ => RegionKind.Unknown
            };

            model.Regions.Add(new RegionModel
            {
                StartOffset = point.Offset,
                EndOffset = Math.Min(point.Offset + point.BlockSize, model.FileSize),
                Kind = kind,
                Entropy = point.Value,
                Confidence = GetEntropyConfidence(point.Value),
                Description = $"Entropy block: {point.Value:0.000}"
            });
        }
    }

    private static double GetEntropyConfidence(double entropy)
    {
        if (entropy >= 7.75)
            return 0.85;
        if (entropy <= 2.00)
            return 0.90;
        if (entropy <= 5.00)
            return 0.65;
        return 0.35;
    }
}
