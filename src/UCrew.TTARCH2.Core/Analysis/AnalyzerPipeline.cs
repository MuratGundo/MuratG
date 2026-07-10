namespace UCrew.TTARCH2.Core.Analysis;

public sealed class AnalyzerPipeline
{
    private readonly List<IAnalyzer> _analyzers = new();

    public AnalyzerPipeline Register(IAnalyzer analyzer)
    {
        _analyzers.Add(analyzer ?? throw new ArgumentNullException(nameof(analyzer)));
        return this;
    }

    public async Task ExecuteAsync(AnalysisContext context, CancellationToken token = default)
    {
        foreach (IAnalyzer analyzer in _analyzers.OrderBy(x => x.Priority))
        {
            token.ThrowIfCancellationRequested();
            await analyzer.AnalyzeAsync(context, token).ConfigureAwait(false);
        }
    }
}
