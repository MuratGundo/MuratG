namespace UCrew.TTARCH2.Core.Models;

public sealed class ArchiveCompressionBlock
{
    public int Index { get; init; }

    public long HeaderOffset { get; init; }

    public long DataOffset { get; init; }

    public long CompressedSize { get; init; }

    public long UncompressedSize { get; init; }

    public string Codec { get; init; } = "Unknown";

    public double Confidence { get; init; }
}
