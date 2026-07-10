using UCrew.TTARCH2.Core.Binary;

namespace UCrew.TTARCH2.Core;

public sealed class ArchiveContext : IDisposable
{
    public ArchiveContext(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Archive path is empty.", nameof(filePath));

        FilePath = Path.GetFullPath(filePath);
        Reader = new BinaryReaderX(File.OpenRead(FilePath));
    }

    public string FilePath { get; }

    public BinaryReaderX Reader { get; }

    public void Dispose() => Reader.Dispose();
}
