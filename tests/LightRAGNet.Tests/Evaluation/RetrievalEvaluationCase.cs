using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public sealed record RetrievalEvaluationCase(
    string Name,
    string Query,
    QueryMode Mode,
    IReadOnlyList<string> HighLevelKeywords,
    IReadOnlyList<string> LowLevelKeywords,
    int TopK,
    int ChunkTopK,
    IReadOnlyList<string> ExpectedChunkIds,
    IReadOnlyList<string> ExpectedReferenceFilePaths,
    IReadOnlyList<string> ExpectedEntityIds,
    IReadOnlyList<ExpectedRelationshipPair> ExpectedRelationshipPairs,
    IReadOnlyList<string> ForbiddenChunkIds,
    bool EnableRerank);

public sealed record ExpectedRelationshipPair(string SourceId, string TargetId);
