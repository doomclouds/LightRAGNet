using System.ClientModel;
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace LightRAGNet.LLM;

public class DeepSeekLLMService : ILLMService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<DeepSeekLLMService> _logger;
    private readonly SemaphoreSlim _extractEntitiesSemaphore;

    public DeepSeekLLMService(
        ILogger<DeepSeekLLMService> logger,
        IOptions<DeepSeekOptions> options)
    {
        _logger = logger;
        // Limit ExtractEntitiesAsync to max 10 concurrent requests
        _extractEntitiesSemaphore = new SemaphoreSlim(10, 10);
        var options1 = options.Value;

        if (string.IsNullOrEmpty(options1.ApiKey))
        {
            options1.ApiKey = Environment.GetEnvironmentVariable("DeepseekKey") ??
                              throw new ArgumentException("Configure the API key[LLM:ApiKey] in the appsettings.json file " +
                                                          "or set the DeepseekKey environment variable.");
        }

        var openAiClientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options1.BaseUrl),
            NetworkTimeout = TimeSpan.FromMinutes(3) // Increase timeout to 3 minutes for long-running operations
        };
        
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(options1.ApiKey),
            openAiClientOptions);
        _chatClient = openAiClient.GetChatClient(options1.ModelName).AsIChatClient();
    }

    public async Task<string> GenerateAsync(
        string prompt,
        string? systemPrompt = null,
        List<ChatMessage>? historyMessages = null,
        float temperature = 1.0f,
        bool enableCot = false,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }

        if (historyMessages != null)
        {
            messages.AddRange(historyMessages);
        }

        messages.Add(new ChatMessage(ChatRole.User, prompt));

        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { Temperature = temperature },
            cancellationToken: cancellationToken);

        return response.Text;
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        string? systemPrompt = null,
        List<ChatMessage>? historyMessages = null,
        float temperature = 1.0f,
        bool enableCot = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }

        if (historyMessages != null)
        {
            messages.AddRange(historyMessages);
        }

        messages.Add(new ChatMessage(ChatRole.User, prompt));

        await foreach (var chunk in _chatClient.GetStreamingResponseAsync(
                           messages, new ChatOptions { Temperature = temperature }, cancellationToken: cancellationToken))
        {
            yield return chunk.Text;
        }
    }

    public async Task<EntityExtractionResult> ExtractEntitiesAsync(
        string text,
        List<string> entityTypes,
        float temperature = 0.3f,
        int? maxEntities = null,
        int? maxRelationships = null,
        CancellationToken cancellationToken = default)
    {
        var textLength = text.Length;
        var startTime = DateTime.UtcNow;
        
        // Acquire semaphore to limit concurrent ExtractEntitiesAsync calls
        await _extractEntitiesSemaphore.WaitAsync(cancellationToken);
        try
        {
            var waitTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            if (waitTime > 1000)
            {
                _logger.LogWarning("Semaphore wait time was {WaitTime}ms for text length {Length}", waitTime, textLength);
            }
            
            // Use provided limits or default values (30 entities, 50 relationships)
            var adjustedMaxEntities = maxEntities ?? 30;
            var adjustedMaxRelationships = maxRelationships ?? 50;
            
            // Reference: Python version prompt.py entity_extraction_system_prompt
            // Default to use same language as input text
            var systemPrompt = BuildEntityExtractionSystemPrompt(entityTypes, adjustedMaxEntities, adjustedMaxRelationships);
            var userPrompt = BuildEntityExtractionUserPrompt(text, entityTypes, adjustedMaxEntities, adjustedMaxRelationships);

            var extractionStartTime = DateTime.UtcNow;
            var response = await GenerateAsync(
                userPrompt,
                systemPrompt,
                temperature: temperature,
                cancellationToken: cancellationToken);
            
            var extractionTime = (DateTime.UtcNow - extractionStartTime).TotalSeconds;
            if (extractionTime > 150)
            {
                _logger.LogWarning("Entity extraction took {Time}s (long operation), text length: {Length}", extractionTime, textLength);
            }

            await Task.Delay(200, cancellationToken);
            var result = ParseEntityExtractionResult(response, adjustedMaxEntities, adjustedMaxRelationships);
            
            if (result.Entities.Count > adjustedMaxEntities || 
                result.Relationships.Count > adjustedMaxRelationships)
            {
                _logger.LogWarning(
                    "Extracted entities/relationships exceeded limits. Entities: {EntityCount}/{MaxEntities}, Relationships: {RelationCount}/{MaxRelationships}. Truncating...",
                    result.Entities.Count, adjustedMaxEntities,
                    result.Relationships.Count, adjustedMaxRelationships);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExtractEntitiesAsync, text length: {Length}, wait time: {WaitTime}ms", 
                textLength, (DateTime.UtcNow - startTime).TotalMilliseconds);
            throw;
        }
        finally
        {
            _extractEntitiesSemaphore.Release();
            var totalTime = (DateTime.UtcNow - startTime).TotalSeconds;
            if (totalTime > 60)
            {
                _logger.LogWarning("Total ExtractEntitiesAsync time: {Time}s, text length: {Length}", totalTime, textLength);
            }
        }
    }

    public async Task<KeywordsResult> ExtractKeywordsAsync(
        string query,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default)
    {
        // Reference: Python version prompt.py keywords_extraction
        var prompt = BuildKeywordsExtractionPrompt(query);

        var response = await GenerateAsync(
            prompt,
            temperature: temperature,
            cancellationToken: cancellationToken);

        return ParseKeywordsResult(response);
    }

    public async Task<string> SummarizeAsync(
        string descriptionType,
        string descriptionName,
        List<string> descriptionList,
        int summaryLengthRecommended,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default)
    {
        // Reference: Python version prompt.py summarize_entity_descriptions
        var prompt = BuildSummaryPrompt(descriptionType, descriptionName, descriptionList, summaryLengthRecommended);

        return await GenerateAsync(prompt, temperature: temperature, cancellationToken: cancellationToken);
    }

    private string BuildEntityExtractionSystemPrompt(List<string> entityTypes, int maxEntities, int maxRelationships)
    {
        var entityTypesStr = string.Join(", ", entityTypes);
        return $"""
                ---Role---
                You are a Knowledge Graph Specialist responsible for extracting entities and relationships from the input text.

                ---Instructions---
                1. **Entity Extraction & Output:**
                   * **Identification:** Identify clearly defined and meaningful entities in the input text. **Focus on key concepts and important entities** - for hierarchical lists, extract the main categories and most significant sub-items, not every single item.
                   * **Entity Details:** For each identified entity, extract the following information:
                       * `entity_name`: The name of the entity. If the entity name is case-insensitive, capitalize the first letter of each significant word (title case). Ensure **consistent naming** across the entire extraction process.
                       * `entity_type`: Categorize the entity using one of the following types: {entityTypesStr}. If none of the provided entity types apply, do not add new entity type and classify it as `Other`.
                       * `entity_description`: Provide a concise yet comprehensive description of the entity's attributes and activities, based *solely* on the information present in the input text. **Keep descriptions brief** (one sentence maximum, focus on key attributes).
                   * **Output Format - Entities:** Output a total of 4 fields for each entity, delimited by `<|#|>`, on a single line. The first field *must* be the literal string `entity`.
                       * Format: `entity<|#|>entity_name<|#|>entity_type<|#|>entity_description`
                   * **Priority:** Extract the most important entities first. For hierarchical structures, prioritize top-level categories and key concepts over granular sub-items.

                2. **Relationship Extraction & Output:**
                   * **Identification:** Identify direct, clearly stated, and meaningful relationships between previously extracted entities. **Focus on the most important relationships only** - avoid extracting trivial or obvious hierarchical relationships (e.g., "Software Development contains Code Generation" is too obvious and can be inferred from structure).
                   * **Relationship Details:** For each binary relationship, extract the following fields:
                       * `source_entity`: The name of the source entity. Ensure **consistent naming** with entity extraction.
                       * `target_entity`: The name of the target entity. Ensure **consistent naming** with entity extraction.
                       * `relationship_keywords`: One or more high-level keywords summarizing the overarching nature, concepts, or themes of the relationship. Multiple keywords within this field must be separated by a comma `,`. **Keep keywords concise** (1-3 words preferred).
                       * `relationship_description`: A concise explanation of the nature of the relationship between the source and target entities. **Keep descriptions brief** (one sentence maximum).
                   * **Output Format - Relationships:** Output a total of 5 fields for each relationship, delimited by `<|#|>`, on a single line. The first field *must* be the literal string `relation`.
                       * Format: `relation<|#|>source_entity<|#|>target_entity<|#|>relationship_keywords<|#|>relationship_description`
                   * **Priority:** Extract only the most meaningful relationships. Skip obvious parent-child relationships in hierarchical structures unless they represent significant conceptual connections.

                3. **Output Order & Prioritization:**
                   * Output all extracted entities first, followed by all extracted relationships.

                4. **Context & Objectivity:**
                   * Ensure all entity names and descriptions are written in the **third person**.
                   * Explicitly name the subject or object; **avoid using pronouns** such as `this article`, `this paper`, `our company`, `I`, `you`, and `he/she`.

                5. **Language & Proper Nouns:**
                   * The entire output (entity names, keywords, and descriptions) must be written in the same language as the input text.
                   * Proper nouns (e.g., personal names, place names, organization names) should be retained in their original language if a proper, widely accepted translation is not available or would cause ambiguity.

                6. **Completion Signal:** Output the literal string `<|COMPLETE|>` only after all entities and relationships have been completely extracted and outputted.
                7. **Extraction Limits:**
                   * Extract a maximum of {maxEntities} entities and {maxRelationships} relationships.
                   * Focus on the most important and meaningful ones.
                   * If the content contains many similar items (e.g., hierarchical lists), prioritize top-level categories and key concepts over granular sub-items.
                """;
    }

    private string BuildEntityExtractionUserPrompt(string text, List<string> entityTypes, int maxEntities, int maxRelationships)
    {
        var entityTypesJson = JsonSerializer.Serialize(entityTypes);
        return $"""
                ---Task---
                Extract entities and relationships from the input text in Data to be Processed below.

                ---Instructions---
                1. **Strict Adherence to Format:** Strictly adhere to all format requirements for entity and relationship lists, including output order, field delimiters, and proper noun handling, as specified in the system prompt.
                2. **Output Content Only:** Output *only* the extracted list of entities and relationships. Do not include any introductory or concluding remarks, explanations, or additional text before or after the list.
                3. **Completion Signal:** Output `<|COMPLETE|>` as the final line after all relevant entities and relationships have been extracted and presented.
                4. **Output Language:** Ensure the output language is the same as the input text. Proper nouns (e.g., personal names, place names, organization names) must be kept in their original language and not translated.
                5. **Extraction Limits:**
                   * Extract a maximum of {maxEntities} entities and {maxRelationships} relationships.
                   * If the content contains hierarchical structures or lists, prioritize the most important top-level concepts and skip redundant or overly granular items.

                ---Data to be Processed---
                <Entity_types>
                {entityTypesJson}

                <Input Text>
                ```
                {text}
                ```

                <Output>

                """;
    }

    private static EntityExtractionResult ParseEntityExtractionResult(string response, int maxEntities, int maxRelationships)
    {
        var result = new EntityExtractionResult();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Trim() == "<|COMPLETE|>")
                break;

            var parts = line.Split(["<|#|>"], StringSplitOptions.None);

            if (parts.Length >= 4 && parts[0].Trim() == "entity")
            {
                result.Entities.Add(new Entity
                {
                    Name = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    Type = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true).Replace(" ", "").ToLower(),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[3])
                });
            }
            else if (parts.Length >= 5 && parts[0].Trim() == "relation")
            {
                // Parse weight field (optional, 6th field)
                // Python version: if LLM returns 6th field and it's a valid float, use it; otherwise use default value 1.0
                float weight = 1.0f;
                if (parts.Length >= 6)
                {
                    var weightStr = parts[5].Trim().Trim('"').Trim('\'');
                    if (float.TryParse(weightStr, out var parsedWeight))
                    {
                        weight = parsedWeight;
                    }
                }

                result.Relationships.Add(new Relationship
                {
                    SourceId = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    TargetId = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true),
                    Keywords = TextUtils.SanitizeAndNormalizeText(parts[3], removeInnerQuotes: true),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[4]),
                    Weight = weight // Parsed from LLM response, use default value 1.0 if not present
                });
            }
        }

        // Apply limits
        if (result.Entities.Count > maxEntities)
        {
            result.Entities = result.Entities.Take(maxEntities).ToList();
        }
        
        if (result.Relationships.Count > maxRelationships)
        {
            result.Relationships = result.Relationships.Take(maxRelationships).ToList();
        }

        return result;
    }

    private static string BuildKeywordsExtractionPrompt(string query)
    {
        return $"""
                ---Role---
                You are an expert keyword extractor, specializing in analyzing user queries for a Retrieval-Augmented Generation (RAG) system.

                ---Goal---
                Given a user query, your task is to extract two distinct types of keywords:
                1. **high_level_keywords**: for overarching concepts or themes, capturing user's core intent, the subject area, or the type of question being asked.
                2. **low_level_keywords**: for specific entities or details, identifying the specific entities, proper nouns, technical jargon, product names, or concrete items.

                ---Instructions & Constraints---
                1. **Output Format**: Your output MUST be a valid JSON object and nothing else.
                2. **Source of Truth**: All keywords must be explicitly derived from the user query.
                3. **Concise & Meaningful**: Keywords should be concise words or meaningful phrases.

                ---Real Data---
                User Query: {query}

                ---Output---
                Output:
                """;
    }

    private KeywordsResult ParseKeywordsResult(string response)
    {
        try
        {
            // Remove possible markdown code block markers
            var jsonText = response.Trim();
            if (jsonText.StartsWith("```json"))
                jsonText = jsonText[7..];
            if (jsonText.StartsWith("```"))
                jsonText = jsonText[3..];
            if (jsonText.EndsWith("```"))
                jsonText = jsonText[..^3];
            jsonText = jsonText.Trim();

            var json = JsonSerializer.Deserialize<JsonElement>(jsonText);

            return new KeywordsResult
            {
                HighLevelKeywords = json.GetProperty("high_level_keywords")
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList(),
                LowLevelKeywords = json.GetProperty("low_level_keywords")
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse keywords result");
            return new KeywordsResult();
        }
    }

    private static string BuildSummaryPrompt(
        string descriptionType,
        string descriptionName,
        List<string> descriptionList,
        int summaryLengthRecommended)
    {
        var descriptionsJson = string.Join("\n",
            descriptionList.Select(d => JsonSerializer.Serialize(new { Description = d })));

        return $"""
                ---Role---
                You are a Knowledge Graph Specialist, proficient in data curation and synthesis.

                ---Task---
                Your task is to synthesize a list of descriptions of a given entity or relation into a single, comprehensive, and cohesive summary.

                ---Instructions---
                1. Input Format: The description list is provided in JSON format.
                2. Output Format: The merged description will be returned as plain text, presented in multiple paragraphs.
                3. Comprehensiveness: The summary must integrate all key information from *every* provided description.
                4. Length Constraint: The summary's total length must not exceed {summaryLengthRecommended} tokens.

                ---Input---
                {descriptionType} Name: {descriptionName}

                Description List:

                ```
                {descriptionsJson}
                ```

                ---Output---

                """;
    }
}

public class DeepSeekOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "deepseek-chat";
}