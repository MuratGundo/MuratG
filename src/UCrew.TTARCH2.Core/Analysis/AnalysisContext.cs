using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class AnalysisContext
{
    public AnalysisContext(ArchiveContext archive, ArchiveModel model)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ArchiveContext Archive { get; }

    public ArchiveModel Model { get; }
}
