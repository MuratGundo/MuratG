using System.Buffers.Binary;
using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class ArchiveVariableSizeReplacementServiceTests
{
    [Fact]
    public async Task ReplacesResourceWithLargerFileAndUpdatesTableFields()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.ttarch2");
        string replacement = Path.Combine(root, "new.landb");
        string output = Path.Combine(root, "output.ttarch2");

        byte[] archiveBytes = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan(0, 4), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan(4, 4), 7);
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

        ArchiveResourceEntry following = new()
        {
            Index = 1,
            Name = "resource_0001.bin",
            Extension = ".bin",
            Offset = 32,
            Size = 9,
            Confidence = 1.0
        };

        archive.Resources.Add(resource);
        archive.Resources.Add(following);
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 0,
            FieldOffset = 4,
            FieldSize = 4,
            StoredValue = 7,
            Kind = "Size",
            Confidence = 0.95
        });
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 1,
            FieldOffset = 0,
            FieldSize = 4,
            StoredValue = 32,
            Kind = "Offset",
            Confidence = 0.95
        });

        ArchiveVariableSizeReplacementResult result = await new ArchiveVariableSizeReplacementService()
            .ReplaceToCopyAsync(archive, resource, replacement, output);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        byte[] rebuilt = await File.ReadAllBytesAsync(output);
        Assert.Equal(71, rebuilt.Length);
        Assert.Equal((uint)39, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(0, 4)));
        Assert.Equal((uint)14, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(4, 4)));
        Assert.Equal("NEW-LANDB-DATA", Encoding.ASCII.GetString(rebuilt, 16, 14));
        Assert.Equal(1, result.UpdatedOffsetFieldCount);
        Assert.Equal(1, result.UpdatedSizeFieldCount);
    }

    [Fact]
    public async Task RejectsVariableSizeWhenNoSizeFieldIsKnown()
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
        Assert.Contains(result.Errors, x => x.Contains("size field", StringComparison.OrdinalIgnoreCase));
    }
}
