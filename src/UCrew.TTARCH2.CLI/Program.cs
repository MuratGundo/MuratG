using UCrew.TTARCH2.Core;
using UCrew.TTARCH2.Core.Analysis;
using UCrew.TTARCH2.Core.Extraction;
using UCrew.TTARCH2.Core.Import;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;
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
    string? validateLandbTextPath = null;
    string? importLandbTextPath = null;
    string? importLandbOutputPath = null;
    string? rebuildLandbTextPath = null;
    string? rebuildLandbOutputPath = null;

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

            case "--validate-landb-text" when i + 1 < args.Length:
                validateLandbTextPath = args[++i];
                break;

            case "--import-landb-text" when i + 2 < args.Length:
                importLandbTextPath = args[++i];
                importLandbOutputPath = args[++i];
                break;

            case "--rebuild-landb-text" when i + 2 < args.Length:
                rebuildLandbTextPath = args[++i];
                rebuildLandbOutputPath = args[++i];
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
        .Register(new LandbLengthFieldAnalyzer())
        .Register(new RegionDetector());

    await pipeline.ExecuteAsync(analysisContext);

    reportPath ??= Path.ChangeExtension(archive.FullPath, ".analysis.json");
    await new JsonReportWriter().WriteAsync(archive, reportPath);

    int dumpedChunks = 0;
    if (!string.IsNullOrWhiteSpace(dumpDirectory))
        dumpedChunks = await new ChunkDumpService().DumpAsync(archive, dumpDirectory);

    if (!string.IsNullOrWhiteSpace(landbTextPath))
        await new LandbTextExportService().ExportAsync(archive, landbTextPath);

    LandbTextImportResult? validation = null;
    if (!string.IsNullOrWhiteSpace(validateLandbTextPath))
        validation = await new LandbTextImportService().ValidateAsync(archive, validateLandbTextPath);

    LandbTextImportResult? importResult = null;
    if (!string.IsNullOrWhiteSpace(importLandbTextPath) && !string.IsNullOrWhiteSpace(importLandbOutputPath))
    {
        importResult = await new LandbFixedSizeImportService()
            .ImportToCopyAsync(archive, importLandbTextPath, importLandbOutputPath);
    }

    LandbVariableLengthRebuildResult? rebuildResult = null;
    if (!string.IsNullOrWhiteSpace(rebuildLandbTextPath) && !string.IsNullOrWhiteSpace(rebuildLandbOutputPath))
    {
        rebuildResult = await new LandbVariableLengthRebuildService()
            .RebuildAsync(archive, rebuildLandbTextPath, rebuildLandbOutputPath);
    }

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
    Console.WriteLine($"Length Fld : {archive.Landb.LengthFieldCandidates.Count:n0} candidates");
    Console.WriteLine($"Chunks     : {archive.Chunks.Count:n0} candidates");
    Console.WriteLine($"Regions    : {archive.Regions.Count:n0} candidates");
    Console.WriteLine($"Report     : {reportPath}");

    if (dumpDirectory is not null)
        Console.WriteLine($"Dumped     : {dumpedChunks:n0} chunks -> {Path.GetFullPath(dumpDirectory)}");

    if (landbTextPath is not null)
        Console.WriteLine($"LANDb TXT  : {Path.GetFullPath(landbTextPath)}");

    if (validation is not null)
    {
        Console.WriteLine($"TXT Check  : {(validation.Success ? "PASS" : "FAIL")}");
        Console.WriteLine($"Lines      : {validation.ActualLineCount:n0} / {validation.ExpectedLineCount:n0}");
        foreach (string error in validation.Errors)
            Console.Error.WriteLine($"ERROR: {error}");
        foreach (string warning in validation.Warnings)
            Console.WriteLine($"WARNING: {warning}");
    }

    if (importResult is not null)
    {
        Console.WriteLine($"LANDb Import: {(importResult.Success ? "PASS" : "FAIL")}");
        Console.WriteLine($"Import Lines: {importResult.ActualLineCount:n0} / {importResult.ExpectedLineCount:n0}");
        foreach (string error in importResult.Errors)
            Console.Error.WriteLine($"ERROR: {error}");
        foreach (string warning in importResult.Warnings)
            Console.WriteLine($"WARNING: {warning}");
        if (importResult.Success && importLandbOutputPath is not null)
            Console.WriteLine($"Output LANDb: {Path.GetFullPath(importLandbOutputPath)}");
    }

    if (rebuildResult is not null)
    {
        Console.WriteLine($"LANDb Rebuild: {(rebuildResult.Success ? "PASS" : "FAIL")}");
        Console.WriteLine($"Planned Size : {rebuildResult.Plan.PlannedFileSize:n0}");
        Console.WriteLine($"Output Size  : {rebuildResult.OutputFileSize:n0}");
        Console.WriteLine($"Pointers     : {rebuildResult.UpdatedPointerCount:n0} updated");
        Console.WriteLine($"Length Fields: {rebuildResult.UpdatedLengthFieldCount:n0} updated");
        foreach (string error in rebuildResult.Errors)
            Console.Error.WriteLine($"ERROR: {error}");
        foreach (string warning in rebuildResult.Warnings)
            Console.WriteLine($"WARNING: {warning}");
        if (rebuildResult.Success)
            Console.WriteLine($"Output LANDb : {rebuildResult.OutputPath}");
    }

    if (validation is { Success: false }
        || importResult is { Success: false }
        || rebuildResult is { Success: false })
    {
        return 4;
    }

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
    Console.WriteLine("  UCrew.TTARCH2.CLI <archive-path> [--report <json-path>] [--dump-chunks <directory>] [--export-landb-text <txt-path>] [--validate-landb-text <translated-txt-path>] [--import-landb-text <translated-txt-path> <output-landb-path>] [--rebuild-landb-text <translated-txt-path> <output-landb-path>]");
}
