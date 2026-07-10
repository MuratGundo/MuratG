namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbPostBuildVerificationResult
{
    public bool Success => Errors.Count == 0;

    public int ExpectedTextCount { get; init; }

    public int VerifiedTextCount { get; set; }

    public long ExpectedFileSize { get; init; }

    public long ActualFileSize { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
