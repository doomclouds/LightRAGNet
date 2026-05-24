using System.Net;
using FluentAssertions;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentPreviewControllerTests
{
    [Fact]
    public async Task PreviewPage_WhenDocumentExists_RendersDarkPageWithPathBaseAwareApiUrls()
    {
        using var factory = new LightRagServerFactory(services =>
        {
            services.AddSingleton<IStartupFilter>(new PathBaseStartupFilter("/ragbase"));
        });
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 101,
            FileName = "fallback.md",
            OriginalFileName = "<unsafe title>.pdf",
            Content = "# Fallback"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ragbase/document-preview/101");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        html.Should().Contain("#0d1117");
        html.Should().Contain("&lt;unsafe title&gt;.pdf");
        html.Should().Contain("/ragbase/api/document-preview/101/content");
        html.Should().Contain("/ragbase/api/document-preview/101/original");
        html.Should().NotContain("<unsafe title>.pdf");
    }

    [Fact]
    public async Task ContentPreview_WhenConvertedMarkdownExists_ReturnsConvertedMarkdownBeforeDocumentContent()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentWithConvertedMarkdownAsync(factory, 102, "# Converted", "# Stored content");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/document-preview/102/content");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/markdown");
        content.Should().Be("# Converted");
    }

    [Fact]
    public async Task ContentPreview_WhenConvertedMarkdownMissing_ReturnsDocumentContent()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 103,
            FileName = "fallback.md",
            Content = "# Fallback content",
            ConvertedMarkdownPath = Path.Combine("documents", "103", "missing.md")
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/document-preview/103/content");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/markdown");
        content.Should().Be("# Fallback content");
    }

    [Fact]
    public async Task OriginalPreview_WhenOriginalArtifactExists_ReturnsContentTypeFilenameAndRange()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentWithOriginalAsync(factory, 104, "contract.pdf", "pdf bytes"u8.ToArray());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/document-preview/104/original");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 2);

        var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().NotBe("attachment");
        response.Headers.AcceptRanges.Should().Contain("bytes");
        bytes.Should().Equal("pdf"u8.ToArray());
    }

    [Fact]
    public async Task ContentPreview_WhenDocumentDoesNotExist_ReturnsNotFound()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/document-preview/404/content");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OriginalPreview_WhenArtifactIsMissing_ReturnsNotFound()
    {
        using var factory = new LightRagServerFactory();
        await SeedDocumentAsync(factory, new MarkdownDocument
        {
            Id = 105,
            FileName = "missing.pdf",
            OriginalFileName = "missing.pdf",
            OriginalFilePath = Path.Combine("documents", "105", "original.pdf")
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/document-preview/105/original");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task SeedDocumentWithConvertedMarkdownAsync(
        LightRagServerFactory factory,
        int documentId,
        string convertedMarkdown,
        string storedContent)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
        var converted = await store.SaveConvertedMarkdownAsync(documentId, convertedMarkdown, CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = documentId,
            FileName = $"document-{documentId}.pdf",
            Content = storedContent,
            ConvertedMarkdownPath = converted.RelativePath
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedDocumentWithOriginalAsync(
        LightRagServerFactory factory,
        int documentId,
        string originalFileName,
        byte[] bytes)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
        await using var stream = new MemoryStream(bytes);
        var original = await store.SaveOriginalAsync(documentId, stream, originalFileName, CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = documentId,
            FileName = originalFileName,
            OriginalFileName = originalFileName,
            OriginalFilePath = original.RelativePath
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedDocumentAsync(LightRagServerFactory factory, MarkdownDocument document)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();
    }

    private sealed class PathBaseStartupFilter(string pathBase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UsePathBase(pathBase);
                next(app);
            };
        }
    }
}
