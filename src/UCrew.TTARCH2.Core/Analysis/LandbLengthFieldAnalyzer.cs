using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class LandbLengthFieldAnalyzer : IAnalyzer
{
    public string Name => "LANDb Length Field Analyzer";

    public int Priority => 295;

    public int SearchWindow { get; init; } = 16;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        if (!context.Model.Landb.LooksLikeLandb || context.Model.Landb.TextCandidates.Count == 0)
            return Task.CompletedTask;

        var reader = context.Archive.Reader;

        for (int i = 0; i < context.Model.Landb.TextCandidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            LandbTextCandidate text = context.Model.Landb.TextCandidates[i];
            long start = Math.Max(0, text.Offset - SearchWindow);
            long end = text.Offset;

            for (long fieldOffset = start; fieldOffset + 2 <= end; fieldOffset++)
            {
                using var _ = reader.Bookmark();
                reader.Seek(fieldOffset);

                ushort value16 = reader.ReadUInt16();
                if (value16 == text.ByteLength)
                {
                    context.Model.Landb.LengthFieldCandidates.Add(new LandbLengthFieldCandidate
                    {
                        TextIndex = i,
                        FieldOffset = fieldOffset,
                        FieldSize = 2,
                        StoredValue = value16,
                        Encoding = "UInt16LE",
                        Confidence = fieldOffset == text.Offset - 2 ? 0.95 : 0.60
                    });
                }

                if (fieldOffset + 4 <= end)
                {
                    reader.Seek(fieldOffset);
                    uint value32 = reader.ReadUInt32();
                    if (value32 == text.ByteLength)
                    {
                        context.Model.Landb.LengthFieldCandidates.Add(new LandbLengthFieldCandidate
                        {
                            TextIndex = i,
                            FieldOffset = fieldOffset,
                            FieldSize = 4,
                            StoredValue = value32,
                            Encoding = "UInt32LE",
                            Confidence = fieldOffset == text.Offset - 4 ? 0.98 : 0.65
                        });
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
