namespace LightRAGNet.Server.Services.SystemHealth;

public enum SystemHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    NotMeasured
}

public sealed class SystemHealthOptions
{
    public TimeSpan PerCheckTimeout { get; set; } = TimeSpan.FromMilliseconds(1500);
}

public sealed record SystemHealthResponse(
    SystemHealthStatus Status,
    DateTimeOffset GeneratedAt,
    long DurationMs,
    SystemHealthSummary Summary,
    IReadOnlyList<SystemHealthCheckResult> Checks,
    IReadOnlyList<SystemHealthFixFirstItem> FixFirst,
    IReadOnlyList<SystemHealthFeatureImpact> FeatureImpacts);

public sealed record SystemHealthSummary(
    int Healthy,
    int Degraded,
    int Unhealthy,
    int NotMeasured);

public sealed record SystemHealthFixFirstItem(
    string CheckId,
    string Title,
    SystemHealthStatus Status,
    string Remediation,
    IReadOnlyList<string> Affects);

public sealed record SystemHealthFeatureImpact(
    string Feature,
    SystemHealthStatus Status,
    string Reason,
    IReadOnlyList<string> AffectedBy,
    IReadOnlyList<SystemHealthLink> Links);

public sealed record SystemHealthLink(string Label, string Href);

public sealed record SystemHealthCheckResult
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required SystemHealthStatus Status { get; init; }

    public required string Message { get; init; }

    public IReadOnlyDictionary<string, object?> Evidence { get; init; } = new Dictionary<string, object?>();

    public string Remediation { get; init; } = string.Empty;

    public IReadOnlyList<string> Affects { get; init; } = [];

    public long DurationMs { get; init; }

    public static SystemHealthCheckResult Healthy(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?>? evidence = null)
    {
        return Create(id, name, category, SystemHealthStatus.Healthy, message, string.Empty, [], evidence);
    }

    public static SystemHealthCheckResult Degraded(
        string id,
        string name,
        string category,
        string message,
        string remediation,
        IReadOnlyList<string>? affects = null,
        IReadOnlyDictionary<string, object?>? evidence = null)
    {
        return Create(id, name, category, SystemHealthStatus.Degraded, message, remediation, affects ?? [], evidence);
    }

    public static SystemHealthCheckResult Unhealthy(
        string id,
        string name,
        string category,
        string message,
        string remediation,
        IReadOnlyList<string>? affects = null,
        IReadOnlyDictionary<string, object?>? evidence = null)
    {
        return Create(id, name, category, SystemHealthStatus.Unhealthy, message, remediation, affects ?? [], evidence);
    }

    public static SystemHealthCheckResult NotMeasured(
        string id,
        string name,
        string category,
        string message,
        IReadOnlyDictionary<string, object?>? evidence = null)
    {
        return Create(id, name, category, SystemHealthStatus.NotMeasured, message, string.Empty, [], evidence);
    }

    public SystemHealthCheckResult WithDuration(long durationMs)
    {
        return this with { DurationMs = durationMs };
    }

    public SystemHealthCheckResult WithEvidence(IReadOnlyDictionary<string, object?> evidence)
    {
        return this with { Evidence = evidence };
    }

    private static SystemHealthCheckResult Create(
        string id,
        string name,
        string category,
        SystemHealthStatus status,
        string message,
        string remediation,
        IReadOnlyList<string> affects,
        IReadOnlyDictionary<string, object?>? evidence)
    {
        return new SystemHealthCheckResult
        {
            Id = id,
            Name = name,
            Category = category,
            Status = status,
            Message = message,
            Remediation = remediation,
            Affects = affects,
            Evidence = evidence ?? new Dictionary<string, object?>()
        };
    }
}
