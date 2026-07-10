namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbRebuildPlan
{
    public bool CanRebuild => Errors.Count == 0;

    public long OriginalFileSize { get; init; }

    public long PlannedFileSize { get; set; }

    public long TotalDelta => PlannedFileSize - OriginalFileSize;

    public List<LandbRebuildEntry> Entries { get; } = new();

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}

public sealed class LandbRebuildEntry
{
    public int Index { get; init; }

    public long OriginalOffset { get; init; }

    public long NewOffset { get; init; }

    public int OriginalByteLength { get; init; }

    public int NewByteLength { get; init; }

    public long Delta => NewByteLength - OriginalByteLength;

    public string Text { get; init; } = string.Empty;

    public List<long> PointerSourceOffsets { get; } = new();
}
