namespace LightRAGNet.Server.Services.CacheManagement;

public sealed record CacheOverviewResponse(
    CacheSummaryDto Summary,
    IReadOnlyList<CacheFamilyDto> Families,
    IReadOnlyList<CacheTrendPointDto> Trend,
    IReadOnlyList<CacheInsightDto> Insights,
    IReadOnlyList<CacheClearPlanDto> ClearPlans,
    IReadOnlyList<CacheEntrySampleDto> Samples);

public sealed record CacheSummaryDto(
    string Workspace,
    string Window,
    DateTimeOffset From,
    DateTimeOffset To,
    bool Measured,
    double? OverallHitRate,
    int TotalReads,
    int Hits,
    int Misses,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs,
    int InventoryEntryCount);

public sealed record CacheFamilyDto(
    string Name,
    int EntryCount,
    int Reads,
    int Hits,
    int Misses,
    bool Measured,
    double? HitRate,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs);

public sealed record CacheTrendPointDto(
    DateTimeOffset Timestamp,
    int Reads,
    int Hits,
    int Misses,
    double? HitRate);

public sealed record CacheInsightDto(
    string Severity,
    string Title,
    string Message);

public sealed record CacheClearPlanDto(
    string Id,
    string Name,
    string Risk,
    string Impact,
    string Confirmation,
    int EstimatedEntryCount,
    bool Available);

public sealed record CacheEntrySampleDto(
    string KeyPrefix,
    string CacheType,
    string State,
    string? ChunkId,
    long CreateTime);

public sealed record CacheClearRequest(
    string PlanId,
    string? Workspace,
    string? Confirmation);

public sealed record CacheClearResponse(
    bool Success,
    string Message,
    string? PlanId,
    int ClearedCount,
    IReadOnlyList<string> Errors);

public sealed record CacheInventoryEntry(
    string Key,
    string KeyPrefix,
    string CacheType,
    string State,
    string? ChunkId,
    long CreateTime);
