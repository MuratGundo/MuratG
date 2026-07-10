using UCrew.TTARCH2.Core;
using UCrew.TTARCH2.Core.Analysis;

if (args.Length == 0)
{
    Console.WriteLine("Usage: UCrew.TTARCH2.CLI <archive-path>");
    return 1;
}

try
{
    ArchiveModel archive = ArchiveLoader.Open(args[0]);

    using ArchiveContext archiveContext = new(archive.FullPath);
    AnalysisContext analysisContext = new(archiveContext, archive);

    AnalyzerPipeline pipeline = new AnalyzerPipeline()
        .Register(new HeaderAnalyzer())
        .Register(new EntropyAnalyzer())
        .Register(new PointerScanner())
        .Register(new RegionDetector());

    await pipeline.ExecuteAsync(analysisContext);

    Console.WriteLine($"File      : {archive.FileName}");
    Console.WriteLine($"Size      : {archive.FileSize:n0} bytes");
    Console.WriteLine($"Magic     : {archive.Header.Magic}");
    Console.WriteLine($"Field0004 : 0x{archive.Header.Field0004:X8}");
    Console.WriteLine($"Field0008 : 0x{archive.Header.Field0008:X8}");
    Console.WriteLine($"Field000C : 0x{archive.Header.Field000C:X8}");
    Console.WriteLine($"Entropy   : {archive.EntropyMap.Count:n0} blocks");
    Console.WriteLine($"Pointers  : {archive.Pointers.Count:n0} candidates");
    Console.WriteLine($"Regions   : {archive.Regions.Count:n0} candidates");

    return archive.Validation.Success ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}
