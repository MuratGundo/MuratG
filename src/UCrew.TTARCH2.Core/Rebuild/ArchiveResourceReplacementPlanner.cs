using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveResourceReplacementPlanner
{
    public ArchiveResourceReplacementPlan Create(
        ArchiveModel archive,
        ArchiveResourceEntry resource,
        string replacementPath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resource);

        ArchiveResourceReplacementPlan plan = new()
        {
            Resource = resource,
            ReplacementPath = Path.GetFullPath(replacementPath),
            ReplacementSize = File.Exists(replacementPath) ? new FileInfo(replacementPath).Length : 0
        };

        if (!File.Exists(replacementPath))
        {
            plan.Errors.Add("Replacement file does not exist.");
            return plan;
        }

        if (!resource.IsLandb)
            plan.Warnings.Add("Selected resource is not confirmed as LANDb.");

        if (resource.Offset < 0 || resource.Size <= 0 || resource.Offset + resource.Size > archive.FileSize)
            plan.Errors.Add("Selected resource boundaries are invalid.");

        if (plan.RequiresArchiveRebuild)
        {
            plan.Errors.Add(
                "Replacement size differs from the original resource. TTARCH2 file-table offsets and compressed block metadata must be confirmed before rebuilding.");
        }
        else
        {
            plan.Warnings.Add(
                "Same-size replacement can be applied safely to an archive copy after resource identity is confirmed.");
        }

        return plan;
    }
}
