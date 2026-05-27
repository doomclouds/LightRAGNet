using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Server.Tests.Evaluation;

internal static class ApiRetrievalEvaluationDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ApiRetrievalEvaluationDataSet LoadDefault()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Evaluation", "Data");
        return LoadFromDirectory(dataDirectory);
    }

    public static ApiRetrievalEvaluationDataSet LoadFromDirectory(string dataDirectory)
    {
        var dataset = ReadJson<SampleDatasetJson>(Path.Combine(dataDirectory, "sample_dataset.json"));
        var documentOracle = ReadJson<SampleRetrievalOracleJson>(Path.Combine(dataDirectory, "sample_retrieval_oracle.json"));
        var extendedOracle = ReadJson<LightRagNetRetrievalOracleJson>(Path.Combine(dataDirectory, "lightragnet_retrieval_oracle.json"));

        var questions = RequireList(dataset.TestCases, "sample_dataset.json:test_cases")
            .Select(item => RequireString(item.Question, "sample_dataset.json:test_cases[].question"))
            .ToHashSet(StringComparer.Ordinal);
        var documentOracleByQuestion = RequireList(documentOracle.Oracle, "sample_retrieval_oracle.json:oracle")
            .ToDictionary(
                item => RequireString(item.Question, "sample_retrieval_oracle.json:oracle[].question"),
                item => RequireStringList(item.ExpectedDocuments, "sample_retrieval_oracle.json:oracle[].expected_documents"),
                StringComparer.Ordinal);
        var corpus = extendedOracle.Corpus
            ?? throw new InvalidOperationException("lightragnet_retrieval_oracle.json:corpus is required.");
        var chunks = RequireList(corpus.Chunks, "lightragnet_retrieval_oracle.json:corpus.chunks")
            .Select(item => new ApiRetrievalChunkSpec(
                RequireString(item.Id, "corpus.chunks[].id"),
                RequireString(item.DocumentName, "corpus.chunks[].documentName"),
                RequireString(item.FilePath, "corpus.chunks[].filePath"),
                RequireString(item.Content, "corpus.chunks[].content")))
            .ToArray();
        var entities = RequireList(corpus.Entities, "lightragnet_retrieval_oracle.json:corpus.entities")
            .Select(item => new ApiRetrievalEntitySpec(
                RequireString(item.Id, "corpus.entities[].id"),
                RequireString(item.Type, "corpus.entities[].type"),
                RequireString(item.Description, "corpus.entities[].description"),
                RequireString(item.SourceId, "corpus.entities[].sourceId"),
                RequireString(item.FilePath, "corpus.entities[].filePath")))
            .ToArray();
        var relationships = RequireList(corpus.Relationships, "lightragnet_retrieval_oracle.json:corpus.relationships")
            .Select(item => new ApiRetrievalRelationshipSpec(
                RequireString(item.SourceId, "corpus.relationships[].sourceId"),
                RequireString(item.TargetId, "corpus.relationships[].targetId"),
                RequireString(item.Keywords, "corpus.relationships[].keywords"),
                RequireString(item.Description, "corpus.relationships[].description"),
                item.Weight ?? throw new InvalidOperationException("corpus.relationships[].weight is required."),
                RequireString(item.SourceIdList, "corpus.relationships[].sourceIdList")))
            .ToArray();
        var cases = RequireList(extendedOracle.Cases, "lightragnet_retrieval_oracle.json:cases")
            .Select(item => new ApiRetrievalEvaluationCase(
                RequireString(item.Name, "cases[].name"),
                RequireString(item.Question, "cases[].question"),
                ParseMode(RequireString(item.Mode, "cases[].mode"), item.Name),
                RequireStringList(item.HighLevelKeywords, "cases[].highLevelKeywords"),
                RequireStringList(item.LowLevelKeywords, "cases[].lowLevelKeywords"),
                item.TopK ?? throw new InvalidOperationException("cases[].topK is required."),
                item.ChunkTopK ?? throw new InvalidOperationException("cases[].chunkTopK is required."),
                item.EnableRerank ?? throw new InvalidOperationException("cases[].enableRerank is required."),
                RequireStringList(item.ExpectedChunkIds, "cases[].expectedChunkIds"),
                RequireStringList(item.ExpectedReferenceFilePaths, "cases[].expectedReferenceFilePaths"),
                RequireStringList(item.ExpectedEntityIds, "cases[].expectedEntityIds"),
                RequireList(item.ExpectedRelationshipPairs, "cases[].expectedRelationshipPairs")))
            .ToArray();

        Validate(dataDirectory, questions, documentOracleByQuestion, chunks, entities, relationships, cases);

        return new ApiRetrievalEvaluationDataSet(chunks, entities, relationships, cases);
    }

    private static void Validate(
        string dataDirectory,
        IReadOnlySet<string> questions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> documentOracleByQuestion,
        IReadOnlyList<ApiRetrievalChunkSpec> chunks,
        IReadOnlyList<ApiRetrievalEntitySpec> entities,
        IReadOnlyList<ApiRetrievalRelationshipSpec> relationships,
        IReadOnlyList<ApiRetrievalEvaluationCase> cases)
    {
        var sampleDocumentsDirectory = Path.Combine(dataDirectory, "sample_documents");
        var chunkIds = chunks.Select(chunk => chunk.Id).ToHashSet(StringComparer.Ordinal);
        var chunkById = chunks.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        var entityIds = entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);
        var relationshipPairs = relationships
            .Select(relationship => PairKey(relationship.SourceId, relationship.TargetId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var documentName in documentOracleByQuestion.Values.SelectMany(static item => item).Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(sampleDocumentsDirectory, documentName)))
            {
                throw new InvalidOperationException($"Expected sample document '{documentName}' was not found.");
            }
        }

        foreach (var chunk in chunks)
        {
            if (!File.Exists(Path.Combine(sampleDocumentsDirectory, chunk.DocumentName)))
            {
                throw new InvalidOperationException($"Chunk '{chunk.Id}' references unknown document '{chunk.DocumentName}'.");
            }
        }

        foreach (var entity in entities)
        {
            EnsureContains(chunkIds, entity.SourceId, $"Entity '{entity.Id}' sourceId");
        }

        foreach (var relationship in relationships)
        {
            EnsureContains(entityIds, relationship.SourceId, "Relationship sourceId");
            EnsureContains(entityIds, relationship.TargetId, "Relationship targetId");
            foreach (var sourceId in relationship.SourceIdList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                EnsureContains(chunkIds, sourceId, "Relationship sourceIdList");
            }
        }

        foreach (var evaluationCase in cases)
        {
            EnsureContains(questions, evaluationCase.Question, $"Case '{evaluationCase.Name}' question");
            if (!documentOracleByQuestion.ContainsKey(evaluationCase.Question))
            {
                throw new InvalidOperationException($"Case '{evaluationCase.Name}' is missing a document oracle entry.");
            }

            foreach (var chunkId in evaluationCase.ExpectedChunkIds)
            {
                EnsureContains(chunkIds, chunkId, $"Case '{evaluationCase.Name}' expectedChunkIds");
            }

            foreach (var filePath in evaluationCase.ExpectedReferenceFilePaths)
            {
                if (!evaluationCase.ExpectedChunkIds.Select(chunkId => chunkById[chunkId].FilePath).Contains(filePath, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Case '{evaluationCase.Name}' expected reference '{filePath}' does not match expected chunks.");
                }
            }

            foreach (var entityId in evaluationCase.ExpectedEntityIds)
            {
                EnsureContains(entityIds, entityId, $"Case '{evaluationCase.Name}' expectedEntityIds");
            }

            foreach (var pair in evaluationCase.ExpectedRelationshipPairs)
            {
                EnsureContains(relationshipPairs, PairKey(pair.SourceId, pair.TargetId), $"Case '{evaluationCase.Name}' expectedRelationshipPairs");
            }
        }
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Evaluation data file was not found: {path}");
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidOperationException($"Evaluation data file could not be parsed: {path}");
    }

    private static QueryMode ParseMode(string mode, string? caseName)
    {
        return Enum.TryParse<QueryMode>(mode, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unknown query mode '{mode}' in case '{caseName}'.");
    }

    private static IReadOnlyList<T> RequireList<T>(IReadOnlyList<T>? values, string fieldName)
    {
        return values ?? throw new InvalidOperationException($"{fieldName} is required.");
    }

    private static IReadOnlyList<string> RequireStringList(IReadOnlyList<string>? values, string fieldName)
    {
        return RequireList(values, fieldName)
            .Select(value => RequireString(value, fieldName))
            .ToArray();
    }

    private static string RequireString(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{fieldName} is required.")
            : value;
    }

    private static void EnsureContains(IReadOnlySet<string> knownValues, string value, string name)
    {
        if (!knownValues.Contains(value))
        {
            throw new InvalidOperationException($"{name} references unknown value '{value}'.");
        }
    }

    private static string PairKey(string sourceId, string targetId)
    {
        return $"{sourceId}->{targetId}";
    }

    private sealed class SampleDatasetJson
    {
        [JsonPropertyName("test_cases")]
        public IReadOnlyList<SampleDatasetCaseJson>? TestCases { get; init; }
    }

    private sealed class SampleDatasetCaseJson
    {
        [JsonPropertyName("question")]
        public string? Question { get; init; }
    }

    private sealed class SampleRetrievalOracleJson
    {
        [JsonPropertyName("oracle")]
        public IReadOnlyList<SampleRetrievalOracleEntryJson>? Oracle { get; init; }
    }

    private sealed class SampleRetrievalOracleEntryJson
    {
        [JsonPropertyName("question")]
        public string? Question { get; init; }

        [JsonPropertyName("expected_documents")]
        public IReadOnlyList<string>? ExpectedDocuments { get; init; }
    }

    private sealed class LightRagNetRetrievalOracleJson
    {
        [JsonPropertyName("corpus")]
        public ApiRetrievalCorpusJson? Corpus { get; init; }

        [JsonPropertyName("cases")]
        public IReadOnlyList<ApiRetrievalCaseJson>? Cases { get; init; }
    }

    private sealed class ApiRetrievalCorpusJson
    {
        [JsonPropertyName("chunks")]
        public IReadOnlyList<ApiRetrievalChunkJson>? Chunks { get; init; }

        [JsonPropertyName("entities")]
        public IReadOnlyList<ApiRetrievalEntityJson>? Entities { get; init; }

        [JsonPropertyName("relationships")]
        public IReadOnlyList<ApiRetrievalRelationshipJson>? Relationships { get; init; }
    }

    private sealed class ApiRetrievalChunkJson
    {
        public string? Id { get; init; }
        public string? DocumentName { get; init; }
        public string? FilePath { get; init; }
        public string? Content { get; init; }
    }

    private sealed class ApiRetrievalEntityJson
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Description { get; init; }
        public string? SourceId { get; init; }
        public string? FilePath { get; init; }
    }

    private sealed class ApiRetrievalRelationshipJson
    {
        public string? SourceId { get; init; }
        public string? TargetId { get; init; }
        public string? Keywords { get; init; }
        public string? Description { get; init; }
        public float? Weight { get; init; }
        public string? SourceIdList { get; init; }
    }

    private sealed class ApiRetrievalCaseJson
    {
        public string? Name { get; init; }
        public string? Question { get; init; }
        public string? Mode { get; init; }
        public IReadOnlyList<string>? HighLevelKeywords { get; init; }
        public IReadOnlyList<string>? LowLevelKeywords { get; init; }
        public int? TopK { get; init; }
        public int? ChunkTopK { get; init; }
        public bool? EnableRerank { get; init; }
        public IReadOnlyList<string>? ExpectedChunkIds { get; init; }
        public IReadOnlyList<string>? ExpectedReferenceFilePaths { get; init; }
        public IReadOnlyList<string>? ExpectedEntityIds { get; init; }
        public IReadOnlyList<ExpectedRelationshipPair>? ExpectedRelationshipPairs { get; init; }
    }
}

