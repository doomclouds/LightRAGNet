namespace LightRAGNet.Services.DocumentDeletion;

public sealed record DocumentDeletionRequest(
    string Workspace,
    string DocId,
    IReadOnlyList<string> ChunkIds,
    bool DeleteLlmCache);
