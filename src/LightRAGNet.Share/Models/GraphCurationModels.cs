namespace LightRAGNet.Share.Models;

public sealed record GraphEntityExistsResponse(bool Exists);

public sealed record GraphEntityCreateDto(
    string? EntityName,
    Dictionary<string, object>? EntityData);

public sealed record GraphEntityEditDto(
    Dictionary<string, object>? UpdatedData,
    bool AllowRename = true,
    bool AllowMerge = false);

public sealed record GraphRelationCreateDto(
    string? SourceEntity,
    string? TargetEntity,
    Dictionary<string, object>? RelationData);

public sealed record GraphRelationEditDto(
    string? SourceEntity,
    string? TargetEntity,
    Dictionary<string, object>? UpdatedData);

public sealed record GraphEntityMergeDto(
    IReadOnlyList<string>? SourceEntities,
    string? TargetEntity);

public sealed record GraphCurationResponse(
    bool Succeeded,
    string Status,
    string Message,
    Dictionary<string, object>? Data,
    GraphCurationSummaryDto? OperationSummary,
    string? FailureStage);

public sealed record GraphCurationSummaryDto(
    bool Merged,
    string MergeStatus,
    string? MergeError,
    string OperationStatus,
    string? TargetEntity,
    string FinalEntity,
    bool Renamed);
