using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Preview;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class ChunkTypeDetectorTests
{
    [Theory]
    [InlineData("DDS ", "DDS Texture", ".dds")]
    [InlineData("OggS", "Ogg Audio", ".ogg")]
    [InlineData("RIFF", "RIFF Container", ".riff")]
    public async Task DetectsAsciiSignatures(string signature, string expectedName, string expectedExtension)
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string path = Path.Combine(root, "sample.bin");
        byte[] data = new byte[64];
        Encoding.ASCII.GetBytes(signature).CopyTo(data, 0);
        await File.WriteAllBytesAsync(path, data);

        ArchiveModel archive = new()
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
            FileSize = data.Length
        };

        ChunkModel chunk = new()
        {
            Index = 0,
            Offset = 0,
            EndOffset = data.Length
        };

        ChunkTypeInfo result = await new ChunkTypeDetector().DetectAsync(archive, chunk);

        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedExtension, result.Extension);
    }

    [Fact]
    public async Task DetectsPlainText()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string path = Path.Combine(root, "text.bin");
        byte[] data = Encoding.UTF8.GetBytes("Hello from UCrew TTARCH2 Studio. This is plain text.");
        await File.WriteAllBytesAsync(path, data);

        ArchiveModel archive = new()
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
            FileSize = data.Length
        };

        ChunkModel chunk = new()
        {
            Index = 0,
            Offset = 0,
            EndOffset = data.Length
        };

        ChunkTypeInfo result = await new ChunkTypeDetector().DetectAsync(archive, chunk);

        Assert.Equal("Text", result.Category);
        Assert.Equal(".txt", result.Extension);
    }
}
