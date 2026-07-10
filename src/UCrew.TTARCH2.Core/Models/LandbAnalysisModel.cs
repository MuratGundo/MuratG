namespace UCrew.TTARCH2.Core.Models;

public sealed class LandbAnalysisModel
{
    public bool LooksLikeLandb { get; set; }

    public double Confidence { get; set; }

    public string Notes { get; set; } = string.Empty;

    public List<LandbTextCandidate> TextCandidates { get; } = new();
}
