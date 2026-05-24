namespace LightRAGNet.Server.Services.CacheManagement;

public sealed record CacheOverviewResponse(
    string Workspace,
    string Window,
    DateTimeOffset GeneratedAt,
    CacheSummaryDto Summary,
    IReadOnlyList<CacheFamilyDto> Families,
    IReadOnlyList<CacheTrendPointDto> Trend,
    IReadOnlyList<CacheInsightDto> Insights,
    IReadOnlyList<CacheClearPlanDto> ClearPlan,
    IReadOnlyList<CacheEntrySampleDto> EntrySamples);

public sealed record CacheSummaryDto(
    double? OverallHitRate,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs,
    int StaleOrRiskyEntries,
    bool Measured);

public sealed record CacheFamilyDto(
    string CacheType,
    string DisplayName,
    double? HitRate,
    int Hits,
    int Misses,
    int Attempts,
    int EntryCount,
    string ValueLevel,
    string RiskLevel,
    int ProviderCallsAvoided,
    long? EstimatedLatencySavedMs,
    string Message);

public sealed record CacheTrendPointDto(
    DateTimeOffset Timestamp,
    double? HitRate,
    int SavedCalls);

public sealed record CacheInsightDto(
    string Title,
    string Message,
    string Level);

public sealed record CacheClearPlanDto(
    string Id,
    string Title,
    IReadOnlyList<string> CacheTypes,
    int EntryCount,
    string Risk,
    string Impact,
    bool RequiresConfirmation);

public sealed record CacheEntrySampleDto(
    string CacheKeyPrefix,
    string CacheType,
    DateTimeOffset? LastHit,
    string State);

public sealed record CacheClearRequest(
    string? Workspace,
    string PlanId,
    bool Confirm);

public sealed record CacheClearResponse(
    bool Succeeded,
    int DeletedEntries,
    IReadOnlyList<string> CacheTypes,
    string Message,
    long? RevisionAfter);

public sealed record CacheInventoryEntry(
    string Key,
    string CacheKeyPrefix,
    string CacheType,
    string State,
    string? ChunkId,
    long CreateTime);
