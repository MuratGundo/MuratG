using UCrew.TTARCH2.Core.Compatibility;
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
            .Register(new ArchiveTableFieldAnalyzer())
            .Register(new ArchiveCompressionBlockAnalyzer());

        await pipeline.ExecuteAsync(analysisContext, token).ConfigureAwait(false);

        bool isEcttTtarch2 = Path.GetExtension(archive.FullPath)
            .Equals(".ttarch2", StringComparison.OrdinalIgnoreCase)
            && archive.Header.Magic.Equals("ECTT", StringComparison.OrdinalIgnoreCase);

        if (isEcttTtarch2)
        {
            TtarchextBackendResult backend = await new TtarchextBackendService()
                .ExtractGuardiansArchiveAsync(archive.FullPath, token)
                .ConfigureAwait(false);

            if (backend.Success)
            {
                archive.Resources.Clear();
                archive.Resources.AddRange(backend.Resources);
            }
            else
            {
                foreach (string error in backend.Errors)
                    archive.Validation.Warnings.Add(error);
            }

            foreach (string warning in backend.Warnings)
                archive.Validation.Warnings.Add(warning);
        }

        return archive;
    }
}
