using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace UCrew.TTARCH2.Core.Binary;

public sealed class BinaryReaderX : IDisposable
{
    private readonly Stream _stream;
    private readonly Endian _endian;
    private readonly byte[] _buffer8 = new byte[8];
    private readonly Stack<long> _bookmarks = new();

    public BinaryReaderX(Stream stream, Endian endian = Endian.Little, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _endian = endian;
        LeaveOpen = leaveOpen;
    }

    public bool LeaveOpen { get; }

    public long Position => _stream.Position;

    public long Length => _stream.Length;

    public long Remaining => Length - Position;

    public bool EndOfFile => Position >= Length;

    public void Seek(long position)
    {
        if (position < 0 || position > Length)
            throw new ArgumentOutOfRangeException(nameof(position), position, "Seek position is outside the stream.");

        _stream.Seek(position, SeekOrigin.Begin);
    }

    public void Skip(long count) => Seek(Position + count);

    public void Align(int alignment)
    {
        if (alignment <= 1)
            return;

        long next = ((Position + alignment - 1) / alignment) * alignment;
        Seek(next);
    }

    public void PushPosition() => _bookmarks.Push(Position);

    public void PopPosition()
    {
        if (_bookmarks.Count == 0)
            throw new InvalidOperationException("No bookmark exists.");

        Seek(_bookmarks.Pop());
    }

    public IDisposable Bookmark()
    {
        PushPosition();
        return new PositionScope(this);
    }

    public int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

    public byte ReadByte()
    {
        int value = _stream.ReadByte();
        if (value < 0)
            throw new EndOfStreamException();
        return (byte)value;
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        byte[] buffer = new byte[count];
        int total = 0;

        while (total < count)
        {
            int read = _stream.Read(buffer, total, count - total);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }

        return buffer;
    }

    public ushort ReadUInt16()
    {
        Fill(2);
        return _endian == Endian.Little
            ? BinaryPrimitives.ReadUInt16LittleEndian(_buffer8)
            : BinaryPrimitives.ReadUInt16BigEndian(_buffer8);
    }

    public uint ReadUInt32()
    {
        Fill(4);
        return _endian == Endian.Little
            ? BinaryPrimitives.ReadUInt32LittleEndian(_buffer8)
            : BinaryPrimitives.ReadUInt32BigEndian(_buffer8);
    }

    public ulong ReadUInt64()
    {
        Fill(8);
        return _endian == Endian.Little
            ? BinaryPrimitives.ReadUInt64LittleEndian(_buffer8)
            : BinaryPrimitives.ReadUInt64BigEndian(_buffer8);
    }

    public short ReadInt16() => unchecked((short)ReadUInt16());

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public long ReadInt64() => unchecked((long)ReadUInt64());

    public string ReadFixedString(int length, Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;
        return encoding.GetString(ReadBytes(length));
    }

    public string ReadCString(Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        List<byte> bytes = new();

        while (!EndOfFile)
        {
            byte b = ReadByte();
            if (b == 0)
                break;
            bytes.Add(b);
        }

        return encoding.GetString(bytes.ToArray());
    }

    public uint PeekUInt32()
    {
        using var _ = Bookmark();
        return ReadUInt32();
    }

    public T ReadStruct<T>() where T : unmanaged
    {
        byte[] data = ReadBytes(Marshal.SizeOf<T>());
        return MemoryMarshal.Read<T>(data);
    }

    private void Fill(int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = _stream.Read(_buffer8, total, count - total);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
    }

    public void Dispose()
    {
        if (!LeaveOpen)
            _stream.Dispose();
    }

    private sealed class PositionScope : IDisposable
    {
        private readonly BinaryReaderX _reader;
        private bool _disposed;

        public PositionScope(BinaryReaderX reader) => _reader = reader;

        public void Dispose()
        {
            if (_disposed)
                return;
            _reader.PopPosition();
            _disposed = true;
        }
    }
}
