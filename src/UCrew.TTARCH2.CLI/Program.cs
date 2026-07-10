using UCrew.TTARCH2.Core;
using UCrew.TTARCH2.Core.Analysis;
using UCrew.TTARCH2.Core.Extraction;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Reporting;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    string archivePath = args[0];
    string? reportPath = null;
    string? dumpDirectory = null;
    string? landbTextPath = null;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--report" when i + 1 < args.Length:
                reportPath = args[++i];
                break;

            case "--dump-chunks" when i + 1 < args.Length:
                dumpDirectory = args[++i];
                break;

            case "--export-landb-text" when i + 1 < args.Length:
                landbTextPath = args[++i];
                break;

            default:
                throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
        }
    }

    ArchiveModel archive = ArchiveLoader.Open(archivePath);

    using ArchiveContext archiveContext = new(archive.FullPath);
    AnalysisContext analysisContext = new(archiveContext, archive);

    AnalyzerPipeline pipeline = new AnalyzerPipeline()
        .Register(new HeaderAnalyzer())
        .Register(new EntropyAnalyzer())
        .Register(new PointerScanner())
        .Register(new SignatureScanner())
        .Register(new EcttAnalyzer())
        .Register(new LandbAnalyzer())
        .Register(new RegionDetector());

    await pipeline.ExecuteAsync(analysisContext);

    reportPath ??= Path.ChangeExtension(archive.FullPath, ".analysis.json");
    await new JsonReportWriter().WriteAsync(archive, reportPath);

    int dumpedChunks = 0;
    if (!string.IsNullOrWhiteSpace(dumpDirectory))
        dumpedChunks = await new ChunkDumpService().DumpAsync(archive, dumpDirectory);

    if (!string.IsNullOrWhiteSpace(landbTextPath))
        await new LandbTextExportService().ExportAsync(archive, landbTextPath);

    Console.WriteLine($"File       : {archive.FileName}");
    Console.WriteLine($"Size       : {archive.FileSize:n0} bytes");
    Console.WriteLine($"Magic      : {archive.Header.Magic}");
    Console.WriteLine($"Field0004  : 0x{archive.Header.Field0004:X8}");
    Console.WriteLine($"Field0008  : 0x{archive.Header.Field0008:X8}");
    Console.WriteLine($"Field000C  : 0x{archive.Header.Field000C:X8}");
    Console.WriteLine($"Entropy    : {archive.EntropyMap.Count:n0} blocks");
    Console.WriteLine($"Pointers   : {archive.Pointers.Count:n0} candidates");
    Console.WriteLine($"Signatures : {archive.Signatures.Count:n0} hits");
    Console.WriteLine($"ECTT       : {(archive.Ectt.LooksLikeEctt ? "yes" : "no")} / confidence {archive.Ectt.Confidence:0.00}");
    Console.WriteLine($"LANDb      : {(archive.Landb.LooksLikeLandb ? "yes" : "no")} / confidence {archive.Landb.Confidence:0.00}");
    Console.WriteLine($"LANDb Text : {archive.Landb.TextCandidates.Count:n0} candidates");
    Console.WriteLine($"Chunks     : {archive.Chunks.Count:n0} candidates");
    Console.WriteLine($"Regions    : {archive.Regions.Count:n0} candidates");
    Console.WriteLine($"Report     : {reportPath}");

    if (dumpDirectory is not null)
        Console.WriteLine($"Dumped     : {dumpedChunks:n0} chunks -> {Path.GetFullPath(dumpDirectory)}");

    if (landbTextPath is not null)
        Console.WriteLine($"LANDb TXT  : {Path.GetFullPath(landbTextPath)}");

    return archive.Validation.Success ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  UCrew.TTARCH2.CLI <archive-path> [--report <json-path>] [--dump-chunks <directory>] [--export-landb-text <txt-path>]");
}
