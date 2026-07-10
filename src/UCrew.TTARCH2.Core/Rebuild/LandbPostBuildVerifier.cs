using System.Text;

namespace UCrew.TTARCH2.Core.Rebuild;

public sealed class LandbPostBuildVerifier
{
    public async Task<LandbPostBuildVerificationResult> VerifyAsync(
        string outputPath,
        LandbRebuildPlan plan,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        LandbPostBuildVerificationResult result = new()
        {
            ExpectedTextCount = plan.Entries.Count,
            ExpectedFileSize = plan.PlannedFileSize
        };

        if (!File.Exists(outputPath))
        {
            result.Errors.Add("Rebuilt LANDb file does not exist.");
            return result;
        }

        FileInfo info = new(outputPath);
        result.ActualFileSize = info.Length;

        if (result.ActualFileSize != result.ExpectedFileSize)
        {
            result.Errors.Add(
                $"File size mismatch. Expected {result.ExpectedFileSize:N0}, got {result.ActualFileSize:N0}.");
        }

        await using FileStream stream = new(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        foreach (LandbRebuildEntry entry in plan.Entries)
        {
            token.ThrowIfCancellationRequested();

            byte[] expected = new UTF8Encoding(false).GetBytes(entry.Text);
            byte[] actual = new byte[expected.Length];
            stream.Position = entry.NewOffset;

            int total = 0;
            while (total < actual.Length)
            {
                int read = await stream.ReadAsync(actual.AsMemory(total), token).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }

            if (total != expected.Length || !actual.AsSpan(0, total).SequenceEqual(expected))
            {
                result.Errors.Add(
                    $"Text verification failed for line {entry.Index + 1:N0} at 0x{entry.NewOffset:X}.");
                continue;
            }

            result.VerifiedTextCount++;
        }

        if (result.VerifiedTextCount != result.ExpectedTextCount)
        {
            result.Errors.Add(
                $"Verified text count mismatch. Expected {result.ExpectedTextCount:N0}, got {result.VerifiedTextCount:N0}.");
        }

        return result;
    }
}
