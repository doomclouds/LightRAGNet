using System.Text.Json.Serialization;

namespace LightRAGNet.Tests.Evaluation;

internal sealed record EvaluationDatasetJson(
    [property: JsonPropertyName("test_cases")] IReadOnlyList<EvaluationTestCaseJson>? TestCases);

internal sealed record EvaluationTestCaseJson(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("ground_truth")] string? GroundTruth,
    [property: JsonPropertyName("project")] string? Project);

internal sealed record EvaluationDocumentOracleJson(
    [property: JsonPropertyName("oracle")] IReadOnlyList<EvaluationDocumentOracleEntryJson>? Oracle);

internal sealed record EvaluationDocumentOracleEntryJson(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("expected_documents")] IReadOnlyList<string>? ExpectedDocuments);

internal sealed record LightRagNetEvaluationOracleJson(
    [property: JsonPropertyName("corpus")] EvaluationCorpusJson? Corpus,
    [property: JsonPropertyName("cases")] IReadOnlyList<RetrievalEvaluationCaseJson>? Cases);

internal sealed record EvaluationCorpusJson(
    [property: JsonPropertyName("chunks")] IReadOnlyList<RetrievalEvaluationChunkJson>? Chunks,
    [property: JsonPropertyName("entities")] IReadOnlyList<RetrievalEvaluationEntityJson>? Entities,
    [property: JsonPropertyName("relationships")] IReadOnlyList<RetrievalEvaluationRelationshipJson>? Relationships);

internal sealed record RetrievalEvaluationChunkJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("documentName")] string? DocumentName,
    [property: JsonPropertyName("filePath")] string? FilePath,
    [property: JsonPropertyName("content")] string? Content);

internal sealed record RetrievalEvaluationEntityJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("sourceId")] string? SourceId,
    [property: JsonPropertyName("filePath")] string? FilePath);

internal sealed record RetrievalEvaluationRelationshipJson(
    [property: JsonPropertyName("sourceId")] string? SourceId,
    [property: JsonPropertyName("targetId")] string? TargetId,
    [property: JsonPropertyName("keywords")] string? Keywords,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("weight")] double? Weight,
    [property: JsonPropertyName("sourceIdList")] string? SourceIdList);

internal sealed record RetrievalEvaluationCaseJson(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("highLevelKeywords")] IReadOnlyList<string>? HighLevelKeywords,
    [property: JsonPropertyName("lowLevelKeywords")] IReadOnlyList<string>? LowLevelKeywords,
    [property: JsonPropertyName("topK")] int? TopK,
    [property: JsonPropertyName("chunkTopK")] int? ChunkTopK,
    [property: JsonPropertyName("enableRerank")] bool? EnableRerank,
    [property: JsonPropertyName("expectedDocumentNames")] IReadOnlyList<string>? ExpectedDocumentNames,
    [property: JsonPropertyName("expectedChunkIds")] IReadOnlyList<string>? ExpectedChunkIds,
    [property: JsonPropertyName("expectedReferenceFilePaths")] IReadOnlyList<string>? ExpectedReferenceFilePaths,
    [property: JsonPropertyName("expectedEntityIds")] IReadOnlyList<string>? ExpectedEntityIds,
    [property: JsonPropertyName("expectedRelationshipPairs")] IReadOnlyList<ExpectedRelationshipPair>? ExpectedRelationshipPairs,
    [property: JsonPropertyName("forbiddenChunkIds")] IReadOnlyList<string>? ForbiddenChunkIds,
    [property: JsonPropertyName("expectedChunkOrder")] IReadOnlyList<string>? ExpectedChunkOrder,
    [property: JsonPropertyName("vectorScoresByChunkId")] IReadOnlyDictionary<string, float>? VectorScoresByChunkId,
    [property: JsonPropertyName("rerankScoresByContent")] IReadOnlyDictionary<string, float>? RerankScoresByContent);
