using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class ArchiveRebuildSafetyValidatorTests
{
    [Fact]
    public void BlocksVariableSizeReplacementInsideCompressionCandidate()
    {
        ArchiveModel archive = new();
        ArchiveResourceEntry resource = new()
        {
            Index = 1,
            Offset = 100,
            Size = 50,
            Extension = ".landb"
        };

        archive.Resources.Add(resource);
        archive.Resources.Add(new ArchiveResourceEntry { Index = 2, Offset = 200, Size = 20 });
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 1,
            FieldOffset = 20,
            FieldSize = 4,
            StoredValue = 50,
            Kind = "Size",
            Confidence = 0.95
        });
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 2,
            FieldOffset = 24,
            FieldSize = 4,
            StoredValue = 200,
            Kind = "Offset",
            Confidence = 0.95
        });
        archive.CompressionBlocks.Add(new ArchiveCompressionBlock
        {
            Index = 0,
            DataOffset = 110,
            Codec = "Zlib candidate",
            Confidence = 0.80
        });

        List<string> errors = new ArchiveRebuildSafetyValidator()
            .ValidateVariableSizeReplacement(archive, resource, 80);

        Assert.Contains(errors, x => x.Contains("compressed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllowsValidatedUncompressedVariableSizeReplacement()
    {
        ArchiveModel archive = new();
        ArchiveResourceEntry resource = new()
        {
            Index = 1,
            Offset = 100,
            Size = 50,
            Extension = ".landb"
        };

        archive.Resources.Add(resource);
        archive.Resources.Add(new ArchiveResourceEntry { Index = 2, Offset = 200, Size = 20 });
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 1,
            FieldOffset = 20,
            FieldSize = 4,
            StoredValue = 50,
            Kind = "Size",
            Confidence = 0.95
        });
        archive.TableFields.Add(new ArchiveTableField
        {
            ResourceIndex = 2,
            FieldOffset = 24,
            FieldSize = 4,
            StoredValue = 200,
            Kind = "Offset",
            Confidence = 0.95
        });

        List<string> errors = new ArchiveRebuildSafetyValidator()
            .ValidateVariableSizeReplacement(archive, resource, 80);

        Assert.Empty(errors);
    }
}
