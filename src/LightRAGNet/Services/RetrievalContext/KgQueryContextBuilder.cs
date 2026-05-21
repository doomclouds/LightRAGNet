using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class KgQueryContextBuilder(ITokenizer tokenizer)
{
    private const int ReferenceAndSafetyBufferTokens = 200;
    private readonly ReferenceListBuilder _referenceListBuilder = new();

    public KgQueryContextBuildResult Build(
        KGSearchResult searchResult,
        QueryParam queryParam,
        string query)
    {
        var entities = LimitEntities(searchResult.Entities, queryParam.MaxEntityTokens);
        var relations = LimitRelations(searchResult.Relations, queryParam.MaxRelationTokens);
        var chunks = LimitChunksByFinalContext(searchResult.Chunks, entities, relations, queryParam, query);
        var (references, chunksWithRefIds) = _referenceListBuilder.Build(chunks);
        var context = BuildContext(entities, relations, chunksWithRefIds, references);

        return new KgQueryContextBuildResult(
            context,
            entities,
            relations,
            chunksWithRefIds,
            references);
    }

    private List<EntityData> LimitEntities(IEnumerable<EntityData> entities, int maxTokens)
    {
        var result = new List<EntityData>();
        var currentTokens = 0;

        foreach (var entity in entities)
        {
            var tokens = tokenizer.CountTokens(SerializeEntity(entity));
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }

            result.Add(entity);
            currentTokens += tokens;
        }

        return result;
    }

    private List<RelationData> LimitRelations(IEnumerable<RelationData> relations, int maxTokens)
    {
        var result = new List<RelationData>();
        var currentTokens = 0;

        foreach (var relation in relations)
        {
            var tokens = tokenizer.CountTokens(SerializeRelation(relation));
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }

            result.Add(relation);
            currentTokens += tokens;
        }

        return result;
    }

    private List<ChunkData> LimitChunksByFinalContext(
        IEnumerable<ChunkData> chunks,
        IReadOnlyCollection<EntityData> entities,
        IReadOnlyCollection<RelationData> relations,
        QueryParam queryParam,
        string query)
    {
        var kgContextWithoutChunks = BuildContext(
            entities,
            relations,
            [],
            []);
        var availableChunkTokens =
            queryParam.MaxTotalTokens
            - tokenizer.CountTokens(query)
            - tokenizer.CountTokens(kgContextWithoutChunks)
            - ReferenceAndSafetyBufferTokens;

        if (availableChunkTokens <= 0)
        {
            return [];
        }

        var accepted = new List<ChunkData>();
        foreach (var chunk in chunks)
        {
            var candidate = accepted.Concat([chunk]).ToList();
            var (candidateReferences, candidateChunksWithRefIds) = _referenceListBuilder.Build(candidate);
            var candidateContext = BuildChunkAndReferenceContext(candidateChunksWithRefIds, candidateReferences);
            if (tokenizer.CountTokens(candidateContext) > availableChunkTokens)
            {
                break;
            }

            accepted.Add(chunk);
        }

        return accepted;
    }

    private static string BuildContext(
        IReadOnlyCollection<EntityData> entities,
        IReadOnlyCollection<RelationData> relations,
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        var parts = new List<string>();

        if (entities.Count > 0)
        {
            parts.Add($"""
                       Knowledge Graph Data (Entity):

                       ```json
                       {string.Join('\n', entities.Select(SerializeEntity))}
                       ```
                       """);
        }

        if (relations.Count > 0)
        {
            parts.Add($"""
                       Knowledge Graph Data (Relationship):

                       ```json
                       {string.Join('\n', relations.Select(SerializeRelation))}
                       ```
                       """);
        }

        var chunkContext = BuildChunkAndReferenceContext(chunks, references);
        if (!string.IsNullOrWhiteSpace(chunkContext))
        {
            parts.Add(chunkContext);
        }

        return string.Join("\n\n", parts);
    }

    private static string BuildChunkAndReferenceContext(
        IReadOnlyCollection<ChunkData> chunks,
        IReadOnlyCollection<ReferenceItem> references)
    {
        if (chunks.Count == 0)
        {
            return string.Empty;
        }

        var chunkLines = chunks.Select(chunk => JsonSerializer.Serialize(new
        {
            reference_id = chunk.ReferenceId,
            content = chunk.Content
        }, LightRAGJsonOptions.HumanReadable));
        var referenceLines = references.Select(reference => $"[{reference.ReferenceId}] {reference.FilePath}");

        return $"""
                Document Chunks (Each entry has a reference_id refer to the `Reference Document List`):

                ```json
                {string.Join('\n', chunkLines)}
                ```

                Reference Document List (Each entry starts with a [reference_id] that corresponds to entries in the Document Chunks):

                ```
                {string.Join('\n', referenceLines)}
                ```
                """;
    }

    private static string SerializeEntity(EntityData entity)
    {
        return JsonSerializer.Serialize(new
        {
            entity = entity.Name,
            type = entity.Type,
            description = entity.Description
        }, LightRAGJsonOptions.HumanReadable);
    }

    private static string SerializeRelation(RelationData relation)
    {
        return JsonSerializer.Serialize(new
        {
            entity1 = relation.SourceId,
            entity2 = relation.TargetId,
            keywords = relation.Keywords,
            description = relation.Description
        }, LightRAGJsonOptions.HumanReadable);
    }
}

internal sealed record KgQueryContextBuildResult(
    string Context,
    List<EntityData> Entities,
    List<RelationData> Relations,
    List<ChunkData> Chunks,
    List<ReferenceItem> References);
