namespace UCrew.TTARCH2.Core.Models;

public sealed class LandbTextCandidate
{
    public long Offset { get; init; }

    public int ByteLength { get; init; }

    public string Text { get; init; } = string.Empty;

    public double Confidence { get; init; }
}
