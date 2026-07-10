namespace UCrew.TTARCH2.Core.Models;

public sealed class ArchiveResourceEntry
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public long Offset { get; init; }

    public long Size { get; init; }

    public bool IsLandb => Extension.Equals(".landb", StringComparison.OrdinalIgnoreCase);

    public double Confidence { get; init; }

    public string Source { get; init; } = string.Empty;

    public string ExtractedPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public bool IsExtracted => !string.IsNullOrWhiteSpace(ExtractedPath) && File.Exists(ExtractedPath);
}
