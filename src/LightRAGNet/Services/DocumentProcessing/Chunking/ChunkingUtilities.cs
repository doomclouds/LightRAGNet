using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

internal static class ChunkingUtilities
{
    public static int RequirePositiveChunkSize(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"{name} must be greater than zero.");
        }

        return value;
    }

    public static int ClampOverlap(int chunkSize, int overlap)
    {
        if (chunkSize <= 1)
        {
            return 0;
        }

        return Math.Clamp(overlap, 0, chunkSize - 1);
    }

    public static int CountTokens(ITokenizer tokenizer, string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : tokenizer.Encode(text).Count;
    }

    public static SourceSpan? TrimmedSpan(string content, int start, int end)
    {
        start = Math.Max(0, Math.Min(start, content.Length));
        end = Math.Max(start, Math.Min(end, content.Length));
        while (start < end && char.IsWhiteSpace(content[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(content[end - 1]))
        {
            end--;
        }

        return start >= end ? null : new SourceSpan(start, end);
    }

    public static double CosineDistance(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            throw new InvalidOperationException("Embedding vectors must have the same non-zero dimension.");
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 1.0;
        }

        return 1.0 - dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (double.IsNaN(percentile) || percentile < 0 || percentile > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentile),
                percentile,
                "Percentile must be between 0 and 100.");
        }

        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var position = (sorted.Length - 1) * percentile / 100.0;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var weight = position - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}
