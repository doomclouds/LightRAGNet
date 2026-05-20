using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Tests.TaskQueue;

public sealed class RagTaskStateStoreTests
{
    [Fact]
    public async Task SaveTaskStateAsync_WhenStateFileIsTemporarilyLocked_RetriesUntilItCanReplaceFile()
    {
        var workingDir = CreateTempDirectory();
        await using var cleanup = new TempDirectoryCleanup(workingDir);
        var store = CreateStore(workingDir);
        await store.SaveTaskStateAsync(CreateTask("task-initial", progress: 10));
        var tasksFilePath = Path.Combine(workingDir, "tasks.json");

        await using var lockStream = new FileStream(
            tasksFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var saveTask = store.SaveTaskStateAsync(CreateTask("task-updated", progress: 70));
        await Task.Delay(150);
        await lockStream.DisposeAsync();

        await saveTask;
        var tasks = await store.LoadAllTasksAsync();

        tasks.Should().Contain(t => t.TaskId == "task-updated" && t.Progress == 70);
    }

    [Fact]
    public async Task SaveTaskStateAsync_PersistsChineseTextWithoutUnicodeEscapes()
    {
        var workingDir = CreateTempDirectory();
        await using var cleanup = new TempDirectoryCleanup(workingDir);
        var store = CreateStore(workingDir);
        var task = CreateTask("task-chinese", progress: 30);
        task.Content = "请用100字简述采集流程";
        task.FilePath = "线性修正业务说明.md";
        task.ErrorMessage = "错误：没有查到上下文";

        await store.SaveTaskStateAsync(task);

        var json = await File.ReadAllTextAsync(Path.Combine(workingDir, "tasks.json"));
        json.Should().Contain("请用100字简述采集流程");
        json.Should().Contain("线性修正业务说明.md");
        json.Should().Contain("错误：没有查到上下文");
        json.Should().NotContain("\\u8BF7");
        json.Should().NotContain("\\u7EBF");
        json.Should().NotContain("\\u9519");
    }

    private static RagTaskStateStore CreateStore(string workingDir)
    {
        return new RagTaskStateStore(
            Options.Create(new LightRAGOptions
            {
                WorkingDir = workingDir
            }),
            NullLogger<RagTaskStateStore>.Instance);
    }

    private static RagTask CreateTask(string taskId, int progress)
    {
        return new RagTask
        {
            TaskId = taskId,
            DocumentId = 7,
            Content = "content",
            FilePath = "file.md",
            Status = RagTaskStatus.Processing,
            CurrentStage = TaskStage.MergingRelations,
            Progress = progress
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightragnet-task-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TempDirectoryCleanup(string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
