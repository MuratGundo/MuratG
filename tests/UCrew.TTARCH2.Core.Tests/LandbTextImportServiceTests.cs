using System.Text;
using UCrew.TTARCH2.Core.Import;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbTextImportServiceTests
{
    [Fact]
    public async Task AcceptsMatchingNonEmptyLines()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "translated.txt");
        await File.WriteAllTextAsync(path, "[stern]Evet, yaptın." + Environment.NewLine + "Bu benim seçimimdi." + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = CreateArchive();
        LandbTextImportResult result = await new LandbTextImportService().ValidateAsync(archive, path);

        Assert.True(result.Success);
        Assert.Equal(2, result.ActualLineCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RejectsLineCountMismatchAndEmptyLine()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "translated.txt");
        await File.WriteAllTextAsync(path, Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = CreateArchive();
        LandbTextImportResult result = await new LandbTextImportService().ValidateAsync(archive, path);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, x => x.Contains("Line count mismatch", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("is empty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WarnsWhenProtectedTagIsMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "translated.txt");
        await File.WriteAllTextAsync(path, "Evet, yaptın." + Environment.NewLine + "Bu benim seçimimdi." + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = CreateArchive();
        LandbTextImportResult result = await new LandbTextImportService().ValidateAsync(archive, path);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, x => x.Contains("[stern]", StringComparison.Ordinal));
    }

    private static ArchiveModel CreateArchive()
    {
        ArchiveModel archive = new();
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Text = "[stern]Yes, you did." });
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Text = "That was my choice." });
        return archive;
    }
}
