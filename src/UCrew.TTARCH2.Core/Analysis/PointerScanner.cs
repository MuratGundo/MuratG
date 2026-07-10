using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class PointerScanner : IAnalyzer
{
    public string Name => "Pointer Scanner";

    public int Priority => 200;

    public int Alignment { get; init; } = 4;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ScanUInt32(context, token);
        ScanUInt64(context, token);
        return Task.CompletedTask;
    }

    private void ScanUInt32(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;
        long fileSize = reader.Length;

        reader.Seek(0);

        while (reader.Remaining >= 4)
        {
            token.ThrowIfCancellationRequested();

            long source = reader.Position;
            uint target = reader.ReadUInt32();

            if (IsValidOffset(target, fileSize))
            {
                context.Model.Pointers.Add(new PointerHit
                {
                    SourceOffset = source,
                    TargetOffset = target,
                    Size = 4,
                    IsValid = true,
                    Kind = "DWORD",
                    Confidence = GetConfidence(target, fileSize)
                });
            }

            long next = source + Math.Max(1, Alignment);
            if (next <= source)
                break;
            reader.Seek(Math.Min(next, reader.Length));
        }
    }

    private static void ScanUInt64(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;
        long fileSize = reader.Length;

        reader.Seek(0);

        while (reader.Remaining >= 8)
        {
            token.ThrowIfCancellationRequested();

            long source = reader.Position;
            ulong value = reader.ReadUInt64();

            if (value <= long.MaxValue && IsValidOffset((long)value, fileSize))
            {
                context.Model.Pointers.Add(new PointerHit
                {
                    SourceOffset = source,
                    TargetOffset = (long)value,
                    Size = 8,
                    IsValid = true,
                    Kind = "QWORD",
                    Confidence = GetConfidence((long)value, fileSize)
                });
            }

            reader.Seek(Math.Min(source + 8, reader.Length));
        }
    }

    private static bool IsValidOffset(long value, long fileSize)
    {
        return value > 0 && value < fileSize;
    }

    private static double GetConfidence(long value, long fileSize)
    {
        if (value <= 0 || value >= fileSize)
            return 0;

        double normalized = (double)value / fileSize;
        return normalized is > 0.01 and < 0.99 ? 0.75 : 0.45;
    }
}
