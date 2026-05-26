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
    IReadOnlyList<string> ExpectedDocumentNames,
    IReadOnlyList<string> ExpectedChunkIds,
    IReadOnlyList<string> ExpectedReferenceFilePaths,
    IReadOnlyList<string> ExpectedEntityIds,
    IReadOnlyList<ExpectedRelationshipPair> ExpectedRelationshipPairs,
    IReadOnlyList<string> ForbiddenChunkIds,
    bool EnableRerank,
    IReadOnlyList<string> ExpectedChunkOrder,
    IReadOnlyDictionary<string, float> VectorScoresByChunkId,
    IReadOnlyDictionary<string, float> RerankScoresByContent)
{
    public RetrievalEvaluationCase(
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
        bool EnableRerank)
        : this(
            Name,
            Query,
            Mode,
            HighLevelKeywords,
            LowLevelKeywords,
            TopK,
            ChunkTopK,
            ExpectedDocumentNames: [],
            ExpectedChunkIds,
            ExpectedReferenceFilePaths,
            ExpectedEntityIds,
            ExpectedRelationshipPairs,
            ForbiddenChunkIds,
            EnableRerank,
            ExpectedChunkOrder: [],
            VectorScoresByChunkId: new Dictionary<string, float>(StringComparer.Ordinal),
            RerankScoresByContent: new Dictionary<string, float>(StringComparer.Ordinal))
    {
    }
}

public sealed record ExpectedRelationshipPair(string SourceId, string TargetId);
