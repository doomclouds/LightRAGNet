using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationDataLoader(IOptions<RagasEvaluationOptions> options)
{
    public async Task<RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>> LoadCasesAsync(
        IReadOnlyList<string> caseNames,
        int? maxCases,
        CancellationToken cancellationToken)
    {
        if (maxCases <= 0)
        {
            return RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>.Fail(
                "invalid_max_cases",
                "maxCases must be greater than 0.",
                StatusCodes.Status400BadRequest);
        }

        if (maxCases > options.Value.MaxCasesPerRun)
        {
            return RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>.Fail(
                "max_cases_exceeded",
                $"maxCases cannot exceed {options.Value.MaxCasesPerRun}.",
                StatusCodes.Status400BadRequest);
        }

        var cases = await LoadDatasetAsync(cancellationToken);
        var filteredCases = FilterCases(cases, caseNames);
        if (filteredCases is null)
        {
            return RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>.Fail(
                "unknown_case",
                "One or more requested cases were not found.",
                StatusCodes.Status400BadRequest);
        }

        var take = maxCases ?? options.Value.MaxCasesPerRun;
        return RagasEvaluationOperationResult<IReadOnlyList<RagasDatasetCase>>.Ok(
            filteredCases.Take(take).ToArray());
    }

    private static async Task<IReadOnlyList<RagasDatasetCase>> LoadDatasetAsync(CancellationToken cancellationToken)
    {
        var dataPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Evaluation",
            "Data",
            "sample_dataset.json");

        await using var stream = File.OpenRead(dataPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("test_cases", out var testCases)
            || testCases.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return testCases.EnumerateArray()
            .Select(static (item, index) => new RagasDatasetCase(
                BuildCaseName(item, index),
                GetString(item, "question"),
                GetString(item, "ground_truth"),
                GetString(item, "project")))
            .ToArray();
    }

    private static IReadOnlyList<RagasDatasetCase>? FilterCases(
        IReadOnlyList<RagasDatasetCase> cases,
        IReadOnlyList<string>? caseNames)
    {
        if (caseNames is null or { Count: 0 })
        {
            return cases;
        }

        var casesByName = cases.ToDictionary(static item => item.CaseName, StringComparer.Ordinal);
        var filteredCases = new List<RagasDatasetCase>(caseNames.Count);
        foreach (var caseName in caseNames)
        {
            if (!casesByName.TryGetValue(caseName, out var datasetCase))
            {
                return null;
            }

            filteredCases.Add(datasetCase);
        }

        return filteredCases;
    }

    private static string BuildCaseName(JsonElement item, int index)
    {
        if (item.TryGetProperty("case_name", out var caseName) && caseName.ValueKind == JsonValueKind.String)
        {
            return caseName.GetString() ?? $"case-{index + 1}";
        }

        if (item.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.String)
        {
            return $"case-{index + 1}-{Slug(question.GetString() ?? string.Empty)}";
        }

        return $"case-{index + 1}";
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingDash = false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingDash = false;
            }
            else
            {
                pendingDash = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
