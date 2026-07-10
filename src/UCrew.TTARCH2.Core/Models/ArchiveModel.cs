namespace UCrew.TTARCH2.Core.Models;

public sealed class ArchiveModel
{
    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public HeaderModel Header { get; set; } = new();

    public List<EntropyPoint> EntropyMap { get; } = new();

    public List<PointerHit> Pointers { get; } = new();

    public List<SignatureHit> Signatures { get; } = new();

    public List<RegionModel> Regions { get; } = new();

    public List<ECTTChunkInfo> ECTTChunks { get; } = new();

    public ValidationResult Validation { get; } = new();
}
