using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class EntropyAnalyzer : IAnalyzer
{
    public string Name => "Entropy Analyzer";

    public int Priority => 100;

    public int BlockSize { get; init; } = 512;

    public async Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;
        reader.Seek(0);

        byte[] buffer = new byte[BlockSize];

        while (!reader.EndOfFile)
        {
            token.ThrowIfCancellationRequested();

            long offset = reader.Position;
            int read = reader.Read(buffer, 0, BlockSize);

            if (read <= 0)
                break;

            context.Model.EntropyMap.Add(new EntropyPoint
            {
                Offset = offset,
                BlockSize = read,
                Value = CalculateEntropy(buffer.AsSpan(0, read))
            });

            await Task.Yield();
        }
    }

    private static double CalculateEntropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return 0;

        Span<int> histogram = stackalloc int[256];

        foreach (byte value in data)
            histogram[value]++;

        double entropy = 0;

        foreach (int count in histogram)
        {
            if (count == 0)
                continue;

            double probability = (double)count / data.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}
