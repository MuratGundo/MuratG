using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Compatibility;

public sealed class TtarchextBackendResult
{
    public bool Success => Errors.Count == 0;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string ToolPath { get; init; } = string.Empty;

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public List<ArchiveResourceEntry> Resources { get; } = new();

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
