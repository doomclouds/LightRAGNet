using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Share.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentIntakePipelineApiTests
{
    [Fact]
    public async Task SubmitTextDocuments_CreatesSingleTrackAndQueuedDocuments()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/MarkdownDocuments/text", new SubmitTextDocumentsRequest
        {
            Documents =
            [
                new TextDocumentInput { FileName = "a.md", Content = "alpha" },
                new TextDocumentInput { FileName = "b.md", Content = "beta" }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
        body.Should().NotBeNull();
        body!.TrackId.Should().NotBeNullOrWhiteSpace();
        body.Documents.Should().HaveCount(2);
        body.Documents.Select(d => d.TrackId).Should().OnlyContain(id => id == body.TrackId);
        body.Documents.Select(d => d.RagStatus).Should().OnlyContain(status => status == "Queued");
    }

    [Fact]
    public async Task GetTrackStatus_ReturnsAllDocumentsAndAggregatesCounts()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 201,
            FileName = "done.md",
            Content = "done",
            TrackId = "track-201",
            RagStatus = "Completed"
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 202,
            FileName = "failed.md",
            Content = "failed",
            TrackId = "track-201",
            RagStatus = "Failed"
        });
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<DocumentTrackStatusResponse>(
            "/api/MarkdownDocuments/tracks/track-201");

        body.Should().NotBeNull();
        body!.TrackId.Should().Be("track-201");
        body.TotalCount.Should().Be(2);
        body.CompletedCount.Should().Be(1);
        body.FailedCount.Should().Be(1);
        body.Documents.Select(d => d.Id).Should().BeEquivalentTo([201, 202]);
    }

    [Fact]
    public async Task GetMarkdownDocuments_WhenStatusAndTrackExist_ReturnsPipelineMetadata()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 101,
            FileName = "alpha.md",
            Content = "alpha",
            FileSize = 5,
            TrackId = "track-alpha",
            RagStatus = "Queued",
            RagCurrentStage = "Accepted",
            ActiveRagTaskId = "task-alpha",
            RagRetryCount = 2
        });
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
            "/api/MarkdownDocuments?page=1&pageSize=10");

        result.Should().NotBeNull();
        var document = result!.Items.Should().ContainSingle(d => d.Id == 101).Subject;
        document.TrackId.Should().Be("track-alpha");
        document.RagStatus.Should().Be("Queued");
        document.RagCurrentStage.Should().Be("Accepted");
        document.ActiveRagTaskId.Should().Be("task-alpha");
        document.RagRetryCount.Should().Be(2);
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }
}
