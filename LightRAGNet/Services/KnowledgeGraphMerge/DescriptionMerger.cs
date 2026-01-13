using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

/// <summary>
/// Description merge strategy implementation (Map-Reduce strategy)
/// Reference: operate.py _handle_entity_relation_summary function
/// </summary>
internal class DescriptionMerger(
    ILLMService llmService,
    ITokenizer tokenizer,
    IOptions<LightRAGOptions> options,
    ILogger<DescriptionMerger> logger)
{
    private readonly LightRAGOptions _options = options.Value;

    private const string GraphFieldSep = "<SEP>";

    public async Task<(string Description, bool LlmWasUsed)> MergeAsync(
        string descriptionType,
        string descriptionName,
        List<string> descriptions,
        CancellationToken cancellationToken = default)
    {
        // Handle empty input
        if (descriptions.Count == 0)
            return (string.Empty, false);
        
        // If only one description, return it directly (no need for LLM call)
        if (descriptions.Count == 1)
            return (descriptions[0], false);
        
        // Copy the list to avoid modifying original
        var currentList = new List<string>(descriptions);
        var llmWasUsed = false; // Track whether LLM was used during the entire process
        
        // Iterative map-reduce process (consistent with Python version, using while loop instead of recursion)
        while (true)
        {
            // Calculate total tokens in current list
            var totalTokens = currentList.Sum(tokenizer.CountTokens);
            
            // If total length is within limits, perform final summarization
            if (totalTokens <= _options.SummaryContextSize || currentList.Count <= 2)
            {
                if (currentList.Count < _options.ForceLLMSummaryOnMerge &&
                    totalTokens < _options.SummaryMaxTokens)
                {
                    // no LLM needed, just join the descriptions
                    var finalDescription = string.Join(GraphFieldSep, currentList);
                    return (finalDescription, llmWasUsed);
                }

                if (totalTokens > _options.SummaryContextSize && currentList.Count <= 2)
                {
                    logger.LogWarning(
                        "Summarizing {DescriptionName}: Oversize description found",
                        descriptionName);
                }
                // Final summarization of remaining descriptions - LLM will be used
                var finalSummary = await llmService.SummarizeAsync(
                    descriptionType,
                    descriptionName,
                    currentList,
                    _options.SummaryLengthRecommended,
                    cancellationToken: cancellationToken);
                return (finalSummary, true); // LLM was used for final summarization
            }
            
            // Need to split into chunks - Map phase
            // Ensure each chunk has minimum 2 descriptions to guarantee progress
            var chunks = SplitIntoChunks(currentList, descriptionName);
            
            logger.LogInformation(
                "Summarizing {DescriptionName}: Map {Count} descriptions into {ChunkCount} groups",
                descriptionName,
                currentList.Count,
                chunks.Count);
            
            // Reduce phase: summarize each group from chunks
            var newSummaries = new List<string>();
            foreach (var chunk in chunks)
            {
                if (chunk.Count == 1)
                {
                    // Optimization: single description chunks don't need LLM summarization
                    newSummaries.Add(chunk[0]);
                }
                else
                {
                    // Multiple descriptions need LLM summarization
                    var summary = await llmService.SummarizeAsync(
                        descriptionType,
                        descriptionName,
                        chunk,
                        _options.SummaryLengthRecommended,
                        cancellationToken: cancellationToken);
                    newSummaries.Add(summary);
                    llmWasUsed = true; // Mark that LLM was used in reduce phase
                }
            }
            
            // Update current list with new summaries for next iteration
            currentList = newSummaries;
        }
    }
    
    private List<List<string>> SplitIntoChunks(List<string> descriptions, string descriptionName)
    {
        // Ensure each chunk has minimum 2 descriptions to guarantee progress
        var chunks = new List<List<string>>();
        var currentChunk = new List<string>();
        var currentTokens = 0;
        
        // Currently at least 3 descriptions in descriptions (from while condition)
        foreach (var desc in descriptions)
        {
            var descTokens = tokenizer.CountTokens(desc);
            
            // If adding current description would exceed limit, finalize current chunk
            if (currentTokens + descTokens > _options.SummaryContextSize && currentChunk.Count > 0)
            {
                // Ensure we have at least 2 descriptions in the chunk (when possible)
                if (currentChunk.Count == 1)
                {
                    // Force add one more description to ensure minimum 2 per chunk
                    currentChunk.Add(desc);
                    chunks.Add(currentChunk);
                    logger.LogWarning(
                        "Summarizing {DescriptionName}: Oversize description found",
                        descriptionName);
                    currentChunk = []; // next group is empty
                    currentTokens = 0;
                }
                else
                {
                    // current_chunk is ready for summary in reduce phase
                    chunks.Add(currentChunk);
                    currentChunk = [desc]; // leave it for next group
                    currentTokens = descTokens;
                }
            }
            else
            {
                currentChunk.Add(desc);
                currentTokens += descTokens;
            }
        }
        
        // Add the last chunk if it exists
        if (currentChunk.Count > 0)
        {
            chunks.Add(currentChunk);
        }
        
        return chunks;
    }
}

