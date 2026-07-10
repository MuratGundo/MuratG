using System.Text;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class HeaderAnalyzer : IAnalyzer
{
    public string Name => "Header Analyzer";

    public int Priority => 0;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;
        var header = context.Model.Header;

        reader.Seek(0);

        header.Magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        header.Field0004 = reader.ReadUInt32();
        header.Field0008 = reader.ReadUInt32();
        header.Field000C = reader.ReadUInt32();
        header.Field0010 = reader.ReadUInt32();
        header.Field0014 = reader.ReadUInt32();
        header.Field0018 = reader.ReadUInt32();
        header.Field001C = reader.ReadUInt32();
        header.HeaderLength = reader.Position;

        if (string.IsNullOrWhiteSpace(header.Magic))
            context.Model.Validation.Warnings.Add("Header magic is empty.");

        return Task.CompletedTask;
    }
}
