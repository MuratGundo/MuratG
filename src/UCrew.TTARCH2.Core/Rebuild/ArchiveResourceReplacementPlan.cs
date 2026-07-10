using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveResourceReplacementPlan
{
    public required ArchiveResourceEntry Resource { get; init; }

    public string ReplacementPath { get; init; } = string.Empty;

    public long OriginalSize => Resource.Size;

    public long ReplacementSize { get; init; }

    public long Delta => ReplacementSize - OriginalSize;

    public bool RequiresArchiveRebuild => Delta != 0;

    public bool CanApply => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
