using System.Text.Json;
using LightRAGNet.Core.IO;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationRunStore(IConfiguration configuration)
{
    private readonly string filePath = GetFilePath(configuration);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<RagasEvaluationRunRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadAllUnlockedAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RagasEvaluationRunRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var runs = await LoadAllUnlockedAsync(cancellationToken);

            return runs
                .OrderByDescending(run => run.CreatedAt)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RagasEvaluationRunRecord?> GetAsync(string runId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var runs = await LoadAllUnlockedAsync(cancellationToken);

            return runs.FirstOrDefault(run => run.RunId == runId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RagasEvaluationRunRecord?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var runs = await LoadAllUnlockedAsync(cancellationToken);

            return runs.FirstOrDefault(run =>
                run.Status is RagasEvaluationRunStatus.Queued or RagasEvaluationRunStatus.Running);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAsync(RagasEvaluationRunRecord run, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var runs = (await LoadAllUnlockedAsync(cancellationToken)).ToList();
            var existingIndex = runs.FindIndex(existing => existing.RunId == run.RunId);
            if (existingIndex >= 0)
            {
                runs[existingIndex] = run;
            }
            else
            {
                runs.Add(run);
            }

            await SaveAllUnlockedAsync(runs, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<RagasEvaluationRunRecord>> LoadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<RagasEvaluationRunRecord>>(
            json,
            LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums) ?? [];
    }

    private async Task SaveAllUnlockedAsync(IReadOnlyList<RagasEvaluationRunRecord> runs, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Could not resolve directory for {filePath}.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(runs, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);
        await AtomicFileWriter.WriteAllTextAsync(filePath, json, cancellationToken: cancellationToken);
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
