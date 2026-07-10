using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class LandbAnalyzer : IAnalyzer
{
    public string Name => "LANDb Analyzer";

    public int Priority => 290;

    public int MinimumTextLength { get; init; } = 4;

    public int MaximumCandidateLength { get; init; } = 4096;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ArchiveModel model = context.Model;
        string extension = Path.GetExtension(model.FileName);

        bool extensionMatch = extension.Equals(".landb", StringComparison.OrdinalIgnoreCase);
        bool magicMatch = model.Header.Magic.Contains("LAND", StringComparison.OrdinalIgnoreCase)
            || model.Header.Magic.Contains("LAN", StringComparison.OrdinalIgnoreCase);

        model.Landb.LooksLikeLandb = extensionMatch || magicMatch;

        if (!model.Landb.LooksLikeLandb)
        {
            model.Landb.Notes = "File name and header do not currently identify this as LANDb.";
            return Task.CompletedTask;
        }

        ScanNullTerminatedUtf8(context, token);

        int count = model.Landb.TextCandidates.Count;
        model.Landb.Confidence = count switch
        {
            >= 100 => 0.90,
            >= 20 => 0.75,
            >= 5 => 0.55,
            _ => extensionMatch ? 0.35 : 0.20
        };

        model.Landb.Notes = count > 0
            ? $"Found {count:N0} readable UTF-8 text candidates. Record structure is not yet confirmed."
            : "LANDb-like file detected, but no safe text candidates were found.";

        return Task.CompletedTask;
    }

    private void ScanNullTerminatedUtf8(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;
        reader.Seek(0);

        List<byte> current = new();
        long candidateStart = 0;

        while (!reader.EndOfFile)
        {
            token.ThrowIfCancellationRequested();

            long offset = reader.Position;
            byte value = reader.ReadByte();

            if (value == 0)
            {
                TryAddCandidate(context.Model, candidateStart, current);
                current.Clear();
                candidateStart = reader.Position;
                continue;
            }

            if (current.Count == 0)
                candidateStart = offset;

            if (current.Count >= MaximumCandidateLength)
            {
                current.Clear();
                candidateStart = reader.Position;
                continue;
            }

            current.Add(value);
        }

        TryAddCandidate(context.Model, candidateStart, current);
    }

    private void TryAddCandidate(ArchiveModel model, long offset, List<byte> bytes)
    {
        if (bytes.Count < MinimumTextLength)
            return;

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return;
        }

        if (!LooksReadable(text))
            return;

        model.Landb.TextCandidates.Add(new LandbTextCandidate
        {
            Offset = offset,
            ByteLength = bytes.Count,
            Text = text,
            Confidence = GetConfidence(text)
        });
    }

    private static bool LooksReadable(string text)
    {
        int printable = 0;
        int lettersOrDigits = 0;

        foreach (char c in text)
        {
            if (!char.IsControl(c) || c is '\r' or '\n' or '\t')
                printable++;

            if (char.IsLetterOrDigit(c))
                lettersOrDigits++;
        }

        return printable >= text.Length * 0.90
            && lettersOrDigits >= Math.Max(2, text.Length / 5);
    }

    private static double GetConfidence(string text)
    {
        double score = 0.45;

        if (text.Contains(' '))
            score += 0.15;
        if (text.Any(char.IsLetter))
            score += 0.15;
        if (text.Length >= 12)
            score += 0.10;
        if (text.Contains('[') && text.Contains(']'))
            score += 0.05;

        return Math.Min(0.90, score);
    }
}
