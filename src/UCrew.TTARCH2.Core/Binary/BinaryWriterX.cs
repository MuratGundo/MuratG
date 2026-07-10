using System.Buffers.Binary;
using System.Text;

namespace UCrew.TTARCH2.Core.Binary;

public sealed class BinaryWriterX : IDisposable
{
    private readonly Stream _stream;
    private readonly Endian _endian;
    private readonly byte[] _buffer8 = new byte[8];

    public BinaryWriterX(Stream stream, Endian endian = Endian.Little, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _endian = endian;
        LeaveOpen = leaveOpen;
    }

    public bool LeaveOpen { get; }

    public long Position => _stream.Position;

    public long Length => _stream.Length;

    public void Seek(long position) => _stream.Seek(position, SeekOrigin.Begin);

    public void Write(byte value) => _stream.WriteByte(value);

    public void WriteBytes(ReadOnlySpan<byte> data) => _stream.Write(data);

    public void WriteUInt16(ushort value)
    {
        if (_endian == Endian.Little)
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer8, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(_buffer8, value);
        _stream.Write(_buffer8, 0, 2);
    }

    public void WriteUInt32(uint value)
    {
        if (_endian == Endian.Little)
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer8, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(_buffer8, value);
        _stream.Write(_buffer8, 0, 4);
    }

    public void WriteUInt64(ulong value)
    {
        if (_endian == Endian.Little)
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer8, value);
        else
            BinaryPrimitives.WriteUInt64BigEndian(_buffer8, value);
        _stream.Write(_buffer8, 0, 8);
    }

    public void WriteFixedString(string text, Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;
        WriteBytes(encoding.GetBytes(text));
    }

    public void Align(int alignment, byte fill = 0)
    {
        if (alignment <= 1)
            return;

        long next = ((Position + alignment - 1) / alignment) * alignment;
        while (Position < next)
            Write(fill);
    }

    public void Dispose()
    {
        if (!LeaveOpen)
            _stream.Dispose();
    }
}
