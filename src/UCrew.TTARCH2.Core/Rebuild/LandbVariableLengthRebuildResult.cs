namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbVariableLengthRebuildResult
{
    public bool Success => Errors.Count == 0 && Plan.CanRebuild;

    public required LandbRebuildPlan Plan { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public long OutputFileSize { get; set; }

    public int UpdatedPointerCount { get; set; }

    public int UpdatedLengthFieldCount { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
