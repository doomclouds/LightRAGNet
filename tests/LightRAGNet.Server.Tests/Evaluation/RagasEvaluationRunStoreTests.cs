using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationRunStoreTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_WhenSavedAndStoreIsRecreated_ReloadsRun()
    {
        var saved = CreateRun("run-1", RagasEvaluationRunStatus.Completed, maxCases: 3);
        var store = CreateStore();

        await store.UpsertAsync(saved, CancellationToken.None);
        var reloadedStore = CreateStore();

        var reloaded = await reloadedStore.GetAsync(saved.RunId, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.RunId.Should().Be(saved.RunId);
        reloaded.Status.Should().Be(RagasEvaluationRunStatus.Completed);
        reloaded.CreatedAt.Should().Be(saved.CreatedAt);
        reloaded.Request.MaxCases.Should().Be(3);
    }

    [Fact]
    public async Task GetAsync_WhenRunIsMissing_ReturnsNull()
    {
        var store = CreateStore();

        var run = await store.GetAsync("missing", CancellationToken.None);

        run.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_WhenRunIsQueued_ReturnsRun()
    {
        await AssertActiveRunAsync(RagasEvaluationRunStatus.Queued);
    }

    [Fact]
    public async Task GetActiveAsync_WhenRunIsRunning_ReturnsRun()
    {
        await AssertActiveRunAsync(RagasEvaluationRunStatus.Running);
    }

    [Fact]
    public async Task GetActiveAsync_WhenRunIsCompleted_ReturnsNull()
    {
        var store = CreateStore();
        await store.UpsertAsync(CreateRun("run-completed", RagasEvaluationRunStatus.Completed), CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);

        active.Should().BeNull();
    }

    [Fact]
    public void StoreSource_UpsertUsesOverwriteMoveWithoutDeletingExistingFile()
    {
        var source = File.ReadAllText(GetStoreSourcePath());

        source.Should().NotContain("File.Delete(filePath)");
        source.Should().Contain("File.Move(tempPath, filePath, overwrite: true)");
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private RagasEvaluationRunStore CreateStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LightRAG:WorkingDir"] = tempDirectory
            })
            .Build();

        return new RagasEvaluationRunStore(configuration);
    }

    private static RagasEvaluationRunRecord CreateRun(
        string runId,
        RagasEvaluationRunStatus status,
        int maxCases = 1)
    {
        return new RagasEvaluationRunRecord
        {
            RunId = runId,
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            Request = new RagasEvaluationRequestSnapshot
            {
                MaxCases = maxCases
            }
        };
    }

    private async Task AssertActiveRunAsync(RagasEvaluationRunStatus status)
    {
        var run = CreateRun($"run-{status}", status);
        var store = CreateStore();
        await store.UpsertAsync(run, CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);

        active.Should().NotBeNull();
        active!.RunId.Should().Be(run.RunId);
        active.Status.Should().Be(status);
    }

    private static string GetStoreSourcePath([CallerFilePath] string testSourcePath = "")
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testSourcePath)!,
            "..",
            "..",
            "..",
            "src",
            "LightRAGNet.Server",
            "Services",
            "Evaluation",
            "RagasEvaluationRunStore.cs"));
    }
}
