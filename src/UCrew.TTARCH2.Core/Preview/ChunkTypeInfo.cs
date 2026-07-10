namespace UCrew.TTARCH2.Core.Preview;

public sealed class ChunkTypeInfo
{
    public string Name { get; init; } = "Unknown";

    public string Extension { get; init; } = ".bin";

    public string Category { get; init; } = "Binary";

    public double Confidence { get; init; }
}
