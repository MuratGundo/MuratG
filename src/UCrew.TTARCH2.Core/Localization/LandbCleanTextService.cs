using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace UCrew.TTARCH2.Core.Localization;

public sealed class LandbCleanTextService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8WithBom = new(true);
    private static readonly Regex FormatCodeRegex = new(@"\^\^|\^[^^]*\^", RegexOptions.Compiled);

    public async Task<int> ExportAsync(
        string landbPath,
        string txtPath,
        CancellationToken token = default)
    {
        ParsedLandb parsed = await ParseAsync(landbPath, token).ConfigureAwait(false);
        string output = string.Join('\n', parsed.Records.Select(record => EncodeTxtLine(RemoveFormatCodes(record.Text))));

        string fullOutput = Path.GetFullPath(txtPath);
        string? directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullOutput, output, Utf8WithBom, token).ConfigureAwait(false);
        return parsed.Records.Count;
    }

    public async Task<int> ImportAsync(
        string originalLandbPath,
        string translatedTxtPath,
        string outputLandbPath,
        CancellationToken token = default)
    {
        ParsedLandb parsed = await ParseAsync(originalLandbPath, token).ConfigureAwait(false);
        string[] translatedLines = await ReadTranslationLinesAsync(translatedTxtPath, token).ConfigureAwait(false);

        if (translatedLines.Length != parsed.Records.Count)
        {
            throw new InvalidDataException(
                $"TXT satır sayısı LANDb kayıt sayısıyla eşleşmiyor. " +
                $"LANDb: {parsed.Records.Count:N0}, TXT: {translatedLines.Length:N0}. " +
                "TXT dosyasına satır eklemeyin veya satır silmeyin.");
        }

        await using MemoryStream rebuilt = new(parsed.OriginalBytes.Length + 4096);
        long sourcePosition = 0;

        for (int index = 0; index < parsed.Records.Count; index++)
        {
            token.ThrowIfCancellationRequested();

            LandbTextRecord record = parsed.Records[index];
            string translated = DecodeTxtLine(translatedLines[index]);
            string restored = RestoreFormatCodes(record.Text, translated);
            byte[] restoredBytes = Encoding.UTF8.GetBytes(restored);

            await rebuilt.WriteAsync(
                parsed.OriginalBytes.AsMemory((int)sourcePosition, (int)(record.HeaderOffset - sourcePosition)),
                token).ConfigureAwait(false);

            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header[..4], checked((uint)(restoredBytes.Length + 8)));
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)restoredBytes.Length));
            await rebuilt.WriteAsync(header.ToArray(), token).ConfigureAwait(false);
            await rebuilt.WriteAsync(restoredBytes, token).ConfigureAwait(false);

            sourcePosition = record.EndOffset;
        }

        await rebuilt.WriteAsync(
            parsed.OriginalBytes.AsMemory((int)sourcePosition),
            token).ConfigureAwait(false);

        string fullOutput = Path.GetFullPath(outputLandbPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        await File.WriteAllBytesAsync(fullOutput, rebuilt.ToArray(), token).ConfigureAwait(false);

        try
        {
            ParsedLandb verification = await ParseAsync(fullOutput, token).ConfigureAwait(false);
            if (verification.Records.Count != parsed.Records.Count)
                throw new InvalidDataException("İçe aktarma sonrası LANDb kayıt sayısı doğrulanamadı.");
        }
        catch
        {
            if (File.Exists(fullOutput))
                File.Delete(fullOutput);
            throw;
        }

        return parsed.Records.Count;
    }

    public async Task<int> CountAsync(string landbPath, CancellationToken token = default)
    {
        ParsedLandb parsed = await ParseAsync(landbPath, token).ConfigureAwait(false);
        return parsed.Records.Count;
    }

    private static async Task<ParsedLandb> ParseAsync(string path, CancellationToken token)
    {
        byte[] data = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        List<LandbTextRecord> records = new();

        int position = 0;
        while (position + 8 <= data.Length)
        {
            uint totalSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position, 4));
            uint byteLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4, 4));

            bool valid = byteLength > 0
                && totalSize == byteLength + 8
                && byteLength <= int.MaxValue
                && position + 8L + byteLength <= data.Length;

            if (valid)
            {
                string text;
                try
                {
                    text = StrictUtf8.GetString(data, position + 8, (int)byteLength);
                }
                catch (DecoderFallbackException)
                {
                    text = string.Empty;
                }

                if (text.Length > 0 && IsReasonableText(text))
                {
                    records.Add(new LandbTextRecord(position, (int)totalSize, (int)byteLength, text));
                    position += (int)totalSize;
                    continue;
                }
            }

            position++;
        }

        if (records.Count == 0)
            throw new InvalidDataException("Desteklenen UTF-8 LANDb metin kaydı bulunamadı.");

        return new ParsedLandb(data, records);
    }

    private static bool IsReasonableText(string value)
    {
        if (value.IndexOf('\0') >= 0)
            return false;

        int printable = value.Count(character => !char.IsControl(character) || character is '\r' or '\n' or '\t');
        return printable >= Math.Ceiling(value.Length * 0.95);
    }

    private static string RemoveFormatCodes(string value) => FormatCodeRegex.Replace(value, string.Empty);

    private static string RestoreFormatCodes(string original, string translated)
    {
        MatchCollection matches = FormatCodeRegex.Matches(original);
        if (matches.Count == 0)
            return translated;

        string originalClean = RemoveFormatCodes(original);
        int originalVisibleLength = Math.Max(1, originalClean.Length);
        List<(int Position, string Code)> codes = new(matches.Count);
        int removedLength = 0;

        foreach (Match match in matches)
        {
            int visiblePosition = match.Index - removedLength;
            int translatedPosition = originalClean.Length == translated.Length
                ? Math.Min(visiblePosition, translated.Length)
                : (int)Math.Round((double)visiblePosition / originalVisibleLength * translated.Length);

            codes.Add((Math.Clamp(translatedPosition, 0, translated.Length), match.Value));
            removedLength += match.Length;
        }

        StringBuilder result = new(translated);
        foreach ((int position, string code) in codes.OrderByDescending(item => item.Position))
            result.Insert(position, code);

        return result.ToString();
    }

    private static string EncodeTxtLine(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string DecodeTxtLine(string value)
    {
        StringBuilder output = new(value.Length);

        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                char next = value[index + 1];
                if (next == 'n')
                {
                    output.Append('\n');
                    index++;
                    continue;
                }

                if (next == '\\')
                {
                    output.Append('\\');
                    index++;
                    continue;
                }
            }

            output.Append(value[index]);
        }

        return output.ToString();
    }

    private static async Task<string[]> ReadTranslationLinesAsync(string path, CancellationToken token)
    {
        string text = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        text = text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.EndsWith('\n'))
            text = text[..^1];
        return text.Split('\n');
    }

    private sealed record ParsedLandb(byte[] OriginalBytes, List<LandbTextRecord> Records);

    private sealed record LandbTextRecord(long HeaderOffset, int TotalSize, int ByteLength, string Text)
    {
        public long EndOffset => HeaderOffset + TotalSize;
    }
}
