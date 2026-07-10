using System.Buffers.Binary;
using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbVariableLengthRebuildServiceTests
{
    [Fact]
    public async Task RebuildSupportsLongerAndShorterText()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string source = Path.Combine(root, "source.landb");
        string translated = Path.Combine(root, "translated.txt");
        string output = Path.Combine(root, "rebuilt.landb");

        byte[] data = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 24);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14, 2), 5);
        Encoding.UTF8.GetBytes("Hello").CopyTo(data, 16);
        data[21] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22, 2), 5);
        Encoding.UTF8.GetBytes("World").CopyTo(data, 24);
        data[29] = 0;
        await File.WriteAllBytesAsync(source, data);
        await File.WriteAllTextAsync(translated, "Merhaba" + Environment.NewLine + "Dün" + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = new()
        {
            FullPath = source,
            FileName = "source.landb",
            FileSize = data.Length
        };

        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 16, ByteLength = 5, Text = "Hello" });
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 24, ByteLength = 5, Text = "World" });
        archive.Pointers.Add(new PointerHit { SourceOffset = 0, TargetOffset = 16, Size = 4, IsValid = true });
        archive.Pointers.Add(new PointerHit { SourceOffset = 4, TargetOffset = 24, Size = 4, IsValid = true });
        archive.Landb.LengthFieldCandidates.Add(new LandbLengthFieldCandidate { TextIndex = 0, FieldOffset = 14, FieldSize = 2, StoredValue = 5, Confidence = 0.95 });
        archive.Landb.LengthFieldCandidates.Add(new LandbLengthFieldCandidate { TextIndex = 1, FieldOffset = 22, FieldSize = 2, StoredValue = 5, Confidence = 0.95 });

        LandbVariableLengthRebuildResult result = await new LandbVariableLengthRebuildService()
            .RebuildAsync(archive, translated, output);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.True(File.Exists(output));

        byte[] rebuilt = await File.ReadAllBytesAsync(output);
        Assert.Equal(64, rebuilt.Length);
        Assert.Equal((uint)16, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(0, 4)));
        Assert.Equal((uint)26, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(4, 4)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(rebuilt.AsSpan(14, 2)));
        Assert.Equal((ushort)4, BinaryPrimitives.ReadUInt16LittleEndian(rebuilt.AsSpan(24, 2)));
        Assert.Equal("Merhaba", Encoding.UTF8.GetString(rebuilt, 16, 7));
        Assert.Equal("Dün", Encoding.UTF8.GetString(rebuilt, 26, 4));
    }
}
