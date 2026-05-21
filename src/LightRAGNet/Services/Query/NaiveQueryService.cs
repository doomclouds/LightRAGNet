using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.RetrievalContext;

namespace LightRAGNet.Services.Query;

public sealed class NaiveQueryService
{
    private readonly IVectorStore _vectorStore;
    private readonly RerankCoordinator _rerankCoordinator;
    private readonly ITokenizer _tokenizer;
    private readonly ReferenceListBuilder _referenceListBuilder = new();

    internal NaiveQueryService(
        IVectorStore vectorStore,
        RerankCoordinator rerankCoordinator,
        ITokenizer tokenizer)
    {
        _vectorStore = vectorStore;
        _rerankCoordinator = rerankCoordinator;
        _tokenizer = tokenizer;
    }

    public async Task<QueryContextResult?> BuildContextAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken = default)
    {
        var topK = queryParam.ChunkTopK > 0 ? queryParam.ChunkTopK : queryParam.TopK;
        var results = await _vectorStore.QueryAsync(
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

        var promptOverheadTokens = _tokenizer.CountTokens(
            NaiveQueryPromptBuilder.BuildPromptOverhead(queryParam));
        var availableChunkTokens = queryParam.MaxTotalTokens - promptOverheadTokens - _tokenizer.CountTokens(query) - 200;
        if (availableChunkTokens <= 0)
        {
            return null;
        }

        var (references, chunksWithRefIds) = LimitChunksByFinalContext(chunks, availableChunkTokens);
        if (chunksWithRefIds.Count == 0)
        {
            return null;
        }

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
        var rerankResults = await _rerankCoordinator.RerankAsync(
            query,
            chunks.Select(chunk => chunk.Content).ToList(),
            topK,
            cancellationToken);

        return rerankResults
            .OrderByDescending(result => result.RelevanceScore)
            .DistinctBy(result => result.Index)
            .Where(result => result.Index >= 0 && result.Index < chunks.Count)
            .Select(result => chunks[result.Index])
            .ToList();
    }

    private (List<ReferenceItem> References, List<ChunkData> ChunksWithRefIds) LimitChunksByFinalContext(
        IEnumerable<ChunkData> chunks,
        int availableChunkTokens)
    {
        var acceptedChunks = new List<ChunkData>();
        foreach (var chunk in chunks)
        {
            var candidateChunks = acceptedChunks.Concat([chunk]).ToList();
            var (candidateReferences, candidateChunksWithRefIds) = _referenceListBuilder.Build(candidateChunks);
            var candidateContext = BuildContext(candidateChunksWithRefIds, candidateReferences);
            if (_tokenizer.CountTokens(candidateContext) > availableChunkTokens)
            {
                break;
            }

            acceptedChunks.Add(chunk);
        }

        return _referenceListBuilder.Build(acceptedChunks);
    }

    private static string BuildContext(
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        var chunkLines = chunks.Select(chunk => JsonSerializer.Serialize(new
        {
            reference_id = chunk.ReferenceId,
            content = chunk.Content
        }, LightRAGJsonOptions.HumanReadable));

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
                }).Cast<object>().ToList(),
                ["references"] = references.Select(reference => new Dictionary<string, object>
                {
                    ["reference_id"] = reference.ReferenceId,
                    ["file_path"] = reference.FilePath
                }).Cast<object>().ToList()
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
