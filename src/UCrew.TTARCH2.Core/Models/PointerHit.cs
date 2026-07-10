namespace UCrew.TTARCH2.Core.Models;

public sealed class PointerHit
{
    public long SourceOffset { get; init; }

    public long TargetOffset { get; init; }

    public int Size { get; init; }

    public bool IsValid { get; init; }

    public string Kind { get; init; } = "Unknown";

    public double Confidence { get; init; }
}
