namespace UCrew.TTARCH2.GUI;

public sealed class ArchiveFileRow
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string ExtractedPath { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public long Size { get; set; }

    public string State { get; set; } = "Orijinal";

    public bool IsLandb => Extension.Equals(".landb", StringComparison.OrdinalIgnoreCase);
}
