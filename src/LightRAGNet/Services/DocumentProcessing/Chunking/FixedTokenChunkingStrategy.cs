using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class FixedTokenChunkingStrategy : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.FixedToken;

    public Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = request.Options.FixedToken;
        var content = request.Content.Trim();
        var chunks = string.IsNullOrEmpty(options.SplitByCharacter)
            ? ChunkByWindow(content, tokenizer, options)
            : ChunkBySeparator(content, tokenizer, options);

        return Task.FromResult<IReadOnlyList<ChunkingSegment>>(chunks);
    }

    private static List<ChunkingSegment> ChunkBySeparator(
        string content,
        ITokenizer tokenizer,
        FixedTokenChunkingSnapshot options)
    {
        var chunkSize = ChunkingUtilities.RequirePositiveChunkSize(
            options.ChunkTokenSize,
            "Chunking:FixedToken:ChunkTokenSize");
        var overlap = ChunkingUtilities.ClampOverlap(chunkSize, options.ChunkOverlapTokenSize);
        var rawChunks = content.Split(options.SplitByCharacter, StringSplitOptions.None);
        var newChunks = new List<(int Tokens, string Content)>();

        if (options.SplitByCharacterOnly)
        {
            foreach (var chunk in rawChunks)
            {
                var chunkTokens = tokenizer.Encode(chunk);
                if (chunkTokens.Count > chunkSize)
                {
                    throw new InvalidOperationException(
                        $"Chunk exceeds token limit: {chunkTokens.Count} > {chunkSize}");
                }

                newChunks.Add((chunkTokens.Count, chunk));
            }
        }
        else
        {
            foreach (var chunk in rawChunks)
            {
                var chunkTokens = tokenizer.Encode(chunk);
                if (chunkTokens.Count > chunkSize)
                {
                    var stepSize = chunkSize - overlap;
                    for (var start = 0; start < chunkTokens.Count; start += stepSize)
                    {
                        var end = Math.Min(start + chunkSize, chunkTokens.Count);
                        var subTokens = chunkTokens.Skip(start).Take(end - start).ToList();
                        var chunkContent = tokenizer.Decode(subTokens);
                        newChunks.Add((subTokens.Count, chunkContent));
                    }
                }
                else
                {
                    newChunks.Add((chunkTokens.Count, chunk));
                }
            }
        }

        return newChunks
            .Select((chunk, index) => CreateSegment(chunk.Content, chunk.Tokens, index))
            .ToList();
    }

    private static List<ChunkingSegment> ChunkByWindow(
        string content,
        ITokenizer tokenizer,
        FixedTokenChunkingSnapshot options)
    {
        var chunkSize = ChunkingUtilities.RequirePositiveChunkSize(
            options.ChunkTokenSize,
            "Chunking:FixedToken:ChunkTokenSize");
        var overlap = ChunkingUtilities.ClampOverlap(chunkSize, options.ChunkOverlapTokenSize);
        var tokens = tokenizer.Encode(content);
        var chunks = new List<ChunkingSegment>();
        var stepSize = chunkSize - overlap;

        for (var index = 0; index < tokens.Count; index += stepSize)
        {
            var end = Math.Min(index + chunkSize, tokens.Count);
            var remainingTokens = tokens.Count - index;

            if (remainingTokens <= overlap && chunks.Count > 0)
            {
                var previous = chunks[^1];
                var previousTokens = tokenizer.Encode(previous.Content);
                var remainingChunkTokens = tokens.Skip(index).Take(remainingTokens).ToList();
                var mergedTokens = previousTokens.Concat(remainingChunkTokens).ToList();
                var mergedContent = tokenizer.Decode(mergedTokens);

                chunks[^1] = CreateSegment(
                    mergedContent,
                    mergedTokens.Count,
                    previous.Order);
                break;
            }

            var chunkTokens = tokens.Skip(index).Take(end - index).ToList();

            if (chunkTokens.Count == 0)
            {
                break;
            }

            var chunkContent = tokenizer.Decode(chunkTokens);

            chunks.Add(CreateSegment(
                chunkContent,
                chunkTokens.Count,
                chunks.Count));
        }

        return chunks;
    }

    private static ChunkingSegment CreateSegment(string content, int tokens, int order)
    {
        return new ChunkingSegment
        {
            Content = content.Trim(),
            Tokens = tokens,
            Order = order,
            Strategy = LightRagChunkingStrategy.FixedToken
        };
    }
}
