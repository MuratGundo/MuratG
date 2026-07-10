namespace UCrew.TTARCH2.Core.Models;

public sealed class SignatureHit
{
    public required string Name { get; init; }

    public required string Category { get; init; }

    public long Offset { get; init; }

    public int Length { get; init; }

    public double Confidence { get; init; }
}
