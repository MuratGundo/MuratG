using System.Text;

namespace UCrew.TTARCH2.Core.Analysis.Signatures;

public static class SignatureDatabase
{
    public static IReadOnlyList<BinarySignature> Default { get; } =
    [
        new BinarySignature
        {
            Name = "DDS",
            Category = "Texture",
            Pattern = Encoding.ASCII.GetBytes("DDS ")
        },
        new BinarySignature
        {
            Name = "PNG",
            Category = "Image",
            Pattern = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        },
        new BinarySignature
        {
            Name = "RIFF",
            Category = "Audio/Container",
            Pattern = Encoding.ASCII.GetBytes("RIFF")
        },
        new BinarySignature
        {
            Name = "OggS",
            Category = "Audio",
            Pattern = Encoding.ASCII.GetBytes("OggS")
        },
        new BinarySignature
        {
            Name = "ZLIB_78_9C",
            Category = "Compression",
            Pattern = [0x78, 0x9C]
        },
        new BinarySignature
        {
            Name = "ZLIB_78_DA",
            Category = "Compression",
            Pattern = [0x78, 0xDA]
        },
        new BinarySignature
        {
            Name = "LZ4_FRAME",
            Category = "Compression",
            Pattern = [0x04, 0x22, 0x4D, 0x18]
        },
        new BinarySignature
        {
            Name = "ZSTD",
            Category = "Compression",
            Pattern = [0x28, 0xB5, 0x2F, 0xFD]
        }
    ];
}
