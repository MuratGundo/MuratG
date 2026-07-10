using System.Buffers.Binary;
using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class ArchiveVariableSizeReplacementServiceTests
{
    [Fact]
    public async Task ReplacesResourceWithLargerFileAndMovesFollowingPointer()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.ttarch2");
        string replacement = Path.Combine(root, "new.landb");
        string output = Path.Combine(root, "output.ttarch2");

        byte[] archiveBytes = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan(0, 4), 32);
        Encoding.ASCII.GetBytes("OLDLAND").CopyTo(archiveBytes, 16);
        Encoding.ASCII.GetBytes("TAIL-DATA").CopyTo(archiveBytes, 32);
        await File.WriteAllBytesAsync(source, archiveBytes);
        await File.WriteAllBytesAsync(replacement, Encoding.ASCII.GetBytes("NEW-LANDB-DATA"));

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = "source.ttarch2",
            FileSize = archiveBytes.Length
        };

        ArchiveResourceEntry resource = new()
        {
            Index = 0,
            Name = "resource_0000.landb",
            Extension = ".landb",
            Offset = 16,
            Size = 7,
            Confidence = 1.0
        };

        archive.Pointers.Add(new PointerHit
        {
            SourceOffset = 0,
            TargetOffset = 32,
            Size = 4,
            IsValid = true
        });

        ArchiveVariableSizeReplacementResult result = await new ArchiveVariableSizeReplacementService()
            .ReplaceToCopyAsync(archive, resource, replacement, output);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        byte[] rebuilt = await File.ReadAllBytesAsync(output);
        Assert.Equal(55, rebuilt.Length);
        Assert.Equal((uint)39, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(0, 4)));
        Assert.Equal("NEW-LANDB-DATA", Encoding.ASCII.GetString(rebuilt, 16, 14));
    }

    [Fact]
    public async Task RejectsVariableSizeWhenNoFollowingPointersAreKnown()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.ttarch2");
        string replacement = Path.Combine(root, "new.landb");
        string output = Path.Combine(root, "output.ttarch2");

        await File.WriteAllBytesAsync(source, new byte[32]);
        await File.WriteAllBytesAsync(replacement, new byte[12]);

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = "source.ttarch2",
            FileSize = 32
        };

        ArchiveResourceEntry resource = new()
        {
            Index = 0,
            Name = "resource_0000.landb",
            Extension = ".landb",
            Offset = 8,
            Size = 4,
            Confidence = 1.0
        };

        ArchiveVariableSizeReplacementResult result = await new ArchiveVariableSizeReplacementService()
            .ReplaceToCopyAsync(archive, resource, replacement, output);

        Assert.False(result.Success);
        Assert.False(File.Exists(output));
        Assert.Contains(result.Errors, x => x.Contains("no pointer", StringComparison.OrdinalIgnoreCase));
    }
}
