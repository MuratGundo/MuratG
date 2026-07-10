using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Import;

public sealed class LandbTextImportService
{
    public async Task<LandbTextImportResult> ValidateAsync(
        ArchiveModel archive,
        string translatedTextPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (string.IsNullOrWhiteSpace(translatedTextPath))
            throw new ArgumentException("Translated text path is empty.", nameof(translatedTextPath));

        if (!File.Exists(translatedTextPath))
            throw new FileNotFoundException("Translated text file was not found.", translatedTextPath);

        string[] lines = await File.ReadAllLinesAsync(translatedTextPath, Encoding.UTF8, token)
            .ConfigureAwait(false);

        LandbTextImportResult result = new()
        {
            ExpectedLineCount = archive.Landb.TextCandidates.Count,
            ActualLineCount = lines.Length
        };

        result.Lines.AddRange(lines);

        if (lines.Length != result.ExpectedLineCount)
        {
            result.Errors.Add(
                $"Line count mismatch. Expected {result.ExpectedLineCount:N0}, got {lines.Length:N0}.");
        }

        for (int i = 0; i < lines.Length; i++)
        {
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(lines[i]))
                result.Errors.Add($"Line {i + 1:N0} is empty.");

            if (lines[i].Contains('\r') || lines[i].Contains('\n'))
                result.Errors.Add($"Line {i + 1:N0} contains an embedded line break.");
        }

        int comparableCount = Math.Min(lines.Length, archive.Landb.TextCandidates.Count);
        for (int i = 0; i < comparableCount; i++)
        {
            string source = archive.Landb.TextCandidates[i].Text;
            string translated = lines[i];

            foreach (string tag in ExtractSquareBracketTags(source))
            {
                if (!translated.Contains(tag, StringComparison.Ordinal))
                {
                    result.Warnings.Add(
                        $"Line {i + 1:N0} may be missing protected tag {tag}.");
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> ExtractSquareBracketTags(string text)
    {
        int index = 0;

        while (index < text.Length)
        {
            int start = text.IndexOf('[', index);
            if (start < 0)
                yield break;

            int end = text.IndexOf(']', start + 1);
            if (end < 0)
                yield break;

            yield return text.Substring(start, end - start + 1);
            index = end + 1;
        }
    }
}
