using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Rerank;

/// <summary>
/// Aliyun Rerank service implementation
/// </summary>
public class AliyunRerankService : IRerankService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AliyunRerankService> _logger;
    private readonly AliyunRerankOptions _options;

    public AliyunRerankService(
        HttpClient httpClient,
        ILogger<AliyunRerankService> logger,
        IOptions<AliyunRerankOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _options.ApiKey = Environment.GetEnvironmentVariable("ALiYunKey") ?? 
                              throw new ArgumentException("Configure the API key[Embedding:ApiKey] in the appsettings.json file " +
                                                          "or set the ALiYunKey environment variable.");
        }
        
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
    }

    public async Task<List<RerankResult>> RerankAsync(
        string query,
        List<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
            return [];

        if (documents.Any(x => string.IsNullOrEmpty(x) || string.IsNullOrWhiteSpace(x)))
        {
            throw new ArgumentException("rerank documents can`t include empty document");
        }

        // Aliyun Rerank API request format (use dictionary to ensure property names are lowercase)
        var request = new Dictionary<string, object>
        {
            ["model"] = _options.ModelName,
            ["input"] = new Dictionary<string, object>
            {
                ["query"] = query,
                ["documents"] = documents
            },
            ["parameters"] = new Dictionary<string, object>
            {
                ["return_documents"] = false,
                ["top_n"] = topN
            }
        };

        var json = JsonSerializer.Serialize(request);

        // Only log request summary, don't print full input content (may be very long)
        _logger.LogDebug("Rerank request: Query length={QueryLength}, Documents count={DocCount}, TopN={TopN}",
            query.Length, documents.Count, topN);

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(
            _options.BaseUrl,
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            // Only log error summary, don't print full request JSON (may be very long)
            _logger.LogError("Rerank API error: Status={StatusCode}, Response={ErrorContent}, Query length={QueryLength}, Documents count={DocCount}",
                response.StatusCode, errorContent, query.Length, documents.Count);
            throw new HttpRequestException(
                $"Rerank API returned status {response.StatusCode}: {errorContent}");
        }

        // Aliyun Rerank API response format: {"output": {"results": [...]}}
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("Rerank response: {ResponseContent}", responseContent);

        JsonElement resultsArray;
        try
        {
            var jsonDoc = JsonDocument.Parse(responseContent);

            // Try to get results from output.results
            if (jsonDoc.RootElement.TryGetProperty("output", out var output))
            {
                if (!output.TryGetProperty("results", out resultsArray))
                {
                    _logger.LogWarning("Rerank API response missing 'output.results' field");
                    return [];
                }
            }
            else if (jsonDoc.RootElement.TryGetProperty("results", out resultsArray))
            {
                // Compatible with standard format: {"results": [...]}
                _logger.LogDebug("Using standard response format (results at root level)");
            }
            else
            {
                _logger.LogWarning("Rerank API response missing 'results' field");
                return [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Rerank API response: {ResponseContent}", responseContent);
            throw new InvalidOperationException($"Failed to parse rerank response: {ex.Message}", ex);
        }

        if (resultsArray.ValueKind != JsonValueKind.Array || resultsArray.GetArrayLength() == 0)
        {
            _logger.LogWarning("Rerank API returned empty results array");
            return [];
        }

        var results = resultsArray.EnumerateArray()
            .Select(r =>
            {
                try
                {
                    return new
                    {
                        Index = r.GetProperty("index").GetInt32(),
                        RelevanceScore = r.GetProperty("relevance_score").GetDouble()
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse rerank result item: {Item}", r.ToString());
                    throw new InvalidOperationException($"Failed to parse rerank result item: {ex.Message}", ex);
                }
            })
            .ToList()!;

        // Directly use API returned index, as document list has been validated
        return results
            .OrderByDescending(r => r.RelevanceScore)
            .Take(topN)
            .Select(r => new RerankResult
            {
                Index = r.Index,
                RelevanceScore = (float)r.RelevanceScore,
            })
            .ToList();
    }
}

public class AliyunRerankOptions
{
    public string BaseUrl { get; set; } = "https://dashscope.aliyuncs.com/api/v1/services/rerank/text-rerank/text-rerank";

    public string ModelName { get; set; } = "qwen3-rerank";

    public string ApiKey { get; set; } = string.Empty;
}