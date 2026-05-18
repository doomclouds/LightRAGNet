namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentDeletionResult(
    string DocId,
    string Workspace,
    bool Found,
    bool Succeeded,
    string Stage,
    string Message);
