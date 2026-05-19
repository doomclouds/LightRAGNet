using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.RetrievalContext;

namespace LightRAGNet.Services.Query;

public sealed class NaiveQueryService(
    IVectorStore vectorStore,
    IRerankService rerankService,
    ITokenizer tokenizer)
{
    private readonly ChunkTokenLimiter _chunkTokenLimiter = new(tokenizer);
    private readonly ReferenceListBuilder _referenceListBuilder = new();

    public async Task<QueryContextResult?> BuildContextAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken = default)
    {
        var topK = queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK;
        var results = await vectorStore.QueryAsync(
            "chunks",
            query,
            topK,
            queryEmbedding: null,
            cancellationToken: cancellationToken);

        if (results.Count == 0)
        {
            return null;
        }

        var chunks = results.Select(result => new ChunkData
        {
            ChunkId = result.Id,
            Content = result.Content,
            FilePath = result.Metadata.GetValueOrDefault("file_path")?.ToString() ?? "unknown_source"
        }).ToList();

        if (queryParam.EnableRerank && chunks.Count > 0)
        {
            chunks = await RerankChunksAsync(query, chunks, topK, cancellationToken);
        }

        var promptOverheadTokens = tokenizer.CountTokens(
            NaiveQueryPromptBuilder.BuildPromptOverhead(queryParam));
        var availableChunkTokens = Math.Max(
            0,
            queryParam.MaxTotalTokens - promptOverheadTokens - tokenizer.CountTokens(query) - 200);
        var limitedChunks = _chunkTokenLimiter.Limit(chunks, availableChunkTokens);
        if (limitedChunks.Count == 0)
        {
            return null;
        }

        var (references, chunksWithRefIds) = _referenceListBuilder.Build(limitedChunks);

        return new QueryContextResult
        {
            Context = BuildContext(chunksWithRefIds, references),
            RawData = BuildRawData(results.Count, chunksWithRefIds, references)
        };
    }

    private async Task<List<ChunkData>> RerankChunksAsync(
        string query,
        List<ChunkData> chunks,
        int topK,
        CancellationToken cancellationToken)
    {
        var rerankResults = await rerankService.RerankAsync(
            query,
            chunks.Select(chunk => chunk.Content).ToList(),
            topK,
            cancellationToken);

        return rerankResults
            .OrderByDescending(result => result.RelevanceScore)
            .Where(result => result.Index >= 0 && result.Index < chunks.Count)
            .Select(result => chunks[result.Index])
            .ToList();
    }

    private static string BuildContext(
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        var chunkLines = chunks.Select(chunk => JsonSerializer.Serialize(new
        {
            reference_id = chunk.ReferenceId,
            content = chunk.Content
        }));

        var referenceLines = references.Select(reference => $"[{reference.ReferenceId}] {reference.FilePath}");

        return $"""
                ---Document Chunks---
                {string.Join('\n', chunkLines)}

                ---Reference Document List---
                {string.Join('\n', referenceLines)}
                """;
    }

    private static Dictionary<string, object> BuildRawData(
        int totalChunksFound,
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        return new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["entities"] = Array.Empty<object>(),
                ["relationships"] = Array.Empty<object>(),
                ["chunks"] = chunks.Select(chunk => new Dictionary<string, object>
                {
                    ["chunk_id"] = chunk.ChunkId,
                    ["content"] = chunk.Content,
                    ["file_path"] = chunk.FilePath,
                    ["reference_id"] = chunk.ReferenceId
                }).ToList(),
                ["references"] = references.Select(reference => new Dictionary<string, object>
                {
                    ["reference_id"] = reference.ReferenceId,
                    ["file_path"] = reference.FilePath
                }).ToList()
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["query_mode"] = QueryMode.Naive.ToString(),
                ["keywords"] = new Dictionary<string, object>
                {
                    ["high_level"] = Array.Empty<string>(),
                    ["low_level"] = Array.Empty<string>()
                },
                ["processing_info"] = new Dictionary<string, object>
                {
                    ["total_chunks_found"] = totalChunksFound,
                    ["final_chunks_count"] = chunks.Count
                }
            }
        };
    }
}
