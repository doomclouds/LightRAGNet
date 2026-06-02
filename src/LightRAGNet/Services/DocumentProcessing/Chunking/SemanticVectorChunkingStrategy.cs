using System.Text.RegularExpressions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class SemanticVectorChunkingStrategy(
    IEmbeddingService? embeddingService,
    RecursiveCharacterChunkingStrategy recursiveFallback,
    ILogger<SemanticVectorChunkingStrategy> logger) : IChunkingStrategy
{
    private const string SentenceTerminators = "。？！.!?";

    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.SemanticVector;

    public async Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return [];
        }

        var options = Normalize(request.Options.SemanticVector);
        if (embeddingService is null)
        {
            if (!options.FallBackToRecursiveWhenEmbeddingUnavailable)
            {
                throw new InvalidOperationException(
                    "Semantic vector chunking requires an embedding service. Configure IEmbeddingService or enable recursive fallback.");
            }

            logger.LogWarning(
                "Semantic vector chunking is falling back to recursive chunking because embedding service is unavailable.");
            return await recursiveFallback.ChunkAsync(
                CreateRecursiveRequest(request, request.Content, options.ChunkTokenSize, null),
                tokenizer,
                cancellationToken);
        }

        var sentences = SplitSentences(request.Content, options.SentenceSplitRegex);
        if (sentences.Count == 0)
        {
            return [];
        }

        if (sentences.Count == 1)
        {
            return await EmitOrResplitAsync([sentences], request, options, tokenizer, cancellationToken);
        }

        var windows = BuildWindows(request.Content, sentences, options.BufferSize);
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            windows.Select(window => window.Content),
            cancellationToken);

        if (embeddings.Length != windows.Count)
        {
            throw new InvalidOperationException(
                $"Embedding service returned {embeddings.Length} embeddings for {windows.Count} semantic windows.");
        }

        var distances = CalculateDistances(embeddings);
        var breakpoints = SelectBreakpoints(distances, options);
        var groups = GroupSentences(sentences, breakpoints);
        groups = MergeSmallGroups(request.Content, groups, tokenizer, options);

        return await EmitOrResplitAsync(groups, request, options, tokenizer, cancellationToken);
    }

    private static SemanticVectorChunkingSnapshot Normalize(SemanticVectorChunkingSnapshot options)
    {
        var chunkSize = ChunkingUtilities.RequirePositiveChunkSize(
            options.ChunkTokenSize,
            "Chunking:SemanticVector:ChunkTokenSize");
        var splitRegex = string.IsNullOrWhiteSpace(options.SentenceSplitRegex)
            ? SemanticVectorChunkingOptions.DefaultSentenceSplitRegex
            : options.SentenceSplitRegex;

        return options with
        {
            ChunkTokenSize = chunkSize,
            BufferSize = Math.Max(0, options.BufferSize),
            MinChunkTokenSize = Math.Max(0, options.MinChunkTokenSize),
            SentenceSplitRegex = splitRegex
        };
    }

    private static List<SentenceSpan> SplitSentences(string content, string regex)
    {
        var span = ChunkingUtilities.TrimmedSpan(content, 0, content.Length);
        if (span is null)
        {
            return [];
        }

        if (regex == SemanticVectorChunkingOptions.DefaultSentenceSplitRegex)
        {
            var punctuationSegments = SplitBySentenceTerminators(content, span);
            if (punctuationSegments.Count > 1)
            {
                return punctuationSegments;
            }
        }

        var regexSegments = SplitByRegex(content, span, regex);
        return regexSegments.Count > 0
            ? regexSegments
            : [CreateSentence(content, span.Start, span.End)!];
    }

    private static List<SentenceSpan> SplitBySentenceTerminators(string content, SourceSpan span)
    {
        var sentences = new List<SentenceSpan>();
        var start = span.Start;

        for (var index = span.Start; index < span.End; index++)
        {
            if (!SentenceTerminators.Contains(content[index], StringComparison.Ordinal))
            {
                continue;
            }

            var sentence = CreateSentence(content, start, index + 1);
            if (sentence is not null)
            {
                sentences.Add(sentence);
            }

            start = index + 1;
        }

        var trailing = CreateSentence(content, start, span.End);
        if (trailing is not null)
        {
            sentences.Add(trailing);
        }

        return sentences;
    }

    private static List<SentenceSpan> SplitByRegex(string content, SourceSpan span, string regex)
    {
        var source = content[span.Start..span.End];
        var parts = Regex.Split(source, regex)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        if (parts.Count == 0)
        {
            return [];
        }

        var sentences = new List<SentenceSpan>();
        var cursor = span.Start;
        foreach (var part in parts)
        {
            var start = content.IndexOf(part, cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                var trimmed = part.Trim();
                start = content.IndexOf(trimmed, cursor, StringComparison.Ordinal);
            }

            if (start < 0)
            {
                start = cursor;
            }

            var sentence = CreateSentence(content, start, start + part.Length);
            if (sentence is not null)
            {
                sentences.Add(sentence);
            }

            cursor = Math.Min(content.Length, start + part.Length);
        }

        return sentences;
    }

    private static SentenceSpan? CreateSentence(string content, int start, int end)
    {
        var span = ChunkingUtilities.TrimmedSpan(content, start, end);
        return span is null
            ? null
            : new SentenceSpan(content[span.Start..span.End], span.Start, span.End);
    }

    private static List<SentenceSpan> BuildWindows(
        string content,
        IReadOnlyList<SentenceSpan> sentences,
        int bufferSize)
    {
        return sentences.Select((_, index) =>
        {
            var start = Math.Max(0, index - bufferSize);
            var end = Math.Min(sentences.Count - 1, index + bufferSize);
            var startOffset = sentences[start].Start;
            var endOffset = sentences[end].End;
            return new SentenceSpan(
                content[startOffset..endOffset],
                startOffset,
                endOffset);
        }).ToList();
    }

    private static List<double> CalculateDistances(IReadOnlyList<float[]> embeddings)
    {
        var distances = new List<double>();
        for (var index = 0; index < embeddings.Count - 1; index++)
        {
            distances.Add(ChunkingUtilities.CosineDistance(embeddings[index], embeddings[index + 1]));
        }

        return distances;
    }

    private static HashSet<int> SelectBreakpoints(
        IReadOnlyList<double> distances,
        SemanticVectorChunkingSnapshot options)
    {
        if (distances.Count == 0)
        {
            return [];
        }

        if (options.NumberOfChunks is > 1)
        {
            var desiredBreakpoints = Math.Min(options.NumberOfChunks.Value - 1, distances.Count);
            return distances
                .Select((distance, index) => (Score: distance, Index: index))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Index)
                .Take(desiredBreakpoints)
                .Select(item => item.Index)
                .ToHashSet();
        }

        var scores = options.BreakpointThresholdType == SemanticVectorBreakpointThresholdType.Gradient
            ? CalculateGradientScores(distances)
            : [.. distances];
        var threshold = CalculateThreshold(scores, distances, options);

        return scores
            .Select((score, index) => (Score: score, Index: index))
            .Where(item => item.Score > threshold)
            .Select(item => item.Index)
            .ToHashSet();
    }

    private static double CalculateThreshold(
        IReadOnlyList<double> scores,
        IReadOnlyList<double> distances,
        SemanticVectorChunkingSnapshot options)
    {
        return options.BreakpointThresholdType switch
        {
            SemanticVectorBreakpointThresholdType.Percentile =>
                ChunkingUtilities.Percentile(distances, options.BreakpointThresholdAmount ?? 95),
            SemanticVectorBreakpointThresholdType.StandardDeviation =>
                distances.Average() + (options.BreakpointThresholdAmount ?? 3) * StandardDeviation(distances),
            SemanticVectorBreakpointThresholdType.Interquartile =>
                ChunkingUtilities.Percentile(distances, 75) +
                (options.BreakpointThresholdAmount ?? 1.5) *
                (ChunkingUtilities.Percentile(distances, 75) - ChunkingUtilities.Percentile(distances, 25)),
            SemanticVectorBreakpointThresholdType.Gradient =>
                ChunkingUtilities.Percentile(scores, options.BreakpointThresholdAmount ?? 95),
            _ => ChunkingUtilities.Percentile(distances, 95)
        };
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        var average = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - average, 2)) / values.Count);
    }

    private static List<double> CalculateGradientScores(IReadOnlyList<double> values)
    {
        if (values.Count <= 1)
        {
            return [.. values];
        }

        var scores = new List<double>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var previous = index > 0 ? Math.Abs(values[index] - values[index - 1]) : 0;
            var next = index < values.Count - 1 ? Math.Abs(values[index + 1] - values[index]) : 0;
            scores.Add(Math.Max(previous, next));
        }

        return scores;
    }

    private static List<List<SentenceSpan>> GroupSentences(
        IReadOnlyList<SentenceSpan> sentences,
        HashSet<int> breakpoints)
    {
        var groups = new List<List<SentenceSpan>>();
        var current = new List<SentenceSpan>();

        for (var index = 0; index < sentences.Count; index++)
        {
            current.Add(sentences[index]);
            if (breakpoints.Contains(index))
            {
                groups.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    private static List<List<SentenceSpan>> MergeSmallGroups(
        string source,
        List<List<SentenceSpan>> groups,
        ITokenizer tokenizer,
        SemanticVectorChunkingSnapshot options)
    {
        if (options.MinChunkTokenSize <= 0 || groups.Count <= 1)
        {
            return groups;
        }

        var output = groups.Select(group => group.ToList()).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 0; index < output.Count; index++)
            {
                var tokens = CountGroupTokens(source, output[index], tokenizer);
                if (tokens >= options.MinChunkTokenSize)
                {
                    continue;
                }

                if (TryMergeWithPrevious(source, output, index, tokenizer, options.ChunkTokenSize) ||
                    TryMergeWithNext(source, output, index, tokenizer, options.ChunkTokenSize))
                {
                    changed = true;
                    break;
                }
            }
        }

        return output;
    }

    private static bool TryMergeWithPrevious(
        string source,
        List<List<SentenceSpan>> groups,
        int index,
        ITokenizer tokenizer,
        int chunkSize)
    {
        if (index <= 0)
        {
            return false;
        }

        var merged = groups[index - 1].Concat(groups[index]).ToList();
        if (CountGroupTokens(source, merged, tokenizer) > chunkSize)
        {
            return false;
        }

        groups[index - 1] = merged;
        groups.RemoveAt(index);
        return true;
    }

    private static bool TryMergeWithNext(
        string source,
        List<List<SentenceSpan>> groups,
        int index,
        ITokenizer tokenizer,
        int chunkSize)
    {
        if (index >= groups.Count - 1)
        {
            return false;
        }

        var merged = groups[index].Concat(groups[index + 1]).ToList();
        if (CountGroupTokens(source, merged, tokenizer) > chunkSize)
        {
            return false;
        }

        groups[index] = merged;
        groups.RemoveAt(index + 1);
        return true;
    }

    private static int CountGroupTokens(
        string source,
        IReadOnlyList<SentenceSpan> group,
        ITokenizer tokenizer)
    {
        return ChunkingUtilities.CountTokens(tokenizer, GetGroupContent(source, group));
    }

    private async Task<IReadOnlyList<ChunkingSegment>> EmitOrResplitAsync(
        IReadOnlyList<List<SentenceSpan>> groups,
        ChunkingRequest request,
        SemanticVectorChunkingSnapshot options,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var output = new List<ChunkingSegment>();

        foreach (var group in groups)
        {
            var content = GetGroupContent(request.Content, group);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var tokens = ChunkingUtilities.CountTokens(tokenizer, content);
            var span = new SourceSpan(group[0].Start, group[^1].End);
            if (tokens <= options.ChunkTokenSize)
            {
                output.Add(new ChunkingSegment
                {
                    Content = content,
                    Tokens = tokens,
                    Order = output.Count,
                    Strategy = Strategy,
                    SourceSpan = span
                });
                continue;
            }

            var recursivePieces = await recursiveFallback.ChunkAsync(
                CreateRecursiveRequest(request, content, options.ChunkTokenSize, 0),
                tokenizer,
                cancellationToken);

            output.AddRange(recursivePieces.Select(piece => new ChunkingSegment
            {
                Content = piece.Content,
                Tokens = piece.Tokens,
                Order = output.Count,
                Strategy = piece.Strategy,
                SourceSpan = MapSourceSpan(piece.SourceSpan, span.Start),
                Heading = piece.Heading,
                Metadata = piece.Metadata
            }));
        }

        return output.Select((segment, index) => new ChunkingSegment
        {
            Content = segment.Content,
            Tokens = segment.Tokens,
            Order = index,
            Strategy = segment.Strategy,
            SourceSpan = segment.SourceSpan,
            Heading = segment.Heading,
            Metadata = segment.Metadata
        }).ToList();
    }

    private static string GetGroupContent(string source, IReadOnlyList<SentenceSpan> group)
    {
        return source[group[0].Start..group[^1].End];
    }

    private static SourceSpan? MapSourceSpan(SourceSpan? span, int offset)
    {
        return span is null
            ? null
            : new SourceSpan(offset + span.Start, offset + span.End);
    }

    private static ChunkingRequest CreateRecursiveRequest(
        ChunkingRequest request,
        string content,
        int chunkSize,
        int? overlap)
    {
        var recursive = request.Options.RecursiveCharacter with
        {
            ChunkTokenSize = chunkSize,
            ChunkOverlapTokenSize = overlap ?? request.Options.RecursiveCharacter.ChunkOverlapTokenSize
        };

        return request with
        {
            Content = content,
            Options = request.Options with
            {
                Strategy = LightRagChunkingStrategy.RecursiveCharacter,
                RecursiveCharacter = recursive
            }
        };
    }

    private sealed record SentenceSpan(string Content, int Start, int End);
}
