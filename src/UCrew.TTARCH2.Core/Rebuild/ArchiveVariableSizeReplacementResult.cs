namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveVariableSizeReplacementResult
{
    public bool Success => Errors.Count == 0;

    public string OutputPath { get; init; } = string.Empty;

    public long OriginalFileSize { get; init; }

    public long OutputFileSize { get; set; }

    public long SizeDelta => OutputFileSize - OriginalFileSize;

    public int UpdatedPointerCount { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
