using UCrew.TTARCH2.Core.Binary;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class BinaryReaderWriterTests
{
    [Fact]
    public void LittleEndianValuesRoundTrip()
    {
        using MemoryStream stream = new();

        using (BinaryWriterX writer = new(stream, Endian.Little, leaveOpen: true))
        {
            writer.WriteUInt16(0x1234);
            writer.WriteUInt32(0x89ABCDEF);
            writer.WriteUInt64(0x0123456789ABCDEF);
        }

        stream.Position = 0;

        using BinaryReaderX reader = new(stream, Endian.Little, leaveOpen: true);
        Assert.Equal((ushort)0x1234, reader.ReadUInt16());
        Assert.Equal(0x89ABCDEFu, reader.ReadUInt32());
        Assert.Equal(0x0123456789ABCDEFul, reader.ReadUInt64());
    }

    [Fact]
    public void BookmarkRestoresOriginalPosition()
    {
        using MemoryStream stream = new([1, 2, 3, 4, 5]);
        using BinaryReaderX reader = new(stream, leaveOpen: true);

        reader.Seek(2);

        using (reader.Bookmark())
        {
            reader.Seek(4);
            Assert.Equal((byte)5, reader.ReadByte());
        }

        Assert.Equal(2, reader.Position);
    }
}
