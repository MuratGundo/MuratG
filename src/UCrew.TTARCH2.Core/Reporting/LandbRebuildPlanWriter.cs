using System.Text.Json;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Reporting;

public sealed class LandbRebuildPlanWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public async Task WriteAsync(
        LandbRebuildPlan plan,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        string json = JsonSerializer.Serialize(plan, Options);
        await File.WriteAllTextAsync(outputPath, json, token).ConfigureAwait(false);
    }
}
