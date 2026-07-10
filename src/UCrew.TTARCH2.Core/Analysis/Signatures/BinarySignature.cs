namespace UCrew.TTARCH2.Core.Analysis.Signatures;

public sealed class BinarySignature
{
    public required string Name { get; init; }

    public required byte[] Pattern { get; init; }

    public int Alignment { get; init; } = 1;

    public string Category { get; init; } = "Unknown";
}
