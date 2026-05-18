using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentDeletionApiTests
{
    [Fact]
    public async Task DeleteTaskFailure_KeepsMarkdownRowAndMarksDeletionFailed()
    {
        using var factory = new LightRagServerFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 77,
            FileName = "delete.md",
            Content = "content",
            IsInRagSystem = true,
            RagDocumentId = "doc-delete",
            RagStatus = "Deleting"
        });
        await context.SaveChangesAsync();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationHandler<RagTaskStatusChangedEvent>>();

        await handler.Handle(new RagTaskStatusChangedEvent(new RagTask
        {
            DocumentId = 77,
            RagDocumentId = "doc-delete",
            OperationType = RagTaskOperationType.DeleteDocument,
            Status = RagTaskStatus.Failed,
            ErrorMessage = "delete failed"
        }), CancellationToken.None);

        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(77);
        document.Should().NotBeNull();
        document!.RagStatus.Should().Be("DeletionFailed");
        document.RagErrorMessage.Should().Be("delete failed");
    }
}
