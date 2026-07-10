using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class ArchiveRebuildSafetyValidator
{
    public List<string> ValidateVariableSizeReplacement(
        ArchiveModel archive,
        ArchiveResourceEntry resource,
        long replacementSize)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resource);

        List<string> errors = new();
        long delta = replacementSize - resource.Size;

        if (delta == 0)
            return errors;

        bool compressionCandidateInsideResource = archive.CompressionBlocks.Any(block =>
            block.DataOffset >= resource.Offset
            && block.DataOffset < resource.Offset + resource.Size
            && block.Confidence >= 0.50);

        if (compressionCandidateInsideResource)
        {
            errors.Add(
                "Selected LANDb appears to be inside a compressed TTARCH2 block. Variable-size replacement is blocked until that codec/block format is fully supported.");
        }

        bool hasSizeField = archive.TableFields.Any(field =>
            field.ResourceIndex == resource.Index
            && field.Kind == "Size"
            && field.Confidence >= 0.60);

        if (!hasSizeField)
            errors.Add("No high-confidence size field was found for the selected LANDb resource.");

        bool hasFollowingOffset = archive.TableFields.Any(field =>
            field.Kind == "Offset"
            && field.Confidence >= 0.60
            && archive.Resources.Any(r => r.Index == field.ResourceIndex && r.Offset > resource.Offset));

        bool hasFollowingPointer = archive.Pointers.Any(pointer => pointer.TargetOffset > resource.Offset);

        if (!hasFollowingOffset && !hasFollowingPointer)
            errors.Add("No following offset or pointer fields were found for variable-size TTARCH2 rebuild.");

        return errors;
    }
}