internal sealed record ApiRetrievalEvaluationDataSet(
    IReadOnlyList<ApiRetrievalChunkSpec> Chunks,
    IReadOnlyList<ApiRetrievalEntitySpec> Entities,
    IReadOnlyList<ApiRetrievalRelationshipSpec> Relationships,
    IReadOnlyList<ApiRetrievalEvaluationCase> Cases)
{
    public ApiRetrievalEvaluationCase GetCase(string name)
    {
        return Cases.Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
    }
}

internal sealed record ApiRetrievalChunkSpec(
    string Id,
    string DocumentName,
    string FilePath,
    string Content);

internal sealed record ApiRetrievalEntitySpec(
    string Id,
    string Type,
    string Description,
    string SourceId,
    string FilePath);

internal sealed record ApiRetrievalRelationshipSpec(
    string SourceId,
    string TargetId,
    string Keywords,
    string Description,
    float Weight,
    string SourceIdList);

internal sealed record ApiRetrievalEvaluationCase(
    string Name,
    string Question,
    QueryMode Mode,
    IReadOnlyList<string> HighLevelKeywords,
    IReadOnlyList<string> LowLevelKeywords,
    int TopK,
    int ChunkTopK,
    bool EnableRerank,
    IReadOnlyList<string> ExpectedChunkIds,
    IReadOnlyList<string> ExpectedReferenceFilePaths,
    IReadOnlyList<string> ExpectedEntityIds,
    IReadOnlyList<ExpectedRelationshipPair> ExpectedRelationshipPairs);

internal sealed record ExpectedRelationshipPair(string SourceId, string TargetId);
