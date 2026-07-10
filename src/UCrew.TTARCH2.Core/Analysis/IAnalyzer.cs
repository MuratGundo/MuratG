namespace UCrew.TTARCH2.Core.Analysis;

public interface IAnalyzer
{
    string Name { get; }

    int Priority { get; }

    Task AnalyzeAsync(AnalysisContext context, CancellationToken token);
}
