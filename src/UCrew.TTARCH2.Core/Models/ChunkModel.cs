namespace UCrew.TTARCH2.Core.Models;

public sealed class ChunkModel
{
    public int Index { get; init; }

    public long Offset { get; init; }

    public long EndOffset { get; init; }

    public long Length => EndOffset - Offset;

    public double Entropy { get; init; }

    public string Source { get; init; } = "Unknown";

    public double Confidence { get; init; }
}
