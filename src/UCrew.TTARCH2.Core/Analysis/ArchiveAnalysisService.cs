using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class ArchiveAnalysisService
{
    public async Task<ArchiveModel> AnalyzeAsync(string filePath, CancellationToken token = default)
    {
        ArchiveModel archive = UCrew.TTARCH2.Core.ArchiveLoader.Open(filePath);

        using UCrew.TTARCH2.Core.ArchiveContext archiveContext = new(archive.FullPath);
        AnalysisContext analysisContext = new(archiveContext, archive);

        AnalyzerPipeline pipeline = new AnalyzerPipeline()
            .Register(new HeaderAnalyzer())
            .Register(new EntropyAnalyzer())
            .Register(new PointerScanner())
            .Register(new SignatureScanner())
            .Register(new EcttAnalyzer())
            .Register(new LandbAnalyzer())
            .Register(new LandbLengthFieldAnalyzer())
            .Register(new RegionDetector())
            .Register(new ArchiveResourceCatalogAnalyzer())
            .Register(new ArchiveTableFieldAnalyzer());

        await pipeline.ExecuteAsync(analysisContext, token).ConfigureAwait(false);
        return archive;
    }
}
