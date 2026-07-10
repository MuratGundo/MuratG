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
            builder.Append("0x");
            builder.Append(candidate.Offset.ToString("X8"));
            builder.Append('\t');
            builder.Append(candidate.ByteLength);
            builder.Append('\t');
            builder.AppendLine(candidate.Text.Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false), token)
            .ConfigureAwait(false);
    }
}
