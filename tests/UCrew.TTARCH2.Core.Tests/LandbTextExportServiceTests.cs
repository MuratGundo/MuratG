using UCrew.TTARCH2.Core.Extraction;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbTextExportServiceTests
{
    [Fact]
    public async Task ExportWritesOnlyCleanTextLines()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string output = Path.Combine(root, "landb.txt");
        ArchiveModel archive = new();

        archive.Landb.TextCandidates.Add(new LandbTextCandidate
        {
            Offset = 0x120,
            ByteLength = 20,
            Text = "[stern]Yes, you did.",
            Confidence = 0.9
        });

        archive.Landb.TextCandidates.Add(new LandbTextCandidate
        {
            Offset = 0x180,
            ByteLength = 24,
            Text = "First line\r\nSecond line",
            Confidence = 0.8
        });

        await new LandbTextExportService().ExportAsync(archive, output);

        string text = await File.ReadAllTextAsync(output);

        Assert.Equal(
            "[stern]Yes, you did." + Environment.NewLine +
            "First line Second line" + Environment.NewLine,
            text);

        Assert.DoesNotContain("0x", text);
        Assert.DoesNotContain("\t", text);
    }
}
