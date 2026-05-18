using System.Net;
using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

public sealed class MarkdownDocumentsControllerTests
{
    [Fact]
    public async Task AddToRagSystem_WhenQueueRejectsTask_ReturnsConflictAndDoesNotMarkPending()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(new RejectingRagTaskQueueService());
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 42,
            FileName = "alpha.md",
            Content = "alpha beta",
            FileUrl = "/uploads/alpha.md"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/MarkdownDocuments/42/add-to-rag", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(42);
        document.Should().NotBeNull();
        document!.RagStatus.Should().BeNull();
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }

    private sealed class RejectingRagTaskQueueService : IRagTaskQueueService
    {
        public Task<string?> EnqueueTaskAsync(
            int documentId,
            string content,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> EnqueueDeletionTaskAsync(
            int documentId,
            string ragDocumentId,
            string filePath,
            bool deleteLlmCache,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<RagTask>());
        }

        public Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RagTask?>(null);
        }

        public Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(
            IEnumerable<int> documentIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Dictionary<int, RagTask>());
        }

        public Task UpdateTaskStatusAsync(
            string taskId,
            RagTaskStatus status,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTaskProgressAsync(
            string taskId,
            TaskStage? stage,
            int? progress,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
