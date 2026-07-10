namespace UCrew.TTARCH2.Core.Models;

public sealed class RegionModel
{
    public long StartOffset { get; init; }

    public long EndOffset { get; init; }

    public long Length => EndOffset - StartOffset;

    public RegionKind Kind { get; init; } = RegionKind.Unknown;

    public double Entropy { get; init; }

    public double Confidence { get; init; }

    public string Description { get; init; } = string.Empty;
}
