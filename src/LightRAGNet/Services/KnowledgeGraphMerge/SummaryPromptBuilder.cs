using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.KnowledgeGraphMerge;

internal static class SummaryPromptBuilder
{
    public static string Build(
        string descriptionType,
        string descriptionName,
        IReadOnlyCollection<string> descriptionList,
        int summaryLengthRecommended,
        string language = "English")
    {
        var descriptionsJsonl = string.Join(
            "\n",
            descriptionList.Select(description =>
                JsonSerializer.Serialize(new { Description = description }, LightRAGJsonOptions.Compact)));

        return $"""
                ---Role---
                You are a Knowledge Graph Specialist, proficient in data curation and synthesis.

                ---Task---
                Your task is to synthesize a list of descriptions of a given entity or relation into a single, comprehensive, and cohesive summary.

                ---Instructions---
                1. Input Format: The description list is provided in JSON Lines format, one JSON object per line.
                2. Output Format: The merged description will be returned as plain text, presented in multiple paragraphs.
                3. Comprehensiveness: The summary must integrate all key information from every provided description.
                4. Length Constraint: The summary's total length must not exceed {summaryLengthRecommended} tokens.
                5. Language: Write the summary in {language}.

                ---Input---
                {descriptionType} Name: {descriptionName}

                Description List:

                ```
                {descriptionsJsonl}
                ```

                ---Output---
                """;
    }
}
