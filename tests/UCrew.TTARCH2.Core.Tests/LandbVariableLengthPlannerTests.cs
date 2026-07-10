using System.Text;
using UCrew.TTARCH2.Core.Models;
using UCrew.TTARCH2.Core.Rebuild;

namespace UCrew.TTARCH2.Core.Tests;

public sealed class LandbVariableLengthPlannerTests
{
    [Fact]
    public async Task PlannerCalculatesNewOffsetsAndFileSize()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string translated = Path.Combine(root, "translated.txt");
        await File.WriteAllTextAsync(translated, "Longer" + Environment.NewLine + "Ok" + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = new() { FileSize = 100 };
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 20, ByteLength = 5, Text = "Hello" });
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 40, ByteLength = 5, Text = "World" });
        archive.Pointers.Add(new PointerHit { SourceOffset = 4, TargetOffset = 20, IsValid = true });
        archive.Pointers.Add(new PointerHit { SourceOffset = 8, TargetOffset = 40, IsValid = true });

        LandbRebuildPlan plan = await new LandbVariableLengthPlanner().CreatePlanAsync(archive, translated);

        Assert.True(plan.CanRebuild);
        Assert.Equal(98, plan.PlannedFileSize);
        Assert.Equal(20, plan.Entries[0].NewOffset);
        Assert.Equal(41, plan.Entries[1].NewOffset);
    }

    [Fact]
    public async Task PlannerBlocksChangedSizeWhenPointerIsUnknown()
    {
        string root = Path.Combine(Path.GetTempPath(), "ucrew-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string translated = Path.Combine(root, "translated.txt");
        await File.WriteAllTextAsync(translated, "Longer" + Environment.NewLine, new UTF8Encoding(false));

        ArchiveModel archive = new() { FileSize = 64 };
        archive.Landb.TextCandidates.Add(new LandbTextCandidate { Offset = 16, ByteLength = 5, Text = "Hello" });

        LandbRebuildPlan plan = await new LandbVariableLengthPlanner().CreatePlanAsync(archive, translated);

        Assert.False(plan.CanRebuild);
        Assert.Contains(plan.Errors, x => x.Contains("no pointer", StringComparison.OrdinalIgnoreCase));
    }
}
