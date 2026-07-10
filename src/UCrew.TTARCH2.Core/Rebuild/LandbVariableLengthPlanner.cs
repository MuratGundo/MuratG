using System.Text;
using UCrew.TTARCH2.Core.Import;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbVariableLengthPlanner
{
    public double MinimumLengthFieldConfidence { get; init; } = 0.90;

    public async Task<LandbRebuildPlan> CreatePlanAsync(
        ArchiveModel archive,
        string translatedTextPath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        LandbTextImportResult validation = await new LandbTextImportService()
            .ValidateAsync(archive, translatedTextPath, token)
            .ConfigureAwait(false);

        LandbRebuildPlan plan = new()
        {
            OriginalFileSize = archive.FileSize,
            PlannedFileSize = archive.FileSize
        };

        plan.Errors.AddRange(validation.Errors);
        plan.Warnings.AddRange(validation.Warnings);

        if (!validation.Success)
            return plan;

        long accumulatedDelta = 0;

        for (int i = 0; i < archive.Landb.TextCandidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            LandbTextCandidate candidate = archive.Landb.TextCandidates[i];
            string translated = validation.Lines[i];
            int newLength = Encoding.UTF8.GetByteCount(translated);

            LandbRebuildEntry entry = new()
            {
                Index = i,
                OriginalOffset = candidate.Offset,
                NewOffset = candidate.Offset + accumulatedDelta,
                OriginalByteLength = candidate.ByteLength,
                NewByteLength = newLength,
                Text = translated
            };

            foreach (PointerHit pointer in archive.Pointers.Where(x => x.TargetOffset == candidate.Offset))
                entry.PointerSourceOffsets.Add(pointer.SourceOffset);

            foreach (LandbLengthFieldCandidate field in archive.Landb.LengthFieldCandidates
                         .Where(x => x.TextIndex == i && x.Confidence >= MinimumLengthFieldConfidence))
            {
                entry.LengthFields.Add(new LandbRebuildLengthField
                {
                    FieldOffset = field.FieldOffset,
                    FieldSize = field.FieldSize,
                    OriginalValue = field.StoredValue,
                    NewValue = newLength,
                    Confidence = field.Confidence
                });
            }

            if (entry.Delta != 0 && entry.PointerSourceOffsets.Count == 0)
            {
                plan.Errors.Add(
                    $"Line {i + 1:N0} changes size, but no pointer referencing text offset 0x{candidate.Offset:X} was identified.");
            }

            if (entry.Delta != 0 && entry.LengthFields.Count == 0)
            {
                plan.Errors.Add(
                    $"Line {i + 1:N0} changes size, but no high-confidence length field was identified.");
            }

            plan.Entries.Add(entry);
            accumulatedDelta += entry.Delta;
        }

        plan.PlannedFileSize = archive.FileSize + accumulatedDelta;

        if (plan.Entries.Any(x => x.Delta != 0) && plan.CanRebuild)
        {
            plan.Warnings.Add(
                "Dry-run passed for detected pointer and length fields. Binary output still requires post-build structural verification.");
        }

        return plan;
    }
}
