using System.Buffers.Binary;
using System.Text;
using UCrew.TTARCH2.Core.Localization;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbCleanTextServiceTests
{
    [Fact]
    public async Task ExportAndImportWithoutEditsProducesExactOriginal()
    {
        string root = CreateTempDirectory();
        string original = Path.Combine(root, "sample.landb");
        string txt = Path.Combine(root, "sample.clean.txt");
        string rebuilt = Path.Combine(root, "sample.rebuilt.landb");

        await File.WriteAllBytesAsync(original, CreateLandb(
            "^glyphScale 0.8^Hello^^",
            "Second line\ncontinues"));

        LandbCleanTextService service = new();
        int exported = await service.ExportAsync(original, txt);
        int imported = await service.ImportAsync(original, txt, rebuilt);

        Assert.Equal(2, exported);
        Assert.Equal(2, imported);
        Assert.Equal(await File.ReadAllBytesAsync(original), await File.ReadAllBytesAsync(rebuilt));

        string cleanText = await File.ReadAllTextAsync(txt);
        Assert.DoesNotContain("glyphScale", cleanText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", cleanText, StringComparison.Ordinal);
        Assert.Contains("Second line\\ncontinues", cleanText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAcceptsLongerTurkishTextAndPreservesCodes()
    {
        string root = CreateTempDirectory();
        string original = Path.Combine(root, "sample.landb");
        string txt = Path.Combine(root, "translated.txt");
        string rebuilt = Path.Combine(root, "translated.landb");

        await File.WriteAllBytesAsync(original, CreateLandb("^color FF00FF^Hello^^"));
        await File.WriteAllTextAsync(txt, "Bu çok daha uzun bir Türkçe çeviri metnidir.", new UTF8Encoding(true));

        LandbCleanTextService service = new();
        int imported = await service.ImportAsync(original, txt, rebuilt);

        Assert.Equal(1, imported);
        Assert.True(new FileInfo(rebuilt).Length > new FileInfo(original).Length);

        byte[] output = await File.ReadAllBytesAsync(rebuilt);
        string outputText = Encoding.UTF8.GetString(output);
        Assert.Contains("^color FF00FF^", outputText, StringComparison.Ordinal);
        Assert.Contains("Türkçe çeviri", outputText, StringComparison.Ordinal);
    }

    private static byte[] CreateLandb(params string[] texts)
    {
        using MemoryStream output = new();
        output.Write(new byte[] { 0x4C, 0x41, 0x4E, 0x44 });

        foreach (string text in texts)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), (uint)(bytes.Length + 8));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)bytes.Length);
            output.Write(header);
            output.Write(bytes);
        }

        output.Write(new byte[] { 0x00, 0x10, 0x20, 0x30 });
        return output.ToArray();
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ucrew-landb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
