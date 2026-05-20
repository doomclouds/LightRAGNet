using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing;

internal sealed record EntityExtractionPrompt(string UserPrompt, string SystemPrompt)
{
    public string CanonicalPrompt => string.Join(
        "\n",
        new[] { UserPrompt, SystemPrompt }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

internal static class EntityExtractionPromptBuilder
{
    public static EntityExtractionPrompt Build(
        string text,
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var normalizedEntityTypes = entityTypes
            .Select(entityType => entityType.Trim())
            .Where(entityType => !string.IsNullOrWhiteSpace(entityType))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entityType => entityType, StringComparer.Ordinal)
            .ToArray();

        return new EntityExtractionPrompt(
            BuildUserPrompt(text, normalizedEntityTypes, maxEntities, maxRelationships),
            BuildSystemPrompt(normalizedEntityTypes, maxEntities, maxRelationships));
    }

    private static string BuildSystemPrompt(
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var entityTypesStr = string.Join(", ", entityTypes);

        return $"""
                ---Role---
                You are a Knowledge Graph Specialist responsible for extracting entities and relationships from the input text.

                ---Instructions---
                1. **Entity Extraction & Output:**
                   * **Identification:** Identify clearly defined and meaningful entities in the input text. **Focus on key concepts and important entities** - for hierarchical lists, extract the main categories and most significant sub-items, not every single item.
                   * **Entity_types:** Categorize each entity using one of the following types: {entityTypesStr}. If none of the provided entity types apply, do not add new entity type and classify it as `Other`.
                   * **Entity Details:** For each identified entity, extract the following information:
                       * `entity_name`: The name of the entity. If the entity name is case-insensitive, capitalize the first letter of each significant word (title case). Ensure **consistent naming** across the entire extraction process.
                       * `entity_type`: The selected entity type.
                       * `entity_description`: Provide a concise yet comprehensive description of the entity's attributes and activities, based *solely* on the information present in the input text. **Keep descriptions brief** (one sentence maximum, focus on key attributes).
                   * **Output Format - Entities:** Output a total of 4 fields for each entity, delimited by `<|#|>`, on a single line. The first field *must* be the literal string `entity`.
                       * Format: `entity<|#|>entity_name<|#|>entity_type<|#|>entity_description`
                   * **Priority:** Extract the most important entities first. For hierarchical structures, prioritize top-level categories and key concepts over granular sub-items.

                2. **Relationship Extraction & Output:**
                   * **Identification:** Identify direct, clearly stated, and meaningful relationships between previously extracted entities. **Focus on the most important relationships only** - avoid extracting trivial or obvious hierarchical relationships.
                   * **Relationship Details:** For each binary relationship, extract the following fields:
                       * `source_entity`: The name of the source entity. Ensure **consistent naming** with entity extraction.
                       * `target_entity`: The name of the target entity. Ensure **consistent naming** with entity extraction.
                       * `relationship_keywords`: One or more high-level keywords summarizing the overarching nature, concepts, or themes of the relationship. Multiple keywords within this field must be separated by a comma `,`. **Keep keywords concise** (1-3 words preferred).
                       * `relationship_description`: A concise explanation of the nature of the relationship between the source and target entities. **Keep descriptions brief** (one sentence maximum).
                   * **Output Format - Relationships:** Output a total of 5 fields for each relationship, delimited by `<|#|>`, on a single line. The first field *must* be the literal string `relation`.
                       * Format: `relation<|#|>source_entity<|#|>target_entity<|#|>relationship_keywords<|#|>relationship_description`
                   * **Priority:** Extract only the most meaningful relationships.

                3. **Output Order & Prioritization:**
                   * Output all extracted entities first, followed by all extracted relationships.

                4. **Context & Objectivity:**
                   * Ensure all entity names and descriptions are written in the **third person**.
                   * Explicitly name the subject or object; **avoid using pronouns** such as `this article`, `this paper`, `our company`, `I`, `you`, and `he/she`.

                5. **Language & Proper Nouns:**
                   * The entire output (entity names, keywords, and descriptions) must be written in the same language as the input text.
                   * Proper nouns should be retained in their original language if a proper, widely accepted translation is not available or would cause ambiguity.

                6. **Completion Signal:** Output the literal string `<|COMPLETE|>` only after all entities and relationships have been completely extracted and outputted.
                7. **Extraction Limits:**
                   * Extract a maximum of {maxEntities} entities and {maxRelationships} relationships.
                   * Focus on the most important and meaningful ones.
                   * If the content contains many similar items, prioritize top-level categories and key concepts over granular sub-items.
                """;
    }

    private static string BuildUserPrompt(
        string text,
        IReadOnlyCollection<string> entityTypes,
        int maxEntities,
        int maxRelationships)
    {
        var entityTypesJson = JsonSerializer.Serialize(entityTypes, LightRAGJsonOptions.HumanReadable);

        return $"""
                ---Task---
                Extract entities and relationships from the input text in Data to be Processed below.

                ---Instructions---
                1. **Strict Adherence to Format:** Strictly adhere to all format requirements for entity and relationship lists, including output order, field delimiters, and proper noun handling, as specified in the system prompt.
                2. **Output Content Only:** Output *only* the extracted list of entities and relationships. Do not include any introductory or concluding remarks, explanations, or additional text before or after the list.
                3. **Completion Signal:** Output `<|COMPLETE|>` as the final line after all relevant entities and relationships have been extracted and presented.
                4. **Output Language:** Ensure the output language is the same as the input text. Proper nouns must be kept in their original language and not translated.
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
}
