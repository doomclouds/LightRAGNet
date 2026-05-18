namespace LightRAGNet.Services.DocumentLifecycle;

public sealed record DocumentIngestionResult(
    string DocId,
    string Workspace,
    bool IsDuplicate,
    DocumentStatusRecord StatusRecord);
