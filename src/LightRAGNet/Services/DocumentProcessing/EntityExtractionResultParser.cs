using System.Globalization;
using System.Text.RegularExpressions;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing;

internal static partial class EntityExtractionResultParser
{
    public static EntityExtractionResult Parse(string response, int maxEntities, int maxRelationships)
    {
        var result = new EntityExtractionResult();
        var cleanedResponse = RemoveThinkTags(response);
        var lines = cleanedResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Trim() == "<|COMPLETE|>")
            {
                break;
            }

            var parts = line.Split(["<|#|>"], StringSplitOptions.None);

            if (parts.Length >= 4 && parts[0].Trim() == "entity")
            {
                result.Entities.Add(new Entity
                {
                    Name = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    Type = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true)
                        .Replace(" ", string.Empty)
                        .ToLowerInvariant(),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[3])
                });
            }
            else if (parts.Length >= 5 && parts[0].Trim() == "relation")
            {
                result.Relationships.Add(new Relationship
                {
                    SourceId = TextUtils.SanitizeAndNormalizeText(parts[1], removeInnerQuotes: true),
                    TargetId = TextUtils.SanitizeAndNormalizeText(parts[2], removeInnerQuotes: true),
                    Keywords = TextUtils.SanitizeAndNormalizeText(parts[3], removeInnerQuotes: true),
                    Description = TextUtils.SanitizeAndNormalizeText(parts[4]),
                    Weight = ParseWeight(parts)
                });
            }
        }

        result.Entities = result.Entities.Take(maxEntities).ToList();
        result.Relationships = result.Relationships.Take(maxRelationships).ToList();

        return result;
    }

    private static string RemoveThinkTags(string response)
    {
        var cleaned = ThinkBlockRegex().Replace(response, string.Empty);
        return OrphanThinkClosePrefixRegex().Replace(cleaned, string.Empty);
    }

    private static float ParseWeight(string[] parts)
    {
        if (parts.Length < 6)
        {
            return 1.0f;
        }

        var weightText = parts[5].Trim().Trim('"').Trim('\'');
        return float.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWeight)
            ? parsedWeight
            : 1.0f;
    }

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex("^((?!<think>).)*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex OrphanThinkClosePrefixRegex();
}
