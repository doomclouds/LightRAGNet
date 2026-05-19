using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
using LightRAGNet.Storage;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentDeletionApiTests
{
    [Fact]
    public void KVContracts_GetKVStoreNames_IncludesDocStatus()
    {
        KVContracts.GetKVStoreNames().Should().Contain(KVContracts.DocStatus);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnly_ReturnsNoContentAndRemovesRow()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 1,
            FileName = "local.md",
            Content = "content"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(1);
        document.Should().BeNull();
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnlyWithRelativeUploadsPath_ReturnsNoContentAndDeletesFile()
    {
        var fileName = CreateUniqueUploadFileName("relative");
        var filePath = await CreateUploadedFileAsync(fileName);
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 9,
                FileName = fileName,
                Content = "content",
                FileUrl = $"/uploads/{fileName}"
            });
            using var client = factory.CreateClient();

            var response = await client.DeleteAsync("/api/MarkdownDocuments/9");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnlyWithFullUploadsUrl_ReturnsNoContentAndDeletesFile()
    {
        var fileName = CreateUniqueUploadFileName("full");
        var filePath = await CreateUploadedFileAsync(fileName);
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 10,
                FileName = fileName,
                Content = "content",
                FileUrl = $"http://localhost/uploads/{fileName}"
            });
            using var client = factory.CreateClient();

            var response = await client.DeleteAsync("/api/MarkdownDocuments/10");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnlyWithNonUploadsPath_ReturnsNoContentAndKeepsUploadsFile()
    {
        var fileName = CreateUniqueUploadFileName("keep");
        var filePath = await CreateUploadedFileAsync(fileName);
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 11,
                FileName = fileName,
                Content = "content",
                FileUrl = $"/docs/{fileName}"
            });
            using var client = factory.CreateClient();

            var response = await client.DeleteAsync("/api/MarkdownDocuments/11");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnlyWithTraversalUploadsPath_ReturnsNoContentAndKeepsUploadsFile()
    {
        var fileName = CreateUniqueUploadFileName("keep-traversal");
        var filePath = await CreateUploadedFileAsync(fileName);
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 12,
                FileName = fileName,
                Content = "content",
                FileUrl = $"/uploads/../{fileName}"
            });
            using var client = factory.CreateClient();

            var response = await client.DeleteAsync("/api/MarkdownDocuments/12");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteMarkdownDocument_LocalOnlyWithExternalUploadsUrl_ReturnsNoContentAndKeepsUploadsFile()
    {
        var fileName = CreateUniqueUploadFileName("external");
        var filePath = await CreateUploadedFileAsync(fileName);
        try
        {
            using var factory = new LightRagServerFactory();
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 13,
                FileName = fileName,
                Content = "content",
                FileUrl = $"https://evil.example/uploads/{fileName}"
            });
            using var client = factory.CreateClient();

            var response = await client.DeleteAsync("/api/MarkdownDocuments/13");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Indexed_ReturnsAcceptedAndMarksDeleting()
    {
        var fileName = CreateUniqueUploadFileName("indexed");
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 2,
            FileName = fileName,
            Content = "content",
            FileUrl = $"http://localhost/uploads/{fileName}",
            IsInRagSystem = true,
            RagDocumentId = "doc-indexed",
            RagStatus = "Completed",
            RagErrorMessage = "old error"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/2?deleteLlmCache=true");
        var result = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result.Should().NotBeNull();
        result!.Accepted.Should().BeTrue();
        result.DeletedImmediately.Should().BeFalse();
        result.Status.Should().Be("Deleting");
        result.Message.Should().Be("Document deletion has been queued.");
        result.DocumentId.Should().Be(2);
        result.RagDocumentId.Should().Be("doc-indexed");
        result.TaskId.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(2);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be("Deleting");
        document.RagErrorMessage.Should().BeNull();

        var taskQueue = scope.ServiceProvider.GetRequiredService<IRagTaskQueueService>();
        var task = await taskQueue.GetTaskAsync(result.TaskId!);
        task.Should().NotBeNull();
        task!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
        task.DeleteLlmCache.Should().BeTrue();
        task.DeleteFilePath.Should().Be($"/uploads/{fileName}");
    }

    [Fact]
    public async Task DeleteMarkdownDocument_IndexedWithExternalUploadsUrl_QueuesEmptyFilePath()
    {
        var fileName = CreateUniqueUploadFileName("indexed-external");
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 17,
            FileName = fileName,
            Content = "content",
            FileUrl = $"https://evil.example/uploads/{fileName}",
            IsInRagSystem = true,
            RagDocumentId = "doc-indexed-external",
            RagStatus = "Completed"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/17");
        var result = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result.Should().NotBeNull();
        result!.TaskId.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var taskQueue = scope.ServiceProvider.GetRequiredService<IRagTaskQueueService>();
        var task = await taskQueue.GetTaskAsync(result.TaskId!);
        task.Should().NotBeNull();
        task!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
        task.DeleteFilePath.Should().BeEmpty();
        task.FilePath.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Processing_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 3,
            FileName = "processing.md",
            Content = "content",
            RagStatus = "Processing"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/3");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Pending_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 8,
            FileName = "pending.md",
            Content = "content",
            RagStatus = "Pending"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/8");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Deleting_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 4,
            FileName = "deleting.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-deleting",
            RagStatus = "Deleting"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/4");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_DeletionFailed_ReturnsAcceptedAndClearsError()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 5,
            FileName = "retry.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-retry",
            RagStatus = "DeletionFailed",
            RagErrorMessage = "previous failure"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/5");
        var result = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result.Should().NotBeNull();
        result!.Accepted.Should().BeTrue();
        result.TaskId.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await context.MarkdownDocuments.FindAsync(5);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be("Deleting");
        document.RagErrorMessage.Should().BeNull();

        var taskQueue = scope.ServiceProvider.GetRequiredService<IRagTaskQueueService>();
        var task = await taskQueue.GetTaskAsync(result.TaskId!);
        task.Should().NotBeNull();
        task!.OperationType.Should().Be(RagTaskOperationType.DeleteDocument);
        task.DeleteLlmCache.Should().BeFalse();
        task.RagDocumentId.Should().Be("doc-retry");
    }

    [Fact]
    public async Task DeleteMarkdownDocument_Missing_ReturnsNotFound()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMarkdownDocument_IndexedWithoutRagDocumentId_ReturnsConflict()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 6,
            FileName = "missing-rag-id.md",
            Content = "content",
            IsInRagSystem = true,
            RagStatus = "Completed"
        });
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/MarkdownDocuments/6");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteTaskCompleted_RemovesMarkdownRowAndUploadedFile()
    {
        var fileName = CreateUniqueUploadFileName("completed");
        var filePath = await CreateUploadedFileAsync(fileName);
        using var factory = new LightRagServerFactory();
        try
        {
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 14,
                FileName = fileName,
                Content = "content",
                FileUrl = $"/uploads/{fileName}",
                IsInRagSystem = true,
                RagDocumentId = "doc-indexed",
                RagStatus = "Deleting"
            });
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

            await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
            {
                DocumentId = 14,
                RagDocumentId = "doc-indexed",
                DeleteFilePath = $"/uploads/{fileName}",
                OperationType = RagTaskOperationType.DeleteDocument,
                Status = RagTaskStatus.Completed
            }), CancellationToken.None);

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.ChangeTracker.Clear();
            var document = await context.MarkdownDocuments.FindAsync(14);
            document.Should().BeNull();
            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteTaskCompleted_WithExternalUploadsUrl_RemovesRowButKeepsLocalFile()
    {
        var fileName = CreateUniqueUploadFileName("completed-external");
        var filePath = await CreateUploadedFileAsync(fileName);
        using var factory = new LightRagServerFactory();
        try
        {
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 15,
                FileName = fileName,
                Content = "content",
                FileUrl = $"https://evil.example/uploads/{fileName}",
                IsInRagSystem = true,
                RagDocumentId = "doc-indexed-external",
                RagStatus = "Deleting"
            });
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

            await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
            {
                DocumentId = 15,
                RagDocumentId = "doc-indexed-external",
                DeleteFilePath = null,
                OperationType = RagTaskOperationType.DeleteDocument,
                Status = RagTaskStatus.Completed
            }), CancellationToken.None);

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.ChangeTracker.Clear();
            var document = await context.MarkdownDocuments.FindAsync(15);
            document.Should().BeNull();
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DeleteTaskFailure_KeepsMarkdownRowAndUploadedFile()
    {
        var fileName = CreateUniqueUploadFileName("failed");
        var filePath = await CreateUploadedFileAsync(fileName);
        using var factory = new LightRagServerFactory();
        try
        {
            await SeedDocumentAsync(factory, new MarkdownDocument
            {
                Id = 16,
                FileName = fileName,
                Content = "content",
                FileUrl = $"/uploads/{fileName}",
                IsInRagSystem = true,
                RagDocumentId = "doc-delete-failed",
                RagStatus = "Deleting"
            });
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

            await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
            {
                DocumentId = 16,
                RagDocumentId = "doc-delete-failed",
                OperationType = RagTaskOperationType.DeleteDocument,
                Status = RagTaskStatus.Failed,
                ErrorMessage = "delete failed"
            }), CancellationToken.None);

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.ChangeTracker.Clear();
            var document = await context.MarkdownDocuments.FindAsync(16);
            document.Should().NotBeNull();
            document!.RagStatus.Should().Be("DeletionFailed");
            document.RagErrorMessage.Should().Be("delete failed");
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }

    private static async Task<string> CreateUploadedFileAsync(string fileName)
    {
        var uploadsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uploads");
        Directory.CreateDirectory(uploadsPath);
        var filePath = Path.Combine(uploadsPath, fileName);
        DeleteFileIfExists(filePath);
        await File.WriteAllTextAsync(filePath, "content");
        return filePath;
    }

    private static string CreateUniqueUploadFileName(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}.md";
    }

    private static void DeleteFileIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
