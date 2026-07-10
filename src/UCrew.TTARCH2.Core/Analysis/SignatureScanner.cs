using UCrew.TTARCH2.Core.Analysis.Signatures;
using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core.Analysis;

public sealed class SignatureScanner : IAnalyzer
{
    public string Name => "Signature Scanner";

    public int Priority => 250;

    public Task AnalyzeAsync(AnalysisContext context, CancellationToken token)
    {
        var reader = context.Archive.Reader;

        foreach (BinarySignature signature in SignatureDatabase.Default)
        {
            reader.Seek(0);

            while (reader.Remaining >= signature.Pattern.Length)
            {
                token.ThrowIfCancellationRequested();

                long offset = reader.Position;

                if (signature.Alignment > 1 && offset % signature.Alignment != 0)
                {
                    reader.Seek(offset + 1);
                    continue;
                }

                byte[] data = reader.ReadBytes(signature.Pattern.Length);

                if (data.AsSpan().SequenceEqual(signature.Pattern))
                {
                    context.Model.Signatures.Add(new SignatureHit
                    {
                        Name = signature.Name,
                        Category = signature.Category,
                        Offset = offset,
                        Length = signature.Pattern.Length,
                        Confidence = 1.0
                    });

                    reader.Seek(Math.Min(offset + signature.Pattern.Length, reader.Length));
                }
                else
                {
                    reader.Seek(offset + 1);
                }
            }
        }

        return Task.CompletedTask;
    }
}
