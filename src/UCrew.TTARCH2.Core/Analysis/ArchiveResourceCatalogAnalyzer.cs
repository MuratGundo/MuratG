using System.Text;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class ArchiveResourceCatalogAnalyzer : IAnalyzer
{
    public string Name => "Archive Resource Catalog Analyzer";

    public int Priority => 310;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        ArchiveModel model = context.Model;
        var reader = context.Archive.Reader;

        model.Resources.Clear();

        foreach (ChunkModel chunk in model.Chunks.OrderBy(x => x.Index))
        {
            token.ThrowIfCancellationRequested();

            if (chunk.Offset < 0 || chunk.EndOffset <= chunk.Offset || chunk.EndOffset > model.FileSize)
                continue;

            int probeLength = (int)Math.Min(32, chunk.Length);
            using var _ = reader.Bookmark();
            reader.Seek(chunk.Offset);
            byte[] probe = reader.ReadBytes(probeLength);

            string extension = DetectExtension(probe);
            string name = $"resource_{chunk.Index:D4}{extension}";
            double confidence = extension == ".landb" ? 0.75 : 0.30;

            model.Resources.Add(new ArchiveResourceEntry
            {
                Index = chunk.Index,
                Name = name,
                Extension = extension,
                Offset = chunk.Offset,
                Size = chunk.Length,
                Confidence = confidence,
                Source = "Chunk boundary + signature probe"
            });
        }

        return Task.CompletedTask;
    }

    private static string DetectExtension(ReadOnlySpan<byte> data)
    {
        if (StartsWithAscii(data, "ERTM") || StartsWithAscii(data, "LAND"))
            return ".landb";
        if (StartsWithAscii(data, "DDS "))
            return ".dds";
        if (StartsWithAscii(data, "OggS"))
            return ".ogg";
        if (StartsWithAscii(data, "RIFF"))
            return ".riff";

        return ".bin";
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string value)
    {
        byte[] expected = Encoding.ASCII.GetBytes(value);
        return data.Length >= expected.Length
            && data[..expected.Length].SequenceEqual(expected);
    }
}
