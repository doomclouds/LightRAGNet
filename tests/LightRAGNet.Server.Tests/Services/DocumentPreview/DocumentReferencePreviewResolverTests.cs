using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentPreview;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Tests.Services.DocumentPreview;

public sealed class DocumentReferencePreviewResolverTests
{
    [Fact]
    public async Task ResolveAsync_UploadSource_ReturnsDocumentPreviewMetadata()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 11,
            FileName = "notes.md",
            FileUrl = "/uploads/notes.md",
            Content = "# Notes",
            FileSize = 7,
            UploadTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "1", FilePath = "/uploads/notes.md" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.ReferenceId.Should().Be("1");
        reference.FilePath.Should().Be("/uploads/notes.md");
        reference.FileName.Should().Be("notes.md");
        reference.PreviewUrl.Should().Be("http://localhost/document-preview/11");
        reference.OpenKind.Should().Be(ReferenceOpenKind.DocumentPreview);
    }

    [Fact]
    public async Task ResolveAsync_UploadLogicalUri_ReturnsOriginalArtifactMetadata()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 12,
            FileName = "converted.md",
            OriginalFileName = "合同.pdf",
            FileUrl = "upload://track-a/%E5%90%88%E5%90%8C.pdf",
            OriginalFilePath = Path.Combine("documents", "12", "original.pdf"),
            Content = "# Contract",
            FileSize = 10,
            UploadTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "2", FilePath = "upload://track-a/%E5%90%88%E5%90%8C.pdf" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.FileName.Should().Be("合同.pdf");
        reference.PreviewUrl.Should().Be("http://localhost/document-preview/12");
        reference.OpenKind.Should().Be(ReferenceOpenKind.OriginalArtifact);
    }

    [Fact]
    public async Task ResolveAsync_WhenUploadTracksShareFileName_ExactFileUrlWins()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.AddRange(
            new MarkdownDocument
            {
                Id = 21,
                FileName = "foo.pdf",
                FileUrl = "upload://track-a/foo.pdf",
                Content = "# Old",
                FileSize = 5,
                UploadTime = DateTime.UtcNow
            },
            new MarkdownDocument
            {
                Id = 22,
                FileName = "foo.pdf",
                FileUrl = "upload://track-b/foo.pdf",
                Content = "# New",
                FileSize = 5,
                UploadTime = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "track-b", FilePath = "upload://track-b/foo.pdf" }],
            CreateRequest(),
            CancellationToken.None);

        result.Should().ContainSingle()
            .Subject.PreviewUrl.Should().Be("http://localhost/document-preview/22");
    }

    [Fact]
    public async Task ResolveAsync_WhenOnlyBaseNameMatchesMultipleDocuments_ReturnsUnresolvedReference()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.AddRange(
            new MarkdownDocument
            {
                Id = 23,
                FileName = "foo.pdf",
                FileUrl = "upload://track-a/foo.pdf",
                Content = "# Old",
                FileSize = 5,
                UploadTime = DateTime.UtcNow
            },
            new MarkdownDocument
            {
                Id = 24,
                FileName = "foo.pdf",
                FileUrl = "upload://track-b/foo.pdf",
                Content = "# New",
                FileSize = 5,
                UploadTime = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "ambiguous", FilePath = "foo.pdf" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.FileName.Should().Be("foo.pdf");
        reference.PreviewUrl.Should().BeNull();
        reference.OpenKind.Should().Be(ReferenceOpenKind.ExternalOrUnresolved);
    }

    [Theory]
    [InlineData("../foo.pdf")]
    [InlineData(@"..\foo.pdf")]
    [InlineData("..%2Ffoo.pdf")]
    public async Task ResolveAsync_PathTraversalLikeReferenceWithSingleBaseNameMatch_ReturnsUnresolvedReference(string filePath)
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 25,
            FileName = "foo.pdf",
            FileUrl = "upload://track-a/foo.pdf",
            Content = "# Foo",
            FileSize = 5,
            UploadTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "unsafe", FilePath = filePath }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.FileName.Should().Be("foo.pdf");
        reference.PreviewUrl.Should().BeNull();
        reference.OpenKind.Should().Be(ReferenceOpenKind.ExternalOrUnresolved);
    }

    [Fact]
    public async Task ResolveAsync_UnmatchedReference_ReturnsExternalReferenceWithoutPreviewUrl()
    {
        await using var db = CreateDb();
        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "3", FilePath = "../secrets.txt" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.ReferenceId.Should().Be("3");
        reference.FilePath.Should().Be("../secrets.txt");
        reference.FileName.Should().Be("secrets.txt");
        reference.PreviewUrl.Should().BeNull();
        reference.OpenKind.Should().Be(ReferenceOpenKind.ExternalOrUnresolved);
    }

    [Fact]
    public async Task ResolveAsync_RequestPathBase_IncludesPathBaseInPreviewUrl()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 13,
            FileName = "path-base.md",
            FileUrl = "/uploads/path-base.md",
            Content = "# Path Base",
            FileSize = 11,
            UploadTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);
        var request = CreateRequest();
        request.PathBase = "/rag";

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "4", FilePath = "path-base.md" }],
            request,
            CancellationToken.None);

        result.Should().ContainSingle()
            .Subject.PreviewUrl.Should().Be("http://localhost/rag/document-preview/13");
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static HttpRequest CreateRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        return context.Request;
    }
}
