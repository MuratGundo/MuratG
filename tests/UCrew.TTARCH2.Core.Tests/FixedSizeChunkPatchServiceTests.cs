using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class FixedSizeChunkPatchServiceTests
{
    [Fact]
    public async Task PatchCreatesCopyAndPreservesOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.ttarch2");
        string output = Path.Combine(root, "patched.ttarch2");
        byte[] original = Encoding.ASCII.GetBytes("HEADabcdefghTAIL");
        await File.WriteAllBytesAsync(source, original);

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = Path.GetFileName(source),
            FileSize = original.Length
        };

        ChunkModel chunk = new()
        {
            Index = 0,
            Offset = 4,
            EndOffset = 12
        };

        await new FixedSizeChunkPatchService().PatchBytesToCopyAsync(
            archive,
            chunk,
            Encoding.ASCII.GetBytes("12345678"),
            output);

        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal("HEAD12345678TAIL", Encoding.ASCII.GetString(await File.ReadAllBytesAsync(output)));
    }

    [Fact]
    public async Task PatchRejectsDifferentReplacementSize()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.ttarch2");
        await File.WriteAllBytesAsync(source, new byte[16]);

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = Path.GetFileName(source),
            FileSize = 16
        };

        ChunkModel chunk = new()
        {
            Index = 0,
            Offset = 4,
            EndOffset = 12
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new FixedSizeChunkPatchService().PatchBytesToCopyAsync(
                archive,
                chunk,
                new byte[7],
                Path.Combine(root, "patched.ttarch2")));
    }
}
