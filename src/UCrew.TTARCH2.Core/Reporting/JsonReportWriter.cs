using System.Text.Json;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Reporting;

public sealed class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public async Task WriteAsync(ArchiveModel archive, string outputPath, CancellationToken token = default)
    {
        string json = JsonSerializer.Serialize(archive, Options);
        await File.WriteAllTextAsync(outputPath, json, token).ConfigureAwait(false);
    }
}
