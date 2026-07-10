using System.Text.Json;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Reporting;

public sealed class LandbVerificationReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public async Task WriteAsync(
        LandbPostBuildVerificationResult verification,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(verification);

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        string json = JsonSerializer.Serialize(verification, Options);
        await File.WriteAllTextAsync(outputPath, json, token).ConfigureAwait(false);
    }
}
