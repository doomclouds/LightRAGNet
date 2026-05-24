using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.SystemHealth;

public sealed class SystemHealthService
{
    private static readonly string[] CheckOrder =
    [
        "server-api",
        "sqlite",
        "working-dir",
        "qdrant",
        "neo4j",
        "llm-config",
        "embedding-config",
        "rerank-config",
        "rag-task-queue",
        "document-conversion-queue"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FailureAffects = new Dictionary<string, IReadOnlyList<string>>
    {
        ["sqlite"] = ["Web Management"],
        ["working-dir"] = ["RAG Storage and Artifacts"],
        ["qdrant"] = ["Vector Retrieval"],
        ["neo4j"] = ["Local", "Global", "Hybrid", "Mix", "GraphWorkbench"],
        ["llm-config"] = ["LLM Generation"],
        ["embedding-config"] = ["Document Indexing"],
        ["rerank-config"] = ["Rerank Quality"],
        ["rag-task-queue"] = ["Document Indexing Queue"],
        ["document-conversion-queue"] = ["PDF/DOCX Conversion"]
    };

    private static readonly IReadOnlyDictionary<string, FeatureDefinition> FeatureDefinitions = new Dictionary<string, FeatureDefinition>
    {
        ["sqlite"] = new("Web Management", "Web Management", "/markdown-documents"),
        ["working-dir"] = new("RAG Storage and Artifacts", "RAG Storage and Artifacts", "/markdown-documents"),
        ["qdrant"] = new("Vector Retrieval", "Vector Retrieval", "/"),
        ["neo4j"] = new("KG Query Modes", "Open Graph", "/graph-view"),
        ["llm-config"] = new("LLM Generation", "LLM Generation", "/"),
        ["embedding-config"] = new("Document Indexing", "Document Indexing", "/markdown-documents"),
        ["rerank-config"] = new("Rerank Quality", "Rerank Quality", "/"),
        ["rag-task-queue"] = new("Document Indexing Queue", "Document Indexing Queue", "/markdown-documents"),
        ["document-conversion-queue"] = new("PDF/DOCX Conversion", "PDF/DOCX Conversion", "/markdown-documents")
    };

    private readonly IReadOnlyList<ISystemHealthCheck> checks;
    private readonly SystemHealthOptions options;
    private readonly ILogger<SystemHealthService> logger;

    public SystemHealthService(
        IEnumerable<ISystemHealthCheck> checks,
        IOptions<SystemHealthOptions> options,
        ILogger<SystemHealthService> logger)
    {
        this.checks = checks.ToList();
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<SystemHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var generatedAt = DateTimeOffset.UtcNow;
        var timeout = GetPerCheckTimeout();

        var results = await Task.WhenAll(checks.Select(check => RunCheckAsync(check, timeout, cancellationToken)));
        var orderedResults = results.OrderBy(result => GetCheckOrder(result.Id)).ThenBy(result => result.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var summary = BuildSummary(orderedResults);
        var status = GetOverallStatus(summary);
        var fixFirst = BuildFixFirst(orderedResults);
        var featureImpacts = BuildFeatureImpacts(orderedResults);

        return new SystemHealthResponse(
            status,
            generatedAt,
            ToDurationMs(startedAt),
            summary,
            orderedResults,
            fixFirst,
            featureImpacts);
    }

    private async Task<SystemHealthCheckResult> RunCheckAsync(
        ISystemHealthCheck check,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkTimeout.CancelAfter(timeout);

        try
        {
            var result = await check.CheckAsync(checkTimeout.Token).WaitAsync(timeout, cancellationToken);
            return result
                .WithEvidence(RedactEvidence(result.Evidence))
                .WithDuration(ToDurationMs(startedAt));
        }
        catch (TimeoutException exception)
        {
            await checkTimeout.CancelAsync();
            logger.LogWarning(exception, "System health check {CheckId} timed out.", check.Id);

            return BuildFailureResult(
                check,
                "Timeout",
                "System health check timed out.",
                "Health check timed out. Verify the dependency is reachable and responsive.",
                new Dictionary<string, object?>
                {
                    ["errorType"] = nameof(TimeoutException),
                    ["timeoutMs"] = (long)timeout.TotalMilliseconds
                },
                startedAt);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "System health check {CheckId} timed out.", check.Id);

            return BuildFailureResult(
                check,
                "Timeout",
                "System health check timed out.",
                "Health check timed out. Verify the dependency is reachable and responsive.",
                new Dictionary<string, object?>
                {
                    ["errorType"] = nameof(TimeoutException),
                    ["timeoutMs"] = (long)timeout.TotalMilliseconds
                },
                startedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "System health check {CheckId} failed.", check.Id);

            return BuildFailureResult(
                check,
                exception.GetType().Name,
                "System health check failed.",
                "Review service configuration and dependency availability.",
                new Dictionary<string, object?>
                {
                    ["errorType"] = exception.GetType().Name,
                    ["errorMessage"] = RedactSensitiveString(exception.Message)
                },
                startedAt);
        }
    }

    private SystemHealthCheckResult BuildFailureResult(
        ISystemHealthCheck check,
        string failureName,
        string message,
        string remediation,
        IReadOnlyDictionary<string, object?> evidence,
        long startedAt)
    {
        var status = GetFailureStatus(check.Id);
        var affects = FailureAffects.TryGetValue(check.Id, out var mappedAffects) ? mappedAffects : [];
        var durationMs = ToDurationMs(startedAt);

        return new SystemHealthCheckResult
        {
            Id = check.Id,
            Name = check.Name,
            Category = check.Category,
            Status = status,
            Message = $"{failureName}: {message}",
            Remediation = remediation,
            Affects = affects,
            Evidence = RedactEvidence(evidence),
            DurationMs = durationMs
        };
    }

    private TimeSpan GetPerCheckTimeout()
    {
        return options.PerCheckTimeout > TimeSpan.Zero ? options.PerCheckTimeout : TimeSpan.FromMilliseconds(1500);
    }

    private static SystemHealthSummary BuildSummary(IReadOnlyList<SystemHealthCheckResult> results)
    {
        return new SystemHealthSummary(
            results.Count(result => result.Status == SystemHealthStatus.Healthy),
            results.Count(result => result.Status == SystemHealthStatus.Degraded),
            results.Count(result => result.Status == SystemHealthStatus.Unhealthy),
            results.Count(result => result.Status == SystemHealthStatus.NotMeasured));
    }

    private static SystemHealthStatus GetOverallStatus(SystemHealthSummary summary)
    {
        if (summary.Unhealthy > 0)
        {
            return SystemHealthStatus.Unhealthy;
        }

        if (summary.Degraded > 0)
        {
            return SystemHealthStatus.Degraded;
        }

        return summary.Healthy > 0 ? SystemHealthStatus.Healthy : SystemHealthStatus.NotMeasured;
    }

    private static IReadOnlyList<SystemHealthFixFirstItem> BuildFixFirst(IReadOnlyList<SystemHealthCheckResult> results)
    {
        return results
            .Where(result => result.Status is SystemHealthStatus.Unhealthy or SystemHealthStatus.Degraded)
            .OrderBy(result => result.Status == SystemHealthStatus.Unhealthy ? 0 : 1)
            .ThenBy(result => GetCheckOrder(result.Id))
            .ThenBy(result => result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(result => new SystemHealthFixFirstItem(result.Id, result.Name, result.Status, result.Remediation, result.Affects))
            .ToList();
    }

    private static IReadOnlyList<SystemHealthFeatureImpact> BuildFeatureImpacts(IReadOnlyList<SystemHealthCheckResult> results)
    {
        return results
            .Where(result => result.Status is SystemHealthStatus.Unhealthy or SystemHealthStatus.Degraded)
            .Where(result => FeatureDefinitions.ContainsKey(result.Id))
            .GroupBy(result => FeatureDefinitions[result.Id].Feature)
            .Select(group =>
            {
                var impacted = group.ToList();
                var first = impacted[0];
                var definition = FeatureDefinitions[first.Id];
                var status = impacted.Any(result => result.Status == SystemHealthStatus.Unhealthy)
                    ? SystemHealthStatus.Unhealthy
                    : SystemHealthStatus.Degraded;

                return new SystemHealthFeatureImpact(
                    definition.Feature,
                    status,
                    string.Join("; ", impacted.Select(result => result.Message)),
                    impacted.Select(result => result.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    [new SystemHealthLink(definition.LinkLabel, definition.Href)]);
            })
            .OrderBy(impact => impact.Status == SystemHealthStatus.Unhealthy ? 0 : 1)
            .ThenBy(impact => impact.Feature, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SystemHealthStatus GetFailureStatus(string checkId)
    {
        return checkId is "neo4j" or "rerank-config" or "rag-task-queue" or "document-conversion-queue"
            ? SystemHealthStatus.Degraded
            : SystemHealthStatus.Unhealthy;
    }

    private static int GetCheckOrder(string checkId)
    {
        var index = Array.FindIndex(CheckOrder, known => string.Equals(known, checkId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    private static IReadOnlyDictionary<string, object?> RedactEvidence(IReadOnlyDictionary<string, object?> evidence)
    {
        return evidence.ToDictionary(pair => pair.Key, pair => RedactEvidenceValue(pair.Key, pair.Value), StringComparer.Ordinal);
    }

    private static object? RedactEvidenceValue(string key, object? value)
    {
        if (IsSensitiveKey(key))
        {
            return "<redacted>";
        }

        return value switch
        {
            IReadOnlyDictionary<string, object?> nested => RedactEvidence(nested),
            IDictionary<string, object?> nested => RedactEvidence(nested.AsReadOnly()),
            string text => RedactSensitiveString(text),
            _ => value
        };
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("authorization", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("connectionstring", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactSensitiveString(string value)
    {
        return Regex.Replace(
            value,
            @"(?i)\b(apiKey|apikey|password|token|authorization|connectionString)\s*=\s*[^;\s,]+",
            "$1=<redacted>");
    }

    private static long ToDurationMs(long startedAt)
    {
        return (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    private sealed record FeatureDefinition(string Feature, string LinkLabel, string Href);
}
