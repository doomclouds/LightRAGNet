using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class KgQueryContextBuilder(ITokenizer tokenizer)
{
    private const int SafetyBufferTokens = 180;
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
        return LimitBySection(
            entities,
            maxTokens,
            item => BuildEntitySection([item]),
            items => BuildEntitySection(items));
    }

    private List<RelationData> LimitRelations(IEnumerable<RelationData> relations, int maxTokens)
    {
        return LimitBySection(
            relations,
            maxTokens,
            item => BuildRelationSection([item]),
            items => BuildRelationSection(items));
    }

    private List<T> LimitBySection<T>(
        IEnumerable<T> items,
        int maxTokens,
        Func<T, string> singleItemSectionFactory,
        Func<IReadOnlyCollection<T>, string> sectionFactory)
    {
        var accepted = new List<T>();

        foreach (var item in items)
        {
            var candidate = accepted.Concat([item]).ToList();
            var candidateTokens = tokenizer.CountTokens(sectionFactory(candidate));
            if (candidateTokens > maxTokens)
            {
                if (accepted.Count == 0 && tokenizer.CountTokens(singleItemSectionFactory(item)) <= maxTokens)
                {
                    accepted.Add(item);
                }

                break;
            }

            accepted.Add(item);
        }

        return accepted;
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
            - SafetyBufferTokens;

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

        var entitySection = BuildEntitySection(entities);
        if (!string.IsNullOrWhiteSpace(entitySection))
        {
            parts.Add(entitySection);
        }

        var relationSection = BuildRelationSection(relations);
        if (!string.IsNullOrWhiteSpace(relationSection))
        {
            parts.Add(relationSection);
        }

        var chunkContext = BuildChunkAndReferenceContext(chunks, references);
        if (!string.IsNullOrWhiteSpace(chunkContext))
        {
            parts.Add(chunkContext);
        }

        return string.Join("\n\n", parts);
    }

    private static string BuildEntitySection(IReadOnlyCollection<EntityData> entities)
    {
        if (entities.Count == 0)
        {
            return string.Empty;
        }

        return $"""
                Knowledge Graph Data (Entity):

                ```json
                {string.Join('\n', entities.Select(SerializeEntity))}
                ```
                """;
    }

    private static string BuildRelationSection(IReadOnlyCollection<RelationData> relations)
    {
        if (relations.Count == 0)
        {
            return string.Empty;
        }

        return $"""
                Knowledge Graph Data (Relationship):

                ```json
                {string.Join('\n', relations.Select(SerializeRelation))}
                ```
                """;
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
