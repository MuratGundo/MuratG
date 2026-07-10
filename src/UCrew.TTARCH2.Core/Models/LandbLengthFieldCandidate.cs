namespace UCrew.TTARCH2.Core.Models;

public sealed class LandbLengthFieldCandidate
{
    public int TextIndex { get; init; }

    public long FieldOffset { get; init; }

    public int FieldSize { get; init; }

    public long StoredValue { get; init; }

    public string Encoding { get; init; } = "Unknown";

    public double Confidence { get; init; }
}
