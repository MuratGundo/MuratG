namespace UCrew.TTARCH2.Core.Import;

public sealed class LandbTextImportResult
{
    public bool Success => Errors.Count == 0;

    public int ExpectedLineCount { get; init; }

    public int ActualLineCount { get; init; }

    public List<string> Lines { get; } = new();

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
