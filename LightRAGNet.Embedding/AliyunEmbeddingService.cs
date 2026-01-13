using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Embedding;

/// <summary>
/// Aliyun Embedding service implementation
/// Reference: Python version embedding implementation
/// </summary>
public class AliyunEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AliyunEmbeddingService> _logger;
    private readonly AliyunEmbeddingOptions _options;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int Delay = 100;
    private const int MaxThreads = 5;
    private readonly TimeSpan _minRequestInterval = TimeSpan.FromMilliseconds(Delay); // Minimum request interval 100ms

    public int EmbeddingDimension => _options.Dimension;
    public int MaxTokenSize => _options.MaxTokenSize;

    public AliyunEmbeddingService(
        HttpClient httpClient,
        ILogger<AliyunEmbeddingService> logger,
        IOptions<AliyunEmbeddingOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _rateLimitSemaphore = new SemaphoreSlim(1, MaxThreads); // Limit concurrent requests

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _options.ApiKey = Environment.GetEnvironmentVariable("ALiYunKey") ??
                              throw new ArgumentException("Configure the API key[Rerank:ApiKey] in the appsettings.json file " +
                                                          "or set the ALiYunKey environment variable.");
        }

        // Set authentication header
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync([text], cancellationToken);
        return embeddings.FirstOrDefault() ?? [];
    }

    public async Task<float[][]> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textsList = texts.ToArray();
        if (textsList.Length == 0)
            return [];

        // Batch processing, maximum 10 texts per batch (Aliyun API limit: string list supports up to 10 items)
        const int batchSize = 10;
        var batches = textsList
            .Select((text, index) => new { text, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.text).ToArray())
            .ToArray();

        var allEmbeddings = new List<float[]>();

        foreach (var batch in batches)
        {
            var batchEmbeddings = await GenerateEmbeddingsBatchWithRetryAsync(
                batch,
                cancellationToken);
            allEmbeddings.AddRange(batchEmbeddings);

            // Add delay between batches to avoid rate limiting
            if (batches.Length > 1)
            {
                await Task.Delay(Delay, cancellationToken);
            }
        }

        return allEmbeddings.ToArray();
    }

    private async Task<float[][]> GenerateEmbeddingsBatchWithRetryAsync(
        string[] texts,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(0.5);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Rate limiting: ensure request interval
                await _rateLimitSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                    if (timeSinceLastRequest < _minRequestInterval)
                    {
                        var delay = _minRequestInterval - timeSinceLastRequest;
                        await Task.Delay(delay, cancellationToken);
                    }

                    return await GenerateEmbeddingsBatchAsync(texts, cancellationToken);
                }
                finally
                {
                    _lastRequestTime = DateTime.UtcNow;
                    _rateLimitSemaphore.Release();
                }
            }
            catch (HttpRequestException ex) when (attempt < maxRetries - 1)
            {
                // Check if it's a 429 error
                if (ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests"))
                {
                    var waitTime = retryDelay * (attempt + 1); // Exponential backoff
                    _logger.LogWarning(
                        "Rate limit exceeded (429). Retrying after {WaitTime}ms (attempt {Attempt}/{MaxRetries})",
                        waitTime.TotalMilliseconds, attempt + 1, maxRetries);

                    await Task.Delay(waitTime, cancellationToken);
                    continue;
                }

                // Other HTTP errors, also retry
                _logger.LogWarning(
                    ex,
                    "HTTP request failed. Retrying after {WaitTime}ms (attempt {Attempt}/{MaxRetries})",
                    retryDelay.TotalMilliseconds, attempt + 1, maxRetries);

                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (TaskCanceledException) when (attempt < maxRetries - 1)
            {
                // Timeout error, retry
                _logger.LogWarning(
                    "Request timeout. Retrying after {WaitTime}ms (attempt {Attempt}/{MaxRetries})",
                    retryDelay.TotalMilliseconds, attempt + 1, maxRetries);

                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        // Last attempt
        await _rateLimitSemaphore.WaitAsync(cancellationToken);
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest < _minRequestInterval)
            {
                var delay = _minRequestInterval - timeSinceLastRequest;
                await Task.Delay(delay, cancellationToken);
            }

            return await GenerateEmbeddingsBatchAsync(texts, cancellationToken);
        }
        finally
        {
            _lastRequestTime = DateTime.UtcNow;
            _rateLimitSemaphore.Release();
        }
    }

    private async Task<float[][]> GenerateEmbeddingsBatchAsync(
        string[] texts,
        CancellationToken cancellationToken)
    {
        if (texts.Any(x => string.IsNullOrEmpty(x) || string.IsNullOrWhiteSpace(x)))
        {
            throw new ArgumentException("embedding texts can`t include empty text");
        }

        // Aliyun Embedding API request format (OpenAI compatible mode)
        // Reference: https://bailian.console.aliyun.com/
        // - String list supports up to 10 items, each up to 8,192 tokens
        // - dimension parameter (singular) is optional, text-embedding-v4 supports 64-2048, default 1024
        // - input should always be in array format
        var request = new Dictionary<string, object>
        {
            ["model"] = _options.ModelName,
            ["input"] = texts
        };

        // Add dimension parameter (if dimension is specified)
        if (_options.Dimension > 0)
        {
            request["dimension"] = _options.Dimension;
        }

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl}/v1/embeddings",
            content,
            cancellationToken);

        // Read error response content (if any)
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Embedding API request failed with status {StatusCode}. Response: {ErrorContent}",
                response.StatusCode,
                errorContent);
        }

        // Check for 429 error
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            _logger.LogWarning(
                "Rate limit exceeded (429). Retry-After: {RetryAfter}",
                retryAfter);
            throw new HttpRequestException(
                $"Rate limit exceeded (429). Retry after {retryAfter.TotalSeconds} seconds.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Data == null)
            throw new InvalidOperationException("Failed to get embedding response");

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding.Select(e => (float)e).ToArray())
            .ToArray();
    }
}

public class AliyunEmbeddingOptions
{
    public string BaseUrl { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "text-embedding-v4";
    public int Dimension { get; set; } = 2048;
    public int MaxTokenSize { get; set; } = 8192;
}

internal class EmbeddingResponse
{
    [JsonPropertyName("data")] public EmbeddingData[]? Data { get; set; }
}

internal class EmbeddingData
{
    [JsonPropertyName("index")] public int Index { get; set; }

    [JsonPropertyName("embedding")] public double[] Embedding { get; set; } = [];
}