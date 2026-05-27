using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationRunStore(IConfiguration configuration)
{
    private readonly string filePath = GetFilePath(configuration);

    public async Task<IReadOnlyList<RagasEvaluationRunRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<RagasEvaluationRunRecord>>(
            json,
            LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums) ?? [];
    }

    public async Task<RagasEvaluationRunRecord?> GetAsync(string runId, CancellationToken cancellationToken)
    {
        var runs = await LoadAllAsync(cancellationToken);

        return runs.FirstOrDefault(run => run.RunId == runId);
    }

    public async Task<RagasEvaluationRunRecord?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var runs = await LoadAllAsync(cancellationToken);

        return runs.FirstOrDefault(run =>
            run.Status is RagasEvaluationRunStatus.Queued or RagasEvaluationRunStatus.Running);
    }

    public async Task UpsertAsync(RagasEvaluationRunRecord run, CancellationToken cancellationToken)
    {
        var runs = (await LoadAllAsync(cancellationToken)).ToList();
        var existingIndex = runs.FindIndex(existing => existing.RunId == run.RunId);
        if (existingIndex >= 0)
        {
            runs[existingIndex] = run;
        }
        else
        {
            runs.Add(run);
        }

        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Could not resolve directory for {filePath}.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(runs, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
        try
        {
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static string GetFilePath(IConfiguration configuration)
    {
        var workingDir = configuration["LightRAG:WorkingDir"] ?? "rag_storage";
        if (!Path.IsPathRooted(workingDir))
        {
            workingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workingDir);
        }

        return Path.Combine(workingDir, "evaluation", "ragas_runs.json");
    }
}
