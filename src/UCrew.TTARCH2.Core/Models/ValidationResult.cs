namespace UCrew.TTARCH2.Core.Models;

public sealed class ValidationResult
{
    public bool Success => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}
