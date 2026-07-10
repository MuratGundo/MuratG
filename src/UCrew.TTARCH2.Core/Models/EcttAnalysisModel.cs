namespace UCrew.TTARCH2.Core.Models;

public sealed class EcttAnalysisModel
{
    public bool LooksLikeEctt { get; set; }

    public long OffsetTableStart { get; set; }

    public long OffsetTableEnd { get; set; }

    public int OffsetEntryCount { get; set; }

    public int ChunkCount { get; set; }

    public double Confidence { get; set; }

    public string Notes { get; set; } = string.Empty;
}
