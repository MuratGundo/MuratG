using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Extraction;

public sealed class LandbTextExportService
{
    public async Task ExportAsync(
        ArchiveModel archive,
        string outputPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        StringBuilder builder = new();

        foreach (LandbTextCandidate candidate in archive.Landb.TextCandidates.OrderBy(x => x.Offset))
        {
            token.ThrowIfCancellationRequested();

            string cleanText = NormalizeSingleLine(candidate.Text);
            if (string.IsNullOrWhiteSpace(cleanText))
                continue;

            builder.AppendLine(cleanText);
        }

        await File.WriteAllTextAsync(
                outputPath,
                builder.ToString(),
                new UTF8Encoding(false),
                token)
            .ConfigureAwait(false);
    }

    private static string NormalizeSingleLine(string text)
    {
        return text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
