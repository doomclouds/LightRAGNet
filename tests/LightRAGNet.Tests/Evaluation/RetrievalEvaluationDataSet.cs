namespace LightRAGNet.Tests.Evaluation;

public sealed record RetrievalEvaluationDataSet(
    IReadOnlyList<RetrievalEvaluationTestCase> TestCases,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DocumentOracleByQuestion,
    IReadOnlyList<RetrievalEvaluationChunkSpec> Chunks,
    IReadOnlyList<RetrievalEvaluationEntitySpec> Entities,
    IReadOnlyList<RetrievalEvaluationRelationshipSpec> Relationships,
    IReadOnlyList<RetrievalEvaluationCase> Cases);

public sealed record RetrievalEvaluationTestCase(string Question, string GroundTruth, string Project);

public sealed record RetrievalEvaluationChunkSpec(
    string Id,
    string DocumentName,
    string FilePath,
    string Content);

public sealed record RetrievalEvaluationEntitySpec(
    string Id,
    string Type,
    string Description,
    string SourceId,
    string FilePath);

public sealed record RetrievalEvaluationRelationshipSpec(
    string SourceId,
    string TargetId,
    string Keywords,
    string Description,
    double Weight,
    string SourceIdList);
