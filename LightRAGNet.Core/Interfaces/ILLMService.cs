using LightRAGNet.Core.Models;
using Microsoft.Extensions.AI;

namespace LightRAGNet.Core.Interfaces;

public interface ILLMService
{
    /// <summary>
    /// Generate text
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        string? systemPrompt = null,
        List<ChatMessage>? historyMessages = null,
        float temperature = 1.0f,
        bool enableCot = false,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate text in streaming mode
    /// </summary>
    IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        string? systemPrompt = null,
        List<ChatMessage>? historyMessages = null,
        float temperature = 1.0f,
        bool enableCot = false,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract entities and relationships
    /// </summary>
    Task<EntityExtractionResult> ExtractEntitiesAsync(
        string text,
        List<string> entityTypes,
        float temperature = 0.3f,
        int? maxEntities = null,
        int? maxRelationships = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract keywords
    /// </summary>
    Task<KeywordsResult> ExtractKeywordsAsync(
        string query,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate summary
    /// </summary>
    Task<string> SummarizeAsync(
        string descriptionType,
        string descriptionName,
        List<string> descriptionList,
        int summaryLengthRecommended,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default);
}

