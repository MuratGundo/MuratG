using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbFixedSizeImportServiceTests
{
    [Fact]
    public async Task ImportCreatesCopyAndPreservesOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.landb");
        string translated = Path.Combine(root, "translated.txt");
        string output = Path.Combine(root, "patched.landb");

        byte[] original = Encoding.UTF8.GetBytes("HEADHello\0World\0TAIL");
        await File.WriteAllBytesAsync(source, original);
        await File.WriteAllTextAsync(translated, "Selam" + Environment.NewLine + "Dunya" + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = "source.landb",
            FileSize = original.Length
        };

        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 4, ByteLength = 5, Text = "Hello" });
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 10, ByteLength = 5, Text = "World" });

        var result = await new LandbFixedSizeImportService().ImportToCopyAsync(archive, translated, output);

        Assert.True(result.Success);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal("HEADSelam\0Dunya\0TAIL", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(output)));
    }

    [Fact]
    public async Task ImportRejectsDifferentUtf8ByteLength()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.landb");
        string translated = Path.Combine(root, "translated.txt");
        string output = Path.Combine(root, "patched.landb");

        byte[] original = Encoding.UTF8.GetBytes("HEADHello\0TAIL");
        await File.WriteAllBytesAsync(source, original);
        await File.WriteAllTextAsync(translated, "Merhaba" + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = "source.landb",
            FileSize = original.Length
        };

        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 4, ByteLength = 5, Text = "Hello" });

        var result = await new LandbFixedSizeImportService().ImportToCopyAsync(archive, translated, output);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, x => x.Contains("byte size mismatch", StringComparison.Ordinal));
        Assert.False(File.Exists(output));
    }
}
