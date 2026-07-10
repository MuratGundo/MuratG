namespace UCrew.TTARCH2.Core.Models;

public sealed class ArchiveTableField
{
    public int ResourceIndex { get; init; }

    public long FieldOffset { get; init; }

    public int FieldSize { get; init; }

    public long StoredValue { get; init; }

    public string Kind { get; init; } = string.Empty;

    public double Confidence { get; init; }
}
