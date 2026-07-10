using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class AnalysisContext
{
    public AnalysisContext(UCrew.TTARCH2.Core.ArchiveContext archive, ArchiveModel model)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public UCrew.TTARCH2.Core.ArchiveContext Archive { get; }

    public ArchiveModel Model { get; }
}
